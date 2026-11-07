# Reference provenance carriers

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This recommendation permits provisional
implementation of the bounded carrier below, not adoption of a specification,
allocation, runtime ABI or support claim. It supplements, without revising,
[aggregate native outputs](aggregate-native-outputs.md), their
[source dependencies](aggregate-native-output-source-dependencies.md) and
[private persistence](aggregate-native-output-private-persistence.md).

## Decision and boundary

Use **operation-derived, instance-aware provenance**, with one common typed
compiler ledger for places, values and supporting obligations. Persist
**declaration relationships and the operations that bind them**, not a graph
of purported future runtime instances. Reuse the assigned shared Dependency
clause for whole-source permissions, including capture phases; do not allocate
parallel Source, Capture or Anchor tables for those same relationships.
Recommend **private revision 93**, exactly the combined revision-92 candidate
plus the declaration collection specified below.

There are separate blockers:

| Boundary | Decision here |
|---|---|
| Declaration source/capture permissions | Existing Dependency fields are sufficient for their assigned whole-source destinations/phases. Normalize them explicitly and retain them on both definitions and expectations. |
| Actual instances, own storage versus forwarded backing, copies, transfers and joins | Select the common ledger and transfer rules below. Reconstruct it from supported typed operations after loading/remapping; source/formal rows are not runtime identities. |
| Executable roots, normal shared-backing support, pins and stored/erased descriptor anchors | **Separate unresolved physical contracts.** This proposal does not select a descriptor layout, support-handle ABI, collector/helper protocol or root/pin Body program. Reject affected use until those contracts are selected and implemented. |

This unblocks a generic provenance implementation and declaration persistence
for **already representable operations and values**. It does not make general
reference/GC persistence or execution available merely by adding a ledger.
In particular, no private TokenSpan persistence is reintroduced.

## Authority and evidence for a decision

These are document/code observations, not new qualification results.

