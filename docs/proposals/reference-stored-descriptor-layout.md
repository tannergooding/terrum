# Stored ordinary checked-reference descriptors

**Status: unapproved; provisional implementation direction only under the
user's authorized missing-contract workflow.** This document selects a
candidate physical contract, not normative adoption, implementation support
or a stable ABI. The governing authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Recommendation and scope

Select an **eight-byte, eight-aligned address descriptor** for ordinary
`ref<T>` and `write ref<T>` in an explicitly selected **Windows x64,
externally bounded stable-backing** extension of the rootless profile.
Select one integer argument carrier and **RAX** for ordinary direct-call
results of those complete descriptor types. Optional descriptors use zero
for absence without another word.

The value remains a **checked, non-owning descriptor**, not `ptr<T>` or
`gc ref<T>`. Equal pointer-sized bits grant no implicit conversion,
marshalling, ownership, rooting, pinning or lifetime extension.

“Externally bounded” means the actual storage and every required supporting
owner, backing, guard, publication and code/load obligation already cover the
descriptor's full dependent extent without a runtime hold owned by each
descriptor residence. It does **not** mean foreign, stack-only, call-only,
independent backing or an empty dependency set. References can be stored,
copied, returned and captured when those extents are established. No hidden
retain, root, pin, allocation or pointer-to-owner lookup repairs a missing
obligation.