| Owner | Existing contract or observed boundary |
|---|---|
| [Dependencies and views](../specs/language/dependencies-and-views.md#independent-admission-and-representations) | Defines whole sources, source-instance/storage versions, capture extents and backing-only forwarding, but explicitly gates physical source/capture/refinement records, root/loan programs and target layouts separately. |
| [References](../specs/language/references.md) and [lifetimes](../specs/lifetimes.md) | Distinguish descriptor storage, selected referent, forwarded backing, independent owner/guard obligations, normal backing, exact-place GC roots and explicit pin reborrows. Ordinary permitted writes need not end a storage generation. These semantics are preserved, not proposed anew. |
| [Logical dependency records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) and [Dependency clause](../specs/representation/contract-clauses.md#6-dependency) | Already define the 20-byte MethodDef-owned relation. They do not serialize actual source instances or supply runtime root/retain/pin/cleanup carriers. An empty formal/receiver set does not erase independent obligations. |
| [Register Body](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations) and [metadata Body boundary](../specs/il-metadata.md#body-table-and-coded-instruction-boundary) | Already require dynamic target/backing/loan facts across incoming edges, derived from operations rather than per-expression wire IDs. Typed registers/Homes are not lifetime identities. This is a reason to reconstruct provenance, not add a second SSA/instance stream. |
| [Windows x64 stable borrows](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | Assigns a one-pointer crossing for bounded stable rootless inputs, expressly not a stored checked-reference descriptor or general reference-result ABI. A static source table cannot complete that missing target contract. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`SemanticVerifier.Dependencies.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.Dependencies.cs) | In-memory recipe contracts distinguish receiver/formal and result/output/holder targets; direct NativeOutput declarations now normalize nonempty source permissions. These records do not identify actual backing generations or runtime support. Compiler enum numbers differ from the shared wire tags. |
| [`SemanticVerifier.TokenSpans.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.TokenSpans.cs) | `BackingDependencies` uses slot-indexed sets, negative incoming-span roots, staged sets and separate native-pointer origins. Owner transfer remaps slots. This bounded profile is not a general place/value/capability identity model. `CreateBackingDependencies` does not instantiate general NativeOutput dependency sets onto published aggregates. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`ModuleCompiler.TokenSpans.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs) | At the inspected pre-93 snapshot, the structured writer selected private revision 89 and function reconstruction did not persist `DependencyContracts`; compilation-local span persistence explicitly rejected. The current writer/reader select revision 104, as recorded in the [implementation inventory](../implementation/modules.md). That later composition does not select candidate 93 or supply its general declaration collection/runtime anchor contract. |

The genuine missing-contract boundary is therefore **how the unassigned
carrier integration is represented and exchanged**, not whether aliases,
backing-only forwarding or GC pinning are source-language concepts.
Implementing the existing slot analysis more broadly cannot silently choose
a new persistent instance graph or public descriptor ABI.

### Alternatives

* **Selected minimal integration:** shared declaration clauses plus an
  operation-derived ledger; a small explicit private declaration-codec delta.
  No duplication of whole-source authority or serialization of execution IDs.
* **Persistent source/capture/anchor graph:** could publish additional
  producer facts, but needs occurrence ownership, CFG/loop instantiation,
  acquisition/release semantics and another complete rewrite codec. Static
  nodes still cannot establish future roots or pins. It is unnecessary for
  the selected operation-derived model.
* **Universal opaque runtime anchor in every reference/view:** could support
  some stored/erased values, but selects layout, acquisition, copying,
  failure, release and ABI behavior. It risks inventing per-view retention
  or allocation and conflating an explicit resource owner with backing
  support. That is a substantive runtime/target proposal, not a small
  alternative encoding of DependsOn.

No compatibility obligation requires preserving the old slot design. The
minimal choice is selected because existing operations already own the needed
state transitions, not because unpublished structures or numbers are frozen.

## Common compiler carrier

The following are exact **candidate semantic-IR shapes**, not source types,
standard tokens or machine-layout structs. Names are illustrative; preserve
the distinctions and fields. A complete identity includes its using Module,
declaration and substitution context, not just a local integer.

```text
SourceKey      = (declaration, Formal(explicitOrdinal) | Receiver)
DestinationKey = (declaration, destinationKind, destinationOrdinal, phase)
Permission     = BorrowStorage | ForwardBacking | DeclaredUnion
Relation       = (DestinationKey, SourceKey, Permission)
SourceSet      = (DestinationKey,
                  ExplicitEmpty | Nonempty(set<(SourceKey, Permission)>))

StorageKey     = (activation, storageSite, supportingScopeActivation,
                  storageGeneration, selectedProjection)
ValueKey       = (producingOperationOccurrence, executionOccurrence)
CapabilityKey  = (acquisitionValueKey, exactSupportingContract)

Anchor =
    Storage(StorageKey)
  | Backing(CapabilityKey)
  | Owner(CapabilityKey)
  | Lease(CapabilityKey)
  | Guard(CapabilityKey)
  | Root(CapabilityKey, exactInitializedPlace)
  | Pin(CapabilityKey, supportingRoot, exactPlaceOrWholeObject)
  | Publication(CapabilityKey)
  | Code(selectedModule, codeOrigin, retainedDeclaration)
  | Affinity(CapabilityKey)

Obligation     = (Anchor, requiredValidity, governingContract)
Dependencies   = Known(set<Obligation>) | Unavailable(requiredFact)
Origin         = (declarationRelation, stagedSourceValue,
                  BorrowStorage | ForwardBacking)
ValueFacts     = (ValueKey, completeType, selectedReferent?,
                  Dependencies, set<Origin>)
PlaceFacts     = (StorageKey, completeType, access, initializationState,
                  currentValueFacts?)
OwnSource      = Ineligible | Eligible(StorageKey, Dependencies)
                           | Unavailable(requiredFact)
StagedSource   = (SourceKey, stagedValueKey, OwnSource,
                  forwardedDependencies, independentDependencies)
Attachment     = (dependentValueOrHolderOrOperationInstance,
                  declaredExtent, Dependencies, set<Origin>)
```

`requiredValidity` is the exact condition supplied by the governing admitted
contract: storage remains live, an owner/lease/guard remains valid, a root
retains its exact place, a pin keeps its selected backing stable, publication
has occurred, code remains loaded, or use/cleanup obeys the required affinity.
It is not an independently authored predicate program. A capability's
acquisition, validity endpoint and transfer rules must already have a complete
supported contract; the `Guard` or `Pin` tag does not invent one. Unknown
protocols, missing backing and unsupported roots remain `Unavailable`.

These are compiler facts. They do not require allocating an Anchor object at
runtime, changing a view's instance size, retaining an explicit owner,
acquiring a root/pin, or attaching a pointer-to-owner registry to raw addresses.
An operation that actually needs runtime support must use its separately
admitted representation; compile-time dependency bookkeeping cannot replace it.
A live stored value requires matching current ValueFacts; optional absence
there means no complete live value only when initialization state permits it,
not unknown provenance. Partial aggregates keep their independently live
children's facts under the existing structural state and projected StorageKeys.

### Identity and availability

* A StorageKey denotes actual selected storage and its lifetime generation.
  A projection uses the defining place operation's already evaluated receiver
  and selectors; it is not a new source-annotation path language. Equal
  addresses, reused slots or equal types do not equate keys.
* A permitted Copy assignment to a still-live aliasable place changes its
  current value/read state, not automatically its StorageKey. A reference
  to that place can observe the later value. Retirement/reuse creates a new
  generation. Keep value/read-state revisions and output-publication
  correlation epochs **separate** from storage-lifetime generations.
* ValueKey identifies a computed/staged value, not its current variable.
  Descriptor copies preserve the referent and supporting identities.
  Rebinding installs the new value's facts; surviving copies keep the old
  facts. Mutable memory reads use the place's current state, not a frozen
  snapshot of an earlier descriptor field.
* CapabilityKey identifies the actual acquisition. Moving an owner changes
  its responsible residence, not its allocation/guard/root identity.
  References into the old owner's inline bytes do not follow that move.
  Do not generalize slot remapping into “all dependencies follow the owner.”
* Distinct declared source permissions/origins survive even when their actual
  referent and supporting anchors normalize to the same place. Deduplicating
  an identical obligation does not merge source permissions, independent
  owners/guards or dependents' extents.
* `Known({})` requires a supported derivation of no obligations in that
  dimension. It is not the default for a missing array, failed lookup,
  unsupported type, unresolved call or exhausted analysis. Joining an
  unavailable required fact cannot produce Known empty. Raw pointer bits
  may remain inert data under their existing rules, but an absent pointee
  proof never becomes a checked backing/dereference permission.

Keys above describe dynamic identity, not mandatory unbounded counters.
Static analysis uses finite operation/scope identities, symbolic generations
and guarded merges/fixed points. Re-executing a loop site instantiates the
ordinary scope/value transitions; it creates no retained-owner arena.
Resource exhaustion or an unsupported required merge is an explicit refusal,
not convergence to empty or invented lifetime extension.

### Binding a source

Capture each argument/receiver once, at its ordinary source evaluation point.
Use the checked named/positional/witness map to bind SourceKey to that staged
value. An implicit receiver is never explicit formal zero.

By-value staging snapshots the value. Staging a reference snapshots its
descriptor and selected place, **not T's contents**. Resolve carried facts
through that place's current state at the required invocation/publication
point, accounting for checked intervening alias writes and callee effects.
Do not freeze a prior descriptor-field value or silently attach an old
backing set to newly stored contents. If the relevant mutation/effect cannot
be represented, refuse the required provenance-dependent use.

| Actual source | OwnSource and forwarded facts |
|---|---|
| Struct receiver or checked `ref<T>` input | OwnSource is the selected T place, including its inline substorage alternatives, not the descriptor slot. Forwarded facts come from backing already carried through that T value. Copying the original reference still uses the own-storage alternative. |
| By-value view/descriptor-carrying aggregate | No borrow of the caller variable or callee parameter-copy slot. Preserve its carried dependencies, including nested data and shape backing. A checked reference's selected-referent rule above still applies. |
| Class/box handle or receiver | Use its admitted object/backing relationship, not its handle slot. Any required normal strong-backing support or GC support must independently exist. |
| Transferred owner | Preserve its actual acquisition and nested dependencies after transfer. No implicit surviving owner is created; inline-place and allocation dependencies remain distinct. |
| Raw pointer | No checked own-pointee storage is inferred. Preserve supported address origins and backing already established by its producer; missing required facts remain unavailable. |
| Independent scalar/unit | No external dependency on the supplying local merely because it was an argument. |
| Checked init-output explicitly admitted as a source | Bind its actual published inner value only in the already declared outcome/phase. Vacancy is not a source value. |

For each permitted source, BorrowStorage selects eligible own-storage
obligations; ForwardBacking selects existing carried backing; DeclaredUnion
retains both possible alternatives. Independently required obligations always
remain. ForwardBacking is **not set subtraction from a previously combined
set**: an owner/guard/root needed by forwarded backing can also support the
source place and must not disappear because its identity occurs in both.
No relation makes an ineligible or unavailable own-storage source eligible.

The caller retains every declared possible source, not only the provider's
current implementation branch. Source checking establishes that an authored
body obeys those permissions. Imported/foreign body truth remains the
producer/declarer's responsibility; the caller does not inspect that body to
invent a narrower permission.

### Operation transfer rules

No new source operation or opcode is selected here. The existing operation,
its complete type and its published contract determine these ledger effects:

| Operation | Required effect |
|---|---|
| Place/reference formation | Capture the exact selected place and access once. Ordinary stable reference formation retains its place/backing dependencies; it cannot acquire a GC root/pin. Referencing a descriptor field slot and copying its stored descriptor produce different facts. |
| Copy/read, projection, optional wrapping/unwrapping | Carry complete applicable dependencies/origins. Copies create distinct dependent extents, not another owner or acquisition. Presence refines availability, not rooting/stability. |
| Assignment/store | Apply ordinary destination preparation and retirement; attach the source value's dependencies to the actual destination. Preserve outstanding aliases and the holder's conservative capture bound. A Copy-place update does not invalidate its storage merely by being a write. |
| Transfer/partial field move | Transfer the responsible value/capability and its dependencies. Preserve live siblings and references to their actual places; reject moves/replacement that would invalidate a surviving dependent or required cleanup. |
| Call staging/invocation | Keep staged source facts while later arguments execute; recheck actual required source/access validity at invocation. ABI/formal ordering cannot re-evaluate or redirect them. Consume the selected declaration, not name/sample policy. |
| Result/checked-output publication | Instantiate that destination's normalized relationships at its exact publication phase and correlate them with the actual invocation/output value epoch. No publication is inferred from initialized bytes or an old result. |
| NativeOutput | Keep the three prerequisite proposals unchanged: attach dependencies to successful inner T; complement publishes no holder; maybe-live state keeps possible obligations; pre-invoke cancellation releases only prospective output use. Failure after Invoke is indeterminate, not complement/vacancy or proof that supporting obligations ended. No output formal becomes a source. |
| Capture into an existing holder | Attach conservative retained dependencies to its actual referent/object/state, including mutation surviving failure. Ending a visible handle, rebinding a descriptor or reaching a private last use does not prove retained-state release. |
| Join/loop | Union possible obligations and retain any established outcome guards, source permissions and independent holder states. A merge does not acquire ownership or make circular liveness claims true. |
| Retirement | End that dependent's actual extent; discharge only support whose governing contract permits it. Ending one descriptor does not retire its referent or other copies. Explicit owner/guard endpoints and cleanup order remain authoritative. |

Erasure/adapters must preserve the same facts and any actual runtime carrier.
If the destination representation cannot do so, reject the conversion/use;
do not box, allocate, retain an owner or erase the obligations as a fallback.

## Standard exchange: reuse, not another instance table

The sole declaration relationship remains the assigned **20-byte** Contract:

```text
u32 ownerMethodRow, u16 clauseKind=0004, u16 flags=0,
u8 destinationKind, u8 sourceKind, u8 relationship, u8 phase,
u32 destinationOrdinal, u32 sourceOrdinal
```

The owner is a one-based typed MethodDef row with reserved-zero high byte,
not a DefId. Payload domains remain:

* Destination `1=result`, `2=initialization output` under the separate raw
  output amendment, `3=retained formal holder`, `4=retained receiver`.
* Source `1=explicit formal`, `2=receiver`; receiver/result ordinals are zero.
* Relationship `1=borrow storage`, `2=forward backing`, `3=declared union`.
  Broad source recipes normalize to 3, refined backing-only sources to 2;
  preserve any independently restricted borrow-only contract as 1.
* Phase `1=normal publication`, `2=existing-holder capture on applicable
  exits`, `3=deferred publication`, under their existing role/phase contracts.
* An explicit empty source set has source kind/relationship/ordinal all zero.
  It is one row, not absence, and cannot coexist with nonempty rows at that
  destination/phase.

Keep existing owner ranges, uniqueness keys, target applicability, source
eligibility, publication and default-normalization rules. Captures is
retention, not Transfer or an allocation; ForwardsBacking is normalized
source permission, not another root record. Do not invent a phase or combine
distinct unrepresentable relationships to fit this row. An operation/Task
destination lacking a complete assigned projection still rejects.
The selected meanings are `dependencies-and-views/1` and
`dependency-source-schemas/1`, including their source-default/inheritance
rules. Standard Module governing edition 1 and its complete retained
requirements must determine those rules; a consumer's current defaults
cannot replace them. This does not complete an unregistered capability.

Typed Body operations, incoming edges, complete types and declaration
contracts reconstruct actual bindings and the ledger. There is no new
per-expression-ID inventory, BlockEntryState field, Anchor pointer, runtime
generation number or persisted verifier cache. Existing reference/place,
copy/store/take/retirement/call operations keep their assigned shapes.
Root/pin acquisitions cannot be reconstructed from operations that never
represented them; those remain the separate blocker below.

### Allocation and exact-schema check

The checked allocation baseline is
[`trrm/1-draft-layout-queries-1`](../specs/representation/trrm-1.registry.json):
core section kinds through `004B` plus optional `7F00`; assigned definition
kinds `13/14/16/19/1E/21`; TypeSpec forms `01..0F`; qualifier bits
`01=write`, `02=gc`, `04=init` with their node restrictions. The GC qualifier
is already assigned: prose saying all GC type encoding is unassigned must
not be used to allocate a second spelling. A qualifier is still not a
complete runtime representation. The retired pin qualifier remains reserved.

Instruction encoding `01` has fixed tiny/short/long partitions, no prefix or
escape; Body records `01..0B` are assigned. The aggregate-output candidate
uses **Body record `0C`, long primary `DF`, source-use 6 and Feature 8**;
Contract `000C` is independently NativeCallable. Features `1..7` and `13`
are registered, `9..12` have assigned/incomplete registrations. This proposal
allocates **none** of those namespaces or their free neighbors. A new static
table/Feature/root opcode is not required merely to preserve the existing
Dependency meaning. Reserved Transfer and optional/nonsemantic sections
cannot carry missing support.

Global typed rows remain `1..0xFF_FFFF`; Body-local indexes remain zero-based
u16 with all 65,536 values usable. No local index is truncated into a
declaration/source/generation identity. Canonical writers order these
Dependency rows by unsigned
`(destinationKind,destinationOrdinal,phase,sourceKind,sourceOrdinal)` within
the owner's Dependency clauses; ordinary equivalent physical reordering is
not a semantic error. Duplicate occurrence keys are invalid, even if equal.

No standard version, schema selector or feature registration is added.
Exchange still requires exact out-of-band agreement on the complete `(1,0)`
draft and governing chapters, including any provisionally selected output
amendments. This decision does **not** close the remaining general reference,
capture/refinement, capability-registration or execution/target gates.
Declaration inspection and complete executable admission are different claims.

### Source-schema normalization

Use the existing source-schema destination and phase rather than encoding the
attribute spelling. For the currently assigned destinations:

| Effective source relationship | `destinationKind` | `phase` |
|---|---:|---:|
| `DependsOn` ordinary result or checked init-output value | `1` or `2` respectively | `1` |
| `Captures` returned holder/value | `1` | `2` |
| `Captures` checked init-output value | `2` | `2`, correlated with its declared publication outcome |
| `Captures` existing retained formal holder | `3` | `2` |
| `Captures` receiver, including a constructed receiver | `4` | `2` |
| `ForwardsBacking` refinement | Same destination and phase as its base relationship | Same as base |
| Explicit deferred-completion publication | Only its already-admitted destination | `3` |

For each destination/phase, retain one row per permitted source. An explicit
empty relationship uses the existing zero source triple. Broad source
permissions use `relationship=3`; `ForwardsBacking` refines only its selected
sources to `2`, leaving other broad sources at `3`. Preserve `1` only where an
already-admitted contract independently restricts that source to storage
borrowing. If distinct authored relationships cannot be projected without
collapsing required source sets, phases or holder extents into the same row
key, reject that use until its complete representation is selected. An
operation/Task capture with no assigned destination remains unsupported;
phase `3` does not invent one.

## Private exchange: candidate revision 93

All assignments here are **unapproved**. Select exactly
**candidate 92** from
[private NoThrow retirement](no-throw-private-persistence.md), with the
unchanged output-schema delta from candidate 90, then add only this field.
Do not redefine candidate revisions 90, 91 or 92, or reinterpret the
revision-89 base schema.

Immediately after each `ModuleFunction.OutputContracts` collection proposed
there, before its expected-vtable presence Boolean, insert:

```text
i32 generalDependencyRowCount
repeat generalDependencyRowCount:
    u8 destinationKind
    u8 sourceKind
    u8 relationship
    u8 phase
    u32 destinationOrdinal
    u32 sourceOrdinal
```

Integers are little-endian. Each row is exactly **12 bytes** and has the
shared payload meanings above, with ownership implicit in the containing
ModuleFunction. The prefix is exactly four bytes; no optional trailer,
per-row owner, type code, source text or host object ID follows.
Exact revision 93 fixes these normalized relationships to the two rule
versions above; it does not select rules by source spelling or the reader's
defaults. No additional per-row rule-version word is needed.

* Include this collection on **every** declaration/expectation, even without
  a body. Count zero means no effective relationship in this collection;
  it cannot substitute for a required empty-source row or normalized default.
  Reject missing required relations and default/uninitialized in-memory arrays.
* An annotated NativeOutput destination's phase-1 rows remain exclusively in
  its prerequisite `OutputContract.Dependencies`. They must **not** also occur
  here. Reconstruct one common normalized declaration view from the two
  disjoint carriers. This preserves that candidate's sole output authority;
  it is not an alternate output encoding or a change to its collection.
* Receiver/result and explicit-formal ordinals have the shared logical
  namespace. Apply the prerequisite's explicit receiver-position conversion
  for private vtable slots. Never cast compiler enum values to shared tags.
  Types and callable roles come from the complete containing declaration;
  a row cannot manufacture an unsupported checked-reference type or role.
* Validate all shared domains, ordinal bounds, eligible destinations/sources,
  uniqueness and empty/nonempty exclusion. Unknown tags/unsupported required
  phases reject affected use. The raw-output self/inter-output source
  exclusions remain unchanged.
* For n explicit formals and receiver-presence r in {0,1}, a checked loose
  row-count bound is `3 * (2 + 2*n) * max(1,n+r)`, further restricted by
  actual applicable targets/phases. Validate `12*count` against remaining
  bytes before allocating. Existing 256-slot/64-call-argument private support
  limits remain limits, not source/standard capacities.
* Writers emit the canonical ordering above. Readers may accept reordered
  well-formed rows, then normalize; equal duplicates still reject.

At the source snapshot used to form this proposal, revision **93** followed
the inspected private revisions through 89 and the interacting proposals'
90–92 candidates. The current implementation inventory selects revision 104;
93 is therefore a historical candidate assignment, not the current schema or
an availability claim. This proposal does not revise its exact candidate
identity. The number denotes an i32 private header value, not `(major,minor)`,
a Feature, opcode, expression tag or native-profile ID. Existing expression
tags 1–40 and the candidate tag 41 are unchanged; no new operation tag is
allocated.

Under this candidate, all new structured writes and its reader would select
exact **93**, including programs with empty collections. Reject other
revisions rather than guessing/promoting them or retrying an older decoder.
As with 92, no historical reader/converter is proposed. Equal “93” bytes still
require agreement on this complete draft schema. The candidate cannot be
implemented by changing only the writer's final version constant: header
allowlists, nested readers, declaration projection and producer validation
must select the same layout.

Keep `TNPK`/`TNSP`/`TNGP` outer version **2** unchanged. Admit the embedded
structured schema independently, rebuild contract lengths and checksums, and
regenerate images when their semantics change. A valid image/checksum cannot
bypass unsupported required relationships. Development `.trrm` and standard
`.trrm` remain separate selected codecs; neither learns revision 93 from its
filename or from legacy native-output clause `0x12`.

This delta persists **general declarations**, not currently unencoded general
reference types/operations, source-grant prototypes, indexed private bodies
or TokenSpan. Where those cannot be decoded and reconstructed completely,
retain their rejection gates. Do not make a general reference persist by
serializing its transient Dependencies as a substitute for its missing
type/operation/runtime codec.

## Binding, rewriting and consumer obligations

Resolve selected Module/declaration identities first. Keep each expected
declaration's collection separately, including declaration-only imports and
multiple expectations selecting one provider. For the new general collection,
apply the accepted directed source-bound comparison: a provider may exclude
a source or narrow its allowed alternatives; it cannot add a source/capture,
widen backing-only to borrowing, change destination/phase, or discard an
independent required bound. Compare after complete substitution and checked
formal/witness mapping. Retain the caller's original bounds and any stronger
provider fact actually exploited. The prerequisite private **output**
collection retains its separately selected exact-comparison rule unchanged.

All type/definition/provider remapping must visit declaration relationships,
place projections, origin permissions and supporting contract references,
including ones not present in a value's outer type. Rebuild the ledger after
body cloning/reordering/remapping; old occurrence keys are not valid for new
operations. Preserve resource acquisition identity across an actual transfer,
not across unrelated equal-layout values.

For standard re-encoding, transform decoded MethodDef/Contract ownership,
signature/formal contexts, TypeMap/MethodMap/FieldMap and referenced place/call
records. Rebuild affected Home references, record indexes/offsets, CFG/code
offsets and SourceMap slots/roles. For private re-encoding, preserve
source-ordered arguments, symbolic call resolution, structure/pointer mappings,
declaration collections and container extents/checksums. Do not substitute
matching bytes or serialize stale slot-based verifier caches.

Source/public-bound-program producers must establish the actual lifetime,
access, backing and publication facts they emit. Consumers must check exact
schema/framing, counts, owner/range/reference namespaces, domains, required
support and binding compatibility before affected use. Reconstructing the
ledger needed for lowering does not impose semantic re-proof of every
producer body or foreign provider promise. Optional deeper audits are separate.
False producer facts can invalidate execution; an unavailable required
representation, incompatible binding or missing fact instead requires refusal,
never a successful empty ledger.

## Separate runtime/target decision and prerequisites

The ledger's Root/Pin/Backing cases are **requirements to preserve**, not
implementations of support. A later physical proposal is required before a
route needs any currently unassigned runtime carrier. It must select:

1. Normal class/box backing holds and once-only Cleanup retirement; exact-place
   GC locator/root representation and relocation updates; shared support for
   explicit pin reborrows/whole-object conversion; acquisition failure with no
   published descriptor and settlement of partial acquisition.
2. The typed Body operations/records that actually acquire/transfer/end such
   support and compose it with normal/exceptional cleanup. Existing ordinary
   ReferenceFormation, register moves or a Dependency row cannot stand in for
   an unrepresented root/pin action. Rooting is neither guard retention nor
   pinning, and a raw pointer is none of them.
3. Each target's stored/result/capture/erased descriptor and anchor layout,
   internal call ABI, root maps, relocation/barriers and cleanup/helper
   contracts; exact capability registration and transitive prerequisites.
   A native one-pointer call-extent projection does not select these layouts.

Those are genuine independent decisions, deliberately **not** numeric
candidates hidden in this proposal. General GC/normal shared-object execution
remains blocked until they are made. This does not reopen accepted source
reachability, pin, aliasability, Cleanup, affinity or failure semantics.

Implementation prerequisites for the selected slice are: complete normalized
declaration contracts; supported complete types and defining operations for
each admitted value/place; shared traversal/rewriting and initialization/
cleanup analysis; exact codec selection; and complete selected bindings.
Private 93 additionally requires the precise combined-92 schema. NativeOutput
use additionally requires all three output proposals, their selector/
reservation codecs and actual source support. Standard use additionally
requires the existing register/metadata implementation and every retained
capability's complete registration/target closure.

Excluded: new source schemas or recipes, arbitrary field/subregion source
contracts, new output-source projections, ownership acquisition/transfer
annotations, async/task/producer frame or operation carriers, witness
projection codecs, guard/invalidation protocols, concurrency reclamation,
collector selection, public Span/TokenSpan ABI or persistence, and a universal
runtime anchor. Preserve already admitted facts from these features, but
reject a route whose own required representation is missing. They are not
implementer-discretion escape hatches.

## Integration and conformance gates

No implementation support is claimed. The intended implementation owners are
the binder and bound declarations; shared bound traversal/rewriters; mandatory
verification/initialization dataflow; private module/reference/set/graph
projection, binding and restoration; standard Contract/Body readers/writers;
and native lowering. Replace sample/type-specific provenance policy, not
merely its field names. Retained native images and semantic re-lowering must
both consume the same complete declaration obligations.

| Case family | Required evidence/outcome |
|---|---|
| Positive storage identity | An ordinary reference observes permitted later Copy assignments to the same initialized place. Retiring/reusing bytes, a new scope activation or another equal-address object does not revive the old reference. |
| Positive forwarding/nesting | Forward a descriptor through a wrapper/tuple/field and through a reference to that wrapper. Distinguish borrowing the descriptor slot from forwarding its referent; preserve data and independently backed shape metadata, including empty/end views. |
| Copy/transfer/retirement | End one copied view and retain another; transfer an allocation owner and reuse its old slot. Allocation dependencies follow the actual acquisition; loans into old inline storage do not. Guard/owner release and unload remain blocked while required. |
| Negative permission | Returning the original `ref<T>` under backing-only permission still needs its selected T storage. Reject dead parameter-copy storage, missing owner/guard support, manufactured write/NoAlias/exclusive authority and unsupported erasure. |
| Staging and loops | Named argument reordering, receiver rebinding in a later argument, mutation of a referenced descriptor field before Invoke, nested calls, conditional sources and repeated materialization preserve evaluated identities/current place facts and source order. Joins retain all required alternatives; no per-iteration retained arena or empty-on-exhaustion fallback. |
| Capture/failure | Existing receiver/holder captures survive an exceptional exit and disappearance of visible handles. Fresh failed construction cleans only its acquired state; a by-value parameter copy does not become persistent caller storage. |
| NativeOutput cross-feature | Nonempty input provenance reaches successful inner aggregates and surviving copies; complement has no holder. Keep maybe-live support, cancellation-before-Invoke and indeterminate post-Invoke failure distinct. Reject raw-output sources and output-address retention; pointer bits alone establish no backing. |
| Empty versus unavailable | Independent scalar/unit and valid independent backing can have explicitly empty receiver/formal sources. Missing required backing, type/layout support or code origins cannot. Known inert raw pointer transport establishes no checked dereference. |
| Malformed declarations | Reject wrong owner/table, bad formal/receiver ordinal, unknown relationship/phase, duplicate keys, empty/nonempty mixing, omitted required defaults, conflicting output/general collections and unsupported operation destinations. Include declaration-only and uncalled requirements. |
| Malformed private bytes | Truncated/negative/oversized counts, `12*count` overflow/extents, unknown 93 layout, old-header substitution and nested native-container mismatches reject before affected use. Unknown facts never decode as zero rows. |
| Binding/replacement | Independently encoded providers with different names/row order preserve source sets after mapping. General narrowing may satisfy broad expectations; adding sources, changing phases or dropping required validity fails. Retain each original expectation and the output candidate's distinct exact comparison. |
| Round trip and rewrite | Reorder types, methods, fields, Homes/maps/records and provider graphs; clone bodies and exercise diamond imports. Canonical re-encoding preserves obligations and source data with rebuilt references/offsets/checksums, not byte equality or cached host identities. |
| Capacity boundaries | Exercise typed-row maximum/reserved high bytes, Body index 0 and `0xFFFF`, zero/maximum inventories and private slot/count bounds. Local numeric equality never aliases independent namespaces or generations. |
| Root/pin/target gate | A valid GC TypeSpec qualifier, a rooted wrapper around a released native resource, or recognizable static clauses cannot bypass missing root/pin/anchor support. Unsupported stored/result references reject even when bounded input borrows work. |
| Runtime qualification, when prerequisites exist | Separate source binding, emitted-image checks and supervised JIT/AOT execution. Test unrelated synthetic APIs/classes, failed root/pin acquisition, exact-place versus sibling access, pin-copy retirement, last backing dependency and retained precompiled providers. Static ledger/codec tests prove none of these runtime observations. |

On eventual adoption, qualify the physical-carrier deferrals in
`dependencies-and-views.md` and `logical-records.md` for this bounded
operation-derived integration, preserving their independent runtime/target
gates. Clarify shared clause/Body reconstruction without changing their
payloads, source semantics or registry allocations. Record actual private
revision/support only after implementation. The normative owners, registry,
existing proposals and implementation inventories are **unchanged here**.

There is no remaining choice inside the proposed ledger or revision-93 delta.
The explicit remaining decisions are the runtime/target support above and
any independently required currently unencoded feature. Completing this
slice is not acceptance of general reference/GC support or of the active
compiler-adoption milestone.