This is the smallest physical decision for that route. It deliberately does
not assign normal shared-class/box backing holds, GC locators, pin acquisition,
erased values, suspension, or a universally replaceable Terrum machine ABI.
The [remaining gates](#remaining-decisions-and-support-gates) identify the
smallest further decisions for affected routes.

### Relationship to the root/pin candidate

The [root/pin runtime proposal](reference-root-pin-runtime.md#value-layouts-and-identity)
already recommends **16/8** stable `ref<T>` values, uniformly
`(address, supportCell)`, under its candidate TargetProfile `(2,1)`.
That is unapproved but explicit provisional direction, not evidence that the
accepted rootless profile assigns stored references.

| Selected physical agreement | Ordinary stable `ref<T>` |
|---|---|
| Current accepted Windows x64 profile `(1,1)` | Stored layout and general reference-result ABI unassigned |
| Rootless extension recommended here | 8/8, one address; no descriptor-owned runtime hold |
| Separate RootPin candidate `(2,1)` | 16/8, address plus support; its acquisition, Copy, retirement and call-hold protocol remain unchanged |

This proposal intentionally fills only the first profile's unassigned
nonforeign case. It does **not** supersede the RootPin proposal's uniform
layout, shrink its zero-support values, change its helper ABI or allow
profile-dependent fields to bind by equal source spelling. One selected
profile gives one representation to a complete type throughout signatures,
fields and substitutions; representation never varies by value origin within
that profile. A pinned-origin ordinary reference cannot be truncated into
this address-only value. No implicit cross-profile adapter is selected.

## Contract owners and source evidence

The [specification index](../specs/README.md) leads to these owners. The
intended amendments below remain entirely in this proposal.

| Owner | Preserved contract; intended integration |
|---|---|
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [complete types](../specs/language/types-and-normalization.md) | Preserve `Type`, `ReferenceType`, `OptionalType` and ordinary `ParameterType`; change no production. Outer access, exact inner type, optional nesting and substitutions remain significant. Grammar admission does not itself assign storage. |
| [References](../specs/language/references.md) | Preserve checked descriptor identity, explicit formation/indirection, parameter-local `mut`, initialization capabilities and access attenuation. Add a link to the admitted stored/value-call profile, not a pointer alias or a new passing mode. |
| [Dependencies and views](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md) | Preserve actual source instances/generations, whole-source bounds, captures, backing-only refinements and independent endpoints. No new lifetime annotation or source-path vocabulary is needed. |
| [Data layout and ABI](../specs/data-layout-and-abi.md) | Preserve null-as-absence, the exclusion of ordinary checked references from Blittable, and the separation of stored layout, call mapping and runtime support. Link the new bounded target case without promising a universal ABI. |
| [Windows x64 profile §1](../specs/targets/windows-x64.md#1-scope-and-identity), [data layout §1](../specs/targets/windows-x64-data-layout.md#1-scalar-storage-and-validity) | Amend the stored-reference exclusion only for the explicit nonforeign extension above; add the descriptor table below. Preserve exclusions for every other unassigned representation. |
| [Windows layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Admit these descriptors in nonforeign fields/aggregate layout without granting Blittable or raw Union eligibility. Reuse placement, packing, actual-alignment, padding and retained-query rules. |
| [Windows call interface §§2–5](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults) | Add the bounded ordinary direct-value mapping below. Preserve §5's call-extent projection and its explicit disclaimer: that existing projection alone defines neither stored layout nor reference results. |
| [TypeSpecRef and TypeSpec](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [register Body](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement) | Reuse complete types and typed place/Copy/store/call operations. Add no address blob, reference passing enum, runtime-instance table or trusted verifier cache. |
| [Versioning](../specs/representation/format-versioning.md), [requirements](../specs/representation/requirements-and-admission.md), [binding](../specs/representation/binding-and-replacement.md) | Preserve exact draft agreement, complete retained support and directed expected/provider checks. Completing a target layout neither registers missing capabilities nor upgrades old inputs. |

The genuine undecided contract is physical: neither an assigned reference
type nor the one-pointer call-extent projection answers what occupies a
reference field or what returns a complete reference value.

Current code and active test sources confirm a narrower implementation,
rather than approving either candidate:

* [`Binder.BindType`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)
  has pointer/function-pointer and bounded nominal/span handling, but no
  general ordinary-reference type constructor. Parameter binding recognizes
  prototype `implicit ref<T>` and historical `init ref<T>` by flags on the
  underlying T.
  [`BoundVariable`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  carries `IsOut`, `IsInit` and `IsImplicitRef`; those are not a complete
  stored `ref<T>` identity.
* [`NativeEmitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
  projects `IsOut`/`IsImplicitRef` inputs as addresses and classifies supported
  scalars/structs/TokenSpan. Reusing an address register is not implementation
  of descriptor-valued fields, substitution, copies or results.
* [`ImplicitReferenceParameterTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ImplicitReferenceParameterTests.cs)
  exercises bounded direct loans, materialization, argument order and stack
  positions, including explicit persistence refusals.
  [`PublicInitReferenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/PublicInitReferenceTests.cs)
  exercises the legacy output syntax/publication path, not storage of
  canonical complete reference types.
* [`TrRmTypeSpecTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs)
  and its [tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmTypeSpecTableReaderTests.cs)
  structurally recognize reference/optional/init-reference TypeSpecs and
  equivalent inline forms. The test
  `ValidatesInlineAndIndexedFamiliesWithoutInventingExecutableSupport`
  states the relevant boundary; recognition supplies no layout or execution.
* Existing dependency/copy-origin facts in
  [`SemanticVerifier.TokenSpans.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.TokenSpans.cs)
  do not supply the missing complete reference type or physical carrier.
  [RootPin provider/helper probes](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeRootPinLoadedTypesTests.cs)
  are a separate route, not evidence for ordinary source-reference storage.

The [implementation summary](../implementation.md) and
[view/owner inventory](../implementation/view-and-owner-profiles.md) retain
the general-reference, provenance, persistence and lowering gaps. The
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
has no Compile exclusions; individual native/platform gates still apply.
These are source observations, not newly executed test results.

## Alternatives and selection

| Candidate | Assessment |
|---|---|
| **One address with checked external obligations — selected for the rootless extension** | 8/8 storage, direct scalar-sized transport, no new acquisition/release protocol. Sufficient when established supporting lifetimes cover every dependent extent. It cannot carry an otherwise unavailable runtime hold through opaque storage. |
| Uniform `(address, supportCell)` descriptor/anchor redesign | 16/8 storage and indirect argument/hidden-result transport. Can carry runtime support, but requires exact support identity, hold acquisition/sharing/release, failure, tracing and call handoff. RootPin already owns one concrete version for its separate profile; normal shared-backing support is still a separate decision there. Reserving an unexplained second word would not complete that contract. |
| One pointer to an opaque descriptor/anchor object | Keeps an eight-byte surface but needs descriptor-object storage, lifetime, copying, failure and ABI rules, commonly another allocation/indirection. It is not the selected address descriptor or a free way to hide missing support. |
| Pointer bits plus a global owner registry or automatically retained owner | Not selected. An address does not identify a live generation or independent guard/owner; such a registry/retain changes the runtime and endpoint contract. |

The larger design is material for support-bearing references, not necessary
for externally bounded ones. The choice is based on those obligations and
costs, not an obligation to preserve unpublished layouts. A future unified
layout would be an explicit amendment of both profiles, not an optimizer
choosing eight or sixteen bytes per instance.

## Exact stored representation

The following are accepted-source type forms with **proposed** physical
interpretations, not currently executable examples. All sizes and alignments
are bytes.

| Complete form/context | Candidate representation |
|---|---|
| `ref<T>`, `write ref<T>` | Size 8, natural alignment 8; one little-endian `u64 address` at offset 0; no padding |
| `mut ref<T>`, `mut write ref<T>` declarations/parameters | The same value layout; `mut` changes only that binding's rebinding permission |
| `ref<write U>`, `write ref<write U>` where inner qualification is legal | The same layout, retaining distinct inner and outer authority |
| `ref<T>?`, `write ref<T>?`, equivalent `option<...>` | Size/alignment 8/8; zero is absent, a valid nonzero descriptor is present |
| `ref<ref<U>>`, `write ref<ref<U>>`, `ref<write ref<U>>` | Outer value 8/8 pointing to an independently live inner descriptor slot; that slot also uses its own 8/8 layout, not an inline concatenation or flattened reference |
| `ref<T?>`, `ref<T?>?` | Address of the actual optional T slot, optionally absent at the outer layer. T's optional storage must independently be admitted |
| `T = ref<U>` or `write ref<U>` in a closed generic substitution | T is the complete eight-byte descriptor value, not U or a special reference-parameter mode |

A valid present value identifies one exact, live, completely initialized,
addressable T place with stable backing for the entire required extent.
T has positive admitted storage extent and the actual address satisfies T's
complete object-alignment requirement, including explicit alignment.
Nonzero bits alone establish none of this. The descriptor's alignment is
eight; its referent need not have size or alignment eight. Extent/address
calculations are checked in the target `nuint` domain.

There are no stored type, extent, access, generation, owner, root, pin or
provenance bits. Those are checked semantic obligations, reconstructed from
typed operations and published contracts, not recovered by examining an
address. No arbitrary integer/pointer bitcast creates a checked reference.
This assigns no new address arithmetic, object-representation API, emitted
constant codec, sharing capability or atomicity guarantee.

`ref<void>` still has no borrowable payload place. Zero-extent nominal
referents remain unsupported here rather than being assigned a neighboring
byte or descriptor slot as an anchor. Merely knowing the outer descriptor's
width does not make an otherwise unsupported T place accessible.

### Optionality, nesting and fields

Optional construction follows existing normalization: `option<option<ref<T>>>`
has one presence layer and the same eight-byte layout; source `T??` remains
invalid. Zero initializes an **optional absence** only under an actual typed
initialization, not a nonoptional reference or a vacant declaration.
Unwrapping requires presence and preserves all present-value obligations.
Absence has no referent payload, but does not erase a holder's independent
conservative capture bound or obligations of surviving old copies.

An outer nonoptional `ref<ref<U>?>` can designate an initialized absent
inner slot. Outer presence never proves inner presence. Referencing that slot
depends on its containing storage; copying its present descriptor depends on
the selected U backing instead. A later permitted replacement of the slot
is observed through the outer reference, not retroactively by earlier copies.
Writable outer access permits eligible slot replacement; inner `write`
permits eligible U writes. Equal layouts do not create covariance or rewrite
stored qualifications.

Use the existing target placement rules for these fields in **nonforeign**
aggregates. Sequential and Transparent need not be Blittable:

| Nonforeign field sequence/layout | Offsets | Size/alignment |
|---|---|---|
| Sequential `uint8`, `ref<int32>`, `write ref<int32>` with default packing | 0, 8, 16 | 24/8 |
| Transparent with exactly one `ref<int32>` field | 0 | 8/8, reference carrier classification |
| Sequential `uint8`, `ref<int32>` with Packing 1 | 0, 1 | 9/1 |

These are layout descriptions, not new declarations or a public Auto-layout
promise. Packed descriptor-field reads/copies use actual alignment and exact
extent under the existing safe-access rules. An underaligned descriptor slot
cannot itself be borrowed as an aligned `ref<ref<T>>`; copying its stored
reference does not misalign that reference's separate T referent.
Auto field placement remains private; retained layout observations remain
fixed or coupled under their existing owner.

No reference-containing aggregate becomes Blittable, even with an explicit
marker, a Transparent layout, optional absence or an eight-byte total size.
Raw Union requires Blittable and therefore cannot overlap these fields.
No whole-aggregate atomicity follows from size. Admitted repeated element
storage uses eight-byte descriptor stride with checked extent arithmetic;
this selects no new array, tuple, view or erased-container representation.

## Copy, transfer, storage and lifetime

For the aliasable ordinary descriptors admitted here, select **Copy of the
descriptor**, independently of whether T is Copy. A copy duplicates the
dependent extent and preserves the exact selected place/generation, authority,
source permissions and every backing obligation. It does not read/copy T,
duplicate a guard/lease/cleanup owner, or acquire a runtime hold.
An independent governing protocol can still prohibit a particular duplicate
or overlapping use; copying address bits cannot satisfy that protocol.

An admitted nonoptional descriptor can satisfy the default non-unique Copy
type-formal domain even when its referent is unique. An optional actual
requires the formal's nullable domain; `mut` is never a type argument.
Generic substitution preserves the constructor and every qualification.
Closed layout/lowering checks still establish eligible backing and referent
storage rather than assuming them from generic syntax.

Moving a descriptor transfers its value/dependencies under ordinary
availability rules, not ownership of T. Retiring it ends that dependent
extent without destroying T. Retiring one copy does not end another.
A containing aggregate has its own Copy/unique/cleanup contract; an ordinary
reference field neither makes an owner Copy nor adds referent Cleanup.

Construction, store, field extraction, whole-value copy, rebind, join and
call return propagate these facts into the actual destination. Retain the
[operation-derived ledger](reference-provenance-carriers.md), not a runtime
instance graph. An allowed Copy assignment to a still-live T changes its
contents, not automatically its storage generation. Retirement/reuse does
change that generation; address reuse never revives old references.

A returned/stored descriptor need not keep its original descriptor variable
alive. A reference **to** that variable or field slot does. Moving an owner
may preserve its allocation under an existing transfer contract, but references
into the owner's old inline storage do not follow the move. No use can extend
an explicit owner, guard or load endpoint.

The accepted normal-class/box subplace rule can require backing support after
the source handle local ends. An address-only representation does not implement
that support. Such an escape is gated on an admitted support-bearing profile,
not silently rejected as a language rule or repaired with a hidden owner.
GC-rooted and pin-derived forms retain the separate RootPin gate above.

### Initialization is not descriptor Copy

`ref<init T>` and `write ref<init T>` remain fixed-destination, non-Copy
initialization capabilities with their existing scoped reborrow, settlement
and one-shot publication rules. This proposal assigns **no stored
initialization-capability layout** and no permission to retain an uncompleted
capability in a field, result, capture or opaque generic container.
Taking an ordinary nested reference cannot launder it into Copy authority.
The optional-layout rule above does not assign storage or Copy to
`option<ref<init T>>` or to a container carrying uncompleted init authority.

`ref<init ref<U>>` initializes an eight-byte **ordinary descriptor slot**;
it does not initialize U. The same applies to a writable or optional inner
descriptor. For the nonforeign direct-call agreement here, explicitly extend
the initialization-destination payload admission to these complete descriptor
types, using the existing one-address projection of the vacant slot and
unchanged state/publication rules. This does not extend the foreign §5
payload set or assign a stored representation to the init capability.
Direct generic initialization helpers with generic T keep their existing
capability contract; complete type/formation/output codecs remain separately
required, including the [checked-output candidate's](function-pointer-checked-output-parameters.md#initialization-capability-formation)
unapproved formation amendment where applicable.

Only complete initialization consumes the `init` capability and attenuates
it to the ordinary reference form, preserving independently present outer
and inner write authority. That ordinary value can then use this profile if
its actual T place/backing is eligible. An `init ref<T>` **local declaration**
instead makes the descriptor slot vacant; it is not a live `ref<init T>`.

## Ordinary direct-value call agreement

This candidate explicitly adopts the Windows positional mapping for these
nonforeign values in **one validated runtime instance and jointly
lowered/regenerated code closure**. Separately supplied semantic Modules can
participate after complete binding and compatible closed specialization.
The actual failure/helper/thread-entry agreement must already be established.
Coarse Windows target requirement 1, same process, or equal source types is
not that agreement.

| Logical input/result | Physical candidate |
|---|---|
| Ordinary `ref<T>` / `write ref<T>` input, including a generic T substituted with one | One eight-byte integer carrier containing the descriptor address value, not the address of the caller's descriptor slot |
| Optional ordinary descriptor input | The same carrier; zero is absence |
| Ordinary or optional descriptor result | All eight descriptor bytes in RAX; no hidden result buffer for this result |
| Checked initialization formal whose inner value is an admitted ordinary/optional descriptor | One address of the exact vacant eight-byte slot; fixed non-Copy reservation and declared publication, not descriptor Copy or a stored init-capability ABI |
| Admitted nonforeign aggregate containing descriptors | Existing aggregate classification: size 1/2/4/8 integer carrier; other nonzero sizes indirect input and hidden result |
| Eligible nonforeign Transparent descriptor wrapper | Inherits the descriptor's integer argument/RAX result classification, without foreign admission |

A two-reference ordinary record is consequently a 16-byte indirect value,
not two register arguments. Its incoming independent by-value copy has at
least 16-byte ABI alignment; hidden result storage has the actual aggregate
object alignment. Neither buffer is a source loan or permission to reference
a destroyed callee parameter copy.

The physical sequence is the hidden result pointer when required, an already
admitted explicit instance receiver when present, then payload-bearing
formals in declaration order. Unit positions remain in the logical signature
but consume no physical position. Positions 0–3 use RCX/RDX/R8/R9 for integer
carriers or the corresponding XMM register for floating carriers; they share
one ordinal. Position 4 starts at `[entryRSP + 40]`, then eight bytes per
position. Retain the 32-byte home area, stack alignment, preservation,
supporting-storage and unwind rules of the selected Windows agreement.
No new receiver, generic dictionary, provenance, runtime-context or exception
argument is inferred. Shared-generic/witness execution needing an unassigned
context remains gated.

Binding the callee parameter copies the descriptor value. Parameter-local
`mut` can rebind that copy without changing the caller's descriptor; an
explicit `write ref<ref<U>>` instead names a caller slot under its own
authority. `implicit ref<T>` remains source-call adaptation, not a different
machine mode or implicit dereference under substitution.

### Staging, publication and failure

Preserve Capture/Stage/Check/Prepare/Invoke/Publish. Evaluate descriptor,
receiver and arguments once in source order; formal/register ordering does
not reevaluate them. Later argument rebinding cannot redirect an earlier
captured reference. Permitted writes through another alias can change T
before invocation; the descriptor is not a snapshot of T.

Instantiate normal result/output relationships from the selected declaration
and staged sources at their actual publication phase. Keep whole-source
DependsOn/Captures bounds even when an exact
[field copy-origin summary](reference-field-value-provenance.md) supplies
additional precision. Missing required provenance is unavailable, not an
empty set or permission to inspect a provider's private body.

RAX/buffer bytes alone publish nothing. Complete result checks and required
cleanup precede normal publication; failure publishes no normal reference
result. Existing-holder captures or writes surviving failure retain their
actual obligations, not rollback. Pre-invocation failure ends/settles staged
values under their real supporting scopes. Private partial writes to an init
destination cannot create a live descriptor or turn failure into optional
absence. No normally required result is fabricated from zero.

Address-descriptor Copy itself needs no allocation/helper and introduces no
new acquisition-failure channel. Formation/checks and surrounding operations
retain their actual failures. A later support-bearing route must use its
own acquisition/no-publication and settlement protocol.

### Boundaries not widened

* The existing [§5 call-extent projection](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization)
  still passes one stable T address with support retained by the caller.
  Coincident address bits do not change its bounded retention/result contract
  into this complete descriptor-value crossing. Its fixed-rootless-T admission
  does not start accepting descriptor-containing T.
* Foreign imports/exports, NativeCallable and foreign function pointers gain
  no checked-reference result, stored descriptor or descriptor-containing
  record ABI. References remain non-Blittable. Explicit unsafe raw-pointer
  projection preserves backing/access obligations and acquires no support.
* The [first Terrum function-pointer ABI](terrum-function-pointer-call-abi.md#exact-entry-and-signature-eligibility)
  excludes checked references and non-Blittable descriptor-containing values.
  This proposal does not widen it because a carrier happens to fit RAX.
  The separate [checked-output candidate](function-pointer-checked-output-parameters.md)
  owns its bounded initialization-destination addition; no general reference
  input/result or reference payload is admitted there by this proposal.
* Independently replaceable **precompiled** providers, different runtime
  instances and cross-profile calls need a complete published ABI/assumption
  agreement or deliberate adapter. This first direct-call agreement selects
  neither. Re-lowering a compatible semantic closure is a different route.

## Metadata, schema and binding consequences

### Reuse existing standard forms; allocate no IDs

This physical decision needs **no new TypeSpec form, qualifier, Param mode,
Contract clause, section, definition/token kind, opcode or Body record**:

* Ordinary references use built-in family `8B`, canonical inline form `01`
  over an eligible atomic T, or indexed form `07` over a complete TypeSpecRef.
  Eligible F3 references denote the same complete type. `write=01` applies
  at its actual node; `gc=02` remains the distinct rooted form, not a way to
  select this address descriptor.
* Optional form `06`, constructed form `05`, formal form `04` and nested
  complete TypeSpecRefs already retain the required types. `mut` is not a
  type qualifier. Initialization form `0E` remains distinct; do not substitute
  `07` plus a passing flag or remove nested qualifications.
* MethodSig/Param/FieldDef and Body type maps retain complete descriptor
  identities; ordinary parameters use mode `00`. A reference result is a
  reference TypeSpec, not T plus a result-mode flag.
* Existing `67`/`DD` reference formation, typed loads/stores/takes/retirement,
  field paths, calls and returns carry the semantic operations. Register
  aliasing/movement alone is not a source Copy. Apply the operation's real
  Copy/transfer/borrow use and preserve the ledger through shared traversal
  and rewriting.
* Shared Dependency clauses carry whole-source relationships. Use existing
  supported output/predicate and copy-origin carriers only when actually
  required and admitted; their proposals remain their own owners. No address,
  runtime-generation number or serialized verifier cache replaces them.

Layout/FieldDef records already describe non-Blittable Sequential/Transparent
fields; target derivation supplies extent and offsets rather than storing a
second computed layout blob. Assigned `DE` SizeOf/AlignOf/OffsetOf queries use
their direct TypeMap/FieldMap operands; no generic `LayoutQuery` Body record
is assigned. Retained observations use the existing `TargetLayoutDependency`
and ordered `TargetLayoutDependencyField` records under their selected
`TargetProfile`, only where an admitted query/operation requires them. This
recommendation proposes an exact draft extension of profile `(1,1)`, **not** a
new numeric profile or an assertion that today's `(1,1)` already includes it.
Independent execution agreement still selects the additional direct-call
contract.

No new Feature ID is selected merely to complete this target case.
`RootlessNativeValues` does not become a general non-Blittable reference
capability; Feature 13 applies to its existing retained query scope, and
RootPin's candidate Feature 14 is not a prerequisite for this rootless route.
Existing general dependency/reference capability registrations and complete
Body/runtime support remain before-use gates. Recognized TypeSpecs or empty
Feature ranges cannot stand in for them. This is not a declaration of full
Standard TRRM reference execution support.

The standard format pair stays `(1,0)`. Exchange still requires exact
out-of-band agreement on the entire selected draft and governing chapters,
including this target extension when used. Equal version/profile bytes do
not authorize a decoder or native ABI guess. No released-schema assignment,
compatibility reader or old-image migration is proposed.

Private semantic persistence is a separate unresolved type/operation codec,
not a standard-layout question. Its current underlying-T/mode representation
cannot be relabeled as lossless complete-reference exchange. The smallest
follow-up is the **shared private recursive complete-reference type encoding**
already identified by the
[checked-output proposal](function-pointer-checked-output-parameters.md#private-persistence-owner-and-explicit-next-decision):
ordinary versus initialization reference, optional nesting and per-node
authority, consistently used by fields, signatures, bodies and bodyless
expectations under one exact composed private schema. This proposal assigns
no private revision, escape or expression tag. A path needing that codec
stays rejected until its decision and implementation exist.

### Providers, replacement and remapping

Retain every original expected declaration, selected provider/module identity,
complete substitution, exact target/layout agreement, result/capture phase,
source permission and independent backing/code obligation. Mutable stored
types remain invariant; an allowed readonly value projection is not permission
to rewrite a field or nested inner type.

A compatible provider may narrow a whole-source permission under the existing
directed relation, but cannot add a source/capture, turn backing-only into
borrowing, alter publication or weaken a relied-on validity/cleanup bound.
Keep an extra provider fact when a consumer exploits it; exact copy-origin
facts do not subtract broad lifetime bounds. Consumers check safe decoding,
support and complete binding, not semantically re-prove provider bodies.
Truthful published facts remain a producer obligation.

Field references resolve by actual declaration/owner/substitution, not name,
offset or equal address bits. Reordering/rebinding visits nested TypeSpecRefs,
generic formals, FieldDef/Import/FieldPath identities, method signatures,
dependency source/formal maps, retained summaries, Homes, Body maps and
call/store operands. Rebuild derived layouts, Body indexes/record/code
offsets and affected source-data references; reconstruct local ledger keys.
Do not byte-substitute addresses or preserve stale verifier instances.

Retained size/alignment/offset observations constrain replacement even for a
non-Blittable holder. A physical contract change requires regenerating every
dependent caller/provider in the joint closure or declining reuse. Neither
an unchanged private container/checksum nor a semantic type-name match permits
linking eight-byte rootless and sixteen-byte RootPin descriptors.

## Conformance and implementation gates

These are required future cases, **not passing tests or approval**. Use
unrelated synthetic declarations through public compiler APIs and independent
provider/consumer encodings. Each implemented slice must identify its final
inputs, selected profile and actual evidence level.

| Family | Required positive, negative and boundary outcomes |
|---|---|
| Complete storage | Observe 8/8 for readonly/writable and optional descriptors, eligible inner qualifications and closed generic substitutions. Non-Copy T can be borrowed without copying/owning T. Reject unsupported referent storage rather than treating it as raw bytes. |
| Optional/nested values | Distinguish absent outer reference, present reference to an absent optional slot and nested descriptor slots. Test normalized repeated `option`, explicit `T??` rejection and preserved inner/outer write authority. Present null and nonoptional zero are not valid values. |
| Fields/layout | Check the 24/8, 8/8 and packed 9/1 examples, nested holders and admitted element stride. Packed field Copy works without an overread; borrowing an underaligned descriptor slot fails. Reject Blittable/Union admission transitively. |
| Copy/rebind/transfer | Copy a reference and its holder, rebind one copy/field, and verify old copies still select old backing. A reference to the field slot sees its current descriptor. Self-references still select original storage, not a copied/moved holder's corresponding field. Ending one copy does not end another or destroy T; owner moves do not relocate old inline loans. |
| Direct ABI | Exercise descriptor inputs/results, optional zero, all four register positions, fifth/later stack arguments, mixed floating positions, named argument order, nested calls and generic identity with T=`ref<U>`. Test one- and two-descriptor holders, hidden result ordering/alignment and no reference to temporary ABI storage. |
| Authority/initialization | Exercise descriptor-slot vacancy separately from a live reference to T. Initialize an inner reference through `ref<init ref<U>>`; publish only on the declared outcome. Reject Copy/storage/escape of uncompleted init authority, reads of vacant T and writable upgrades. Completion consumes init once and retains independent qualifiers. |
| Sources/provenance | Return/capture a supplied reference under its permitted whole sources; preserve field-origin precision without narrowing those bounds. Reject a return into destroyed callee parameter-copy storage, or returning the original ref under a backing-only permission that excludes its selected place. |
| Lifetime/failure | Retain all possible sources at joins/loops and across later-argument alias writes. Reject owner/guard release, provider unload and storage-generation reuse while required. Test failed staging, failed construction/replacement, postcondition failure and existing-holder capture surviving an exception without rollback or fabricated optional absence. |
| Separate compilation | Strip provider bodies from expectations; preserve complete reference/optional/nested types and compatible source narrowing through semantic binding/re-lowering. Reject lost bounds, wrong phase, swapped field identities and exploited summary loss. Private transport remains rejected until its exact codec is selected. |
| Metadata/rewrites | Independently encode valid inline/indexed/F3 equivalents and malformed inner references, qualifier positions, ordinary/initialization form confusion and illegal modes. Reorder types, fields, methods, maps and providers; rebuild all references/source data, not cached host identities. |
| Capacity/zero extent | Exercise checked layout/address arithmetic, nested types, global row/high-byte limits and Body-local indexes 0/FFFF without namespace conflation. Refuse zero-extent nominal formation pending its anchor contract; never make void addressable or use zero as a live-reference sentinel. |
| Boundary exclusions | Reject stored-reference foreign/fnptr ABI admission, independent precompiled crossings lacking agreement, implicit pins/roots/retains and eight-/sixteen-byte profile mixing. Existing valid call-extent projections remain bounded and unchanged. |
| Runtime evidence | After implementation, distinguish source binding, mandatory verification, exact-schema round trip, image emission and supervised JIT/AOT execution. Include dangling/stale-generation negatives and actual argument/result observations; emission alone is not runtime qualification. |

Implementation remains required across complete bound type/value/place
representation, generic substitution, shared traversal/rewriting, provenance
and initialization verification, field/holder layout, ordinary value calls,
publication/cleanup and native lowering. Supported persistence additionally
needs complete codecs, source-free binding/replacement and remapping.
The existing bounded parameter tests and raw pointer fields do not close
these gates.

## Remaining decisions and support gates

There is no remaining representation or direct-value carrier choice inside
the selected externally bounded rootless subset. Formal approval and all
implementation/qualification remain outstanding.

| Route not completed here | Smallest further decision or existing dependency |
|---|---|
| Private exchange of these complete types | Select the one recursive private reference/optional type codec and exact composed schema described above; do not invent per-call mode fallbacks. |
| Reference to a normal class/box subplace whose backing must survive the last source handle | Select the normal shared-backing support-hold carrier and its Copy/last-dependent release/call handoff. RootPin's two-word envelope does not itself assign that support kind; this address-only subset cannot carry it through opaque storage. |
| Pin-derived ordinary references or stored rooted locators | Use the separate RootPin profile and its complete runtime/schema support unchanged. Replacing it with this layout would require an explicit amendment, not an implicit dependency on GC for rootless references. |
| Zero-extent nominal referents | Select a non-null address/anchor and exact-place identity rule without fabricating a payload byte. Void remains nonaddressable under the accepted contract. |
| General Standard dependency admission, fnptr reference calls, erasure or independently replaceable native providers | Complete their own registration/contract/ABI owners before affected use. No new allocation or policy for those broader surfaces is hidden in this layout choice. |

This proposal supplements [reference provenance carriers](reference-provenance-carriers.md)
and [field-projected provenance](reference-field-value-provenance.md), and
preserves [RootPin](reference-root-pin-runtime.md) and the
[Terrum function-pointer ABI](terrum-function-pointer-call-abi.md) as distinct
unapproved owners. It changes no normative specification, runtime protocol or
implementation inventory.
