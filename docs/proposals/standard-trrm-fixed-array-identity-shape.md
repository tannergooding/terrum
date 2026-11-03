# Standard TRRM fixed-array identity and shape

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This recommendation completes the separate
[storage-address prerequisite](standard-native-storage-address-formation.md#array-representation-boundary),
not normative adoption, implemented support or execution evidence.
[Proposal index](README.md).

The structural array candidate below is **A1** and is superseded for
integrated nominal-array use by the unapproved
[rootless Copy type-evidence composition](standard-trrm-rootless-copy-type-evidence.md),
which selects exact schema **A2**. A2 retains this document's TypeSpec,
FixedArrayLayout, PlaceSelection and Feature 20 allocations, and adds
positive nominal Copy evidence. Do not select or decode A1 as an alternate
route for arrays whose complete element graph reaches a nominal struct.

## Decision and boundary

Recommend a **structural fixed-inline-array semantic family**, with exact
element type E and unsigned 64-bit count N; one explicit, Module-owned
target-layout row; and an intrinsic fixed-array alternative in the existing
PlaceSelection record. Select these together as **A** in one new **F+S+A**
schema below. Leave the existing F+S identity, EC/StorageAddress record,
CallSite and raw-pointer ABI unchanged.

This deliberately chooses a metadata constructor, **not** a Core nominal
`InlineArray` registration, new source spelling, user indexer or foreign API
recognizer. The implementation's intrinsic local-array carrier can acquire
this complete identity from its verified E/N facts. A genuine nominal array
or wrapper cannot be erased into it: preserve that declaration, Module
origin, complete arguments/constant domains and real containing subplace.
Equal spelling, extent or an arbitrary constant argument does not establish
this family. A future nominal Core array can contain this storage through an
independently admitted declaration; its API/registration is not selected here.

Scope is homogeneous, headerless, fixed rootless Copy inline storage and its
typed local element places, including nested arrays and independently
admitted containing aggregates. No heap/dynamic/capacity array, slice,
arbitrary stride, GC/support-bearing element, implicit allocation, marshalling,
bulk initializer, frozen aggregate codec or new whole-array foreign mapping
is supplied. Existing typed operations still need their own actual support.
The family is Copy under E's admitted Copy contract; copying it does not
copy pointees, acquire backing ownership or initialize padding.

Choose a **positive natural array-container alignment**, at least one byte,
for every admitted E, rather than excluding non-unit zero-size/zero-alignment
elements. This generalizes the unit-only floor without narrowing the stated
rootless Copy scope. It preserves E's own size, alignment and nominal identity;
it supplies neither a payload byte nor zero-extent native backing.

## Accepted rules, prior art and alternatives

The owners reached through the [specification index](../specs/README.md)
already determine the following; the physical additions below are unapproved.

| Evidence | Already determined; missing boundary |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [metadata](../specs/il-metadata.md#section-kinds-and-row-layouts) | Preserve constructor, complete arguments, nominal origins and qualifier positions. TypeSpecs have no fixed-array constructor/count codec; Layout belongs to TypeDef and derives field layout, not repeated closed storage. |
| [PlaceSelection](../specs/il-instruction-set.md#typed-operand-records), [element places](../specs/il-instruction-set.md#fields-elements-and-ranges) | Capture base/index and checks once; PlaceHandle is not a pointer/reference/pin. The accepted element route binds a MethodMap index operation; it has no intrinsic repeated-storage binding. |
| [Layout/ABI](../specs/data-layout-and-abi.md), [Windows layout §§5–6](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride), [WXAG-10](../specs/conformance/target-windows-x64.md#3-aggregate-layout-and-access) | Complete element stride, padding, transitive explicit alignment and checked target-nuint arithmetic govern storage/access. A nominal Transparent-over-void type can retain size/alignment 0/0 without becoming unit; zero-extent native borrowing remains separately gated. No fixed-array source identity, header or managed-array ABI follows. |
| [Native boundaries](../specs/native.md), [Windows calls](../specs/targets/windows-x64-call-interface.md) | Raw calls remain caller-unsafe; an address affects actual eligible backing, not an ABI by-value copy. No implicit root, pin, ownership or lifetime extension. |
| [Manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md), [versioning](../specs/representation/format-versioning.md) | One exact schema per artifact, independent Module closures and explicit dependency schemas; mutable drafts do not authorize equal-version decoding guesses. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`NativeFixedArrayContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs) | Source arrays use canonical table-backed `PrimitiveType` identities and exact u64 counts with geometry derived from the type row. The in-memory identity is not the Standard TypeSpec/table codec. |
| [Array binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.NativeSampleArray.cs), [native-argument binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.DirectNativeImport.cs) | The provisional source path uses structural `[E; N]` and typed integer indexes. Direct NativeParameter local-array addresses now preserve dynamic index and requested Q; source NativeJit evaluates the index once and checks original bounds and suffix. This does not produce or execute Standard A2 Body records. |
| [TypeSpec reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs), [Layout reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmLayoutTableReader.cs), [Body validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs) | No Standard array codec. The selected storage-address validator currently admits scalar HomePlace/count-one sources, not indexed array PlaceHandles. Existing record-writer/framing patterns do not supply the missing type. |

[RootPin array descriptors](reference-root-pin-array-layout-descriptors.md)
remain a **separate native loaded-type handoff**: shape 9, V2 count/stride,
exact typed traversal and relocation. Their descriptor-specific zero, count
and alignment rules do not supply A's element-admission or container-layout
policy; neither a descriptor key nor its 96-byte node is a Standard codec.
Future integration must derive that descriptor after semantic binding,
never serialize it or reuse its byte layout.
The requested `reference-root-pin-provider-fixed-arrays.md` is absent from
this checkout; no additional provider contract is assumed. The available
[provider deployment](reference-root-pin-provider-deployment.md) decision
selects provider loading, not array identity. The
[Core library sketch](core-library-reference.md#general-span-and-inline-storage)
also remains unapproved and does not register its illustrative nominal array.

| Direction | Assessment |
|---|---|
| Minimal nominal amendment: attach E/N to existing complete nominal TypeSpecs and add a fixed-array Layout kind/index-method registration | Preserves a chosen library constructor, but must also select its exact origin, formal/count-domain mapping, per-closed-instantiation shape, source registration and bound callable protocol. Ordinary LayoutField lists cannot encode repetition. Coherent if that nominal API is the objective; it is not the smallest complete source-independent storage prerequisite. |
| **Structural TypeSpec + explicit layout + intrinsic place binding — recommended** | One 14-byte type recipe, one 40-byte layout row per using Module/profile/type and one new reference discriminator in an unchanged 24-byte place record. No nominal/constant-program registration or fake element declarations. Adds an explicit semantic family and element-operation alternative rather than pretending existing nominal/indexer rules imply them. |
| Redesign Layout to own arbitrary complete TypeSpecs instead of TypeDefs; unify field/repetition layouts | Can eliminate a separate layout table, but changes all Layout ownership/ranges, default/profile selection, field correspondence and consumers. It gives no missing identity or index semantics by itself. Reassigning existing draft fields/IDs is permitted, but this wider migration has no necessary benefit for this prerequisite. |
| Infer shape from bytes, fields, a name or RootPin data | Loses exact identity/count, zero-extent cases or binding; rejected. N fabricated FieldDefs and private encodings are not alternatives. |

## Exact schema, allocations and capability

The structural array candidate is **A1**,
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-1`.
For the complete rootless Copy array composition, select **A2**,
[`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`](standard-trrm-rootless-copy-type-evidence.md),
candidate selector **`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft`**.
Its exact meaning inherits A1's F+S+A layout and place records and adds the
TypeDef Copy-evidence rule in the separate proposal. A1 is not an alias or
fallback for A2; a changed composition needs distinct exact agreement.
Both candidates inherit the exact F+S composition
[`trrm/1-draft-foreign-invocation-storage-addresses-1`](standard-native-storage-address-formation.md#exact-schema-and-capability-selection).
Keep `(1,0)`, codec `01`, all existing maps, width partitions, records,
framing and inherited catalogs.

| Namespace | Candidate delta |
|---|---|
| TypeSpec form `u8` | `0x11=FixedInlineArray`, exact 14-byte row below. No atomic/family-code abbreviation, TypeSpecRef tag, Body pseudo-type or nominal DefId. |
| Required section kind `u16` | `0x0012=FixedArrayLayout`, policy `0000`, fixed stride 40. Replaces that reserved gap prospectively, **not** the retired TypeArg encoding. No new exported definition kind. |
| PlaceSelection reference kind `u8` | `3=FixedArrayLayout row`, valid only with targetKind 3 Element; full existing u32 targetRef. Existing kind 2 MethodMap indexing is unchanged. |
| Feature | `20/1=FixedInlineArrayStorage`, governing edition 1, immediate prerequisite `2/1`, deriving `1/1`. Covers this rootless type/layout, intrinsic element places and their ordinary typed access/state flow. |
| Preserved | EC/Body `0E`, E9/Body `0D`, DF/Body `0C`, short `2A`, TypeSpec `0D/0E/0F`, all controls and mapMask. No new instruction, Body kind, Contract, target or runtime-policy number. A2 additionally assigns TypeDef flags bit `0x20` only under its exact schema; A1 does not carry this evidence and cannot admit nominal array elements. |

These are candidates against the current
[registry](../specs/representation/trrm-1.registry.json), not assignments.
This reviews the allocation **by namespace**, not numeric coincidence.
Form 11 leaves the unrelated [G form 10](general-predicate-graph-schema.md)
untouched at no width cost. A reassignment to 10 or a wholesale Layout
repack would be valid draft alternatives; avoiding unnecessary cross-proposal
churn is not a compatibility duty. Section 0012 groups this structural layout
with TypeSpec without preserving retired TypeArg bytes or claiming lower IDs
save space. RootPin's F0/Body-0D collision with F remains separate; A allocates
no Body kind and selects no RootPin composition.

Every Module retaining an A type, layout or intrinsic selection requires
effective 20/1, including nested types, imported expectations, unused
retained records and uncalled Bodies. Each such using Module selects
`targetRequirement=1` and complete Windows x64 TargetProfile **(1,1)** layout
support below. No Feature 3/4/16 is required by an array alone; actual native
calls, function pointers and F contracts retain their own closure. FieldMap,
FieldPath and queries still conditionally require 13. Counts here are
quantities, not ConstantSource requests: A does not complete inherited
registration-pending 9/11/12 or select scalar/G/enum frozen-value codecs.

Select the exact schema **before** indexing, writing, binding or lowering.
Unchanged Standard, F, F+S, C/G and B+RootPin selections gain nothing.
No sniffing by form/section/Feature, aliases, equal-version retries, private
RootPin/Development fallback or obligation-erasing conversion. Separately
supplied providers keep their own explicit supported schema; packing requires
one destination schema carrying all retained obligations. Co-location never
selects a decoder per Module.

## Complete identity and exact layout row

All multibyte fields below are little-endian. The new variable TypeSpec row
has **exactly 14 bytes**, with no padding:

```text
u8 form=0x11, u8 qualifiers=0, TypeSpecRef elementType, u64 elementCount
```

Its identity is `(FixedInlineArray, complete normalized E, uint64 N)`,
independent of target, local name, layout-row number or storage generation.
N is in `0..0xFFFF_FFFF_FFFF_FFFF`; zero is a count, never absence/capacity.
This explicit count domain is not a guessed generic formal/constant codec.
E preserves nominal Module/declaration identity, every nested pointer depth,
qualification and function-pointer contract. Equal-size E types never merge.
Outer write/gc/init qualifiers on this inline family reject; mutable storage
is a place property. Distinct nominal constructors/constant domains remain
distinct outside this structural family; no adapter may erase them.

`elementType` uses the ordinary non-Body row/F2/F3 TypeSpecRef namespace;
indexed structural child rows precede the parent and remain acyclic.
The Body TypeMap uses its existing **kind-11 TypeSpec-row token**, not this
form number or a non-Body row reference. Existing complete-type formation,
substitution and binding apply before comparison. This bounded registration
requires closed, independently admitted rootless Copy E with fixed layout;
nested A and target-valid non-unit E with size/alignment 0/0 are allowed.
Equal zero-size layouts do not merge unit and nominal E. Open
formals/existentials, unique/Cleanup-bearing, support-bearing or unassigned
element representations decline affected use. An address element's cell
layout never follows its pointee storage.

Section 0012 has no extra header or offset index. It is absent when empty;
nonempty length is exactly checked `recordCount * 40` under the normal
directory/count rules. Each row is:

| Offset | Field | Required meaning |
|---:|---|---|
| 0 | `u32 ownerModuleRow` | Required one-based Module row: the **using/source-owner context**, not an entry/root Module or inferred provider |
| 4 | `u32 arrayTypeSpecRow` | Required one-based row of form 11, not a TypeSpecRef, token or TypeMap index |
| 8 | `u32 targetProfileRef` | Required TargetProfile row exactly (1,1); no default/zero profile |
| 12 | `u32 reserved=0` | No serialized Copy, initialized, stable, unsafe or admission authorization |
| 16 | `u64 elementStride` | Completed byte stride S |
| 24 | `u64 byteExtent` | Complete inline extent B |
| 32 | `u64 alignment` | Positive array-container object alignment A, not E's alignment a |

There is exactly one row for each `(using Module, normalized array type,
selected profile)` retained by that Module, including nested array types.
Conflicting or duplicate keys reject; ordinary row order is immaterial.
Every row's owner/type/profile must exist. Global rows are `1..FF_FFFF`
within their actual table, with high byte zero; zero is not a sentinel here.
All E references resolve in **that Module's** declaration/binding context.
A Body inherits it from its owning MethodDef partition, never the caller or
chosen native provider. Structural TypeSpecs remain unowned/shareable only
when their complete meaning is identical in every using context. Keep
separate original import expectations and actual bound origins; clone and
remap a shared recipe if rewriting only one context would change another.

Let E have completed size s, alignment a and transitive explicit alignment R.
Validate E's complete layout under the selected target before deriving this
headerless representation; the container floor does not repair an invalid
element layout, such as positive size with zero alignment.

```text
A = max(1, a)
S = checkedRoundUp(s, A)
B = checked(N * S)
array required-explicit-alignment = E's R
```

The floor is **natural container alignment**, not a supplied explicit
Alignment or a change to E's alignment. Positive a retains A=a. Both unit
and an independently admitted nominal Transparent-over-void E have
`(s,a,R)=(0,0,1)` on this target: their array recipes are valid with
`(A,S,B)=(1,0,0)` for every admitted N, including 0 and 1. E retains its
original 0/0 layout and distinct identity. No minimum payload byte, hidden
anchor or sentinel is inserted. R is inherited unchanged, including the
value one retained by Transparent from void; the floor neither creates
an explicit-alignment requirement nor discards one.

Submitted S/B/A must equal these derived values, not merely fit allocation.
E includes aggregate tail padding; no hidden length/header/state bytes,
spare capacity, extra tail or repacking of E is permitted. R propagates
through enclosing packing under the existing target rules. No declared
array packing/alignment override is introduced. All quantities, rounding,
products and nested offsets must fit target nuint, without signed-int/u32
narrowing, division-derived counts, wrap or saturation.

N=0 has B=0 but still validates E, R, S/A, origins and cycles; it has no
element. Every admitted zero-extent E has S=B=0 even with positive N;
count/type/logical initialization remain meaningful, including for nested
zero-extent arrays whose own alignment is positive.

**Type/layout validity is separate from NativeStorageAddress backing
eligibility.** Neither a whole-array Home with B=0 nor a zero-extent element
provides a native payload address, even with A=1, an in-bounds index or a
positive counted request. Containing padding is not substitute backing.
Unit and nominal E keep their distinct source-reference rules; the selected
target's separate zero-extent native borrow/anchor gate is unchanged.
An inline cycle rejects even at N=0/S=0; pointer edges do not expand pointee
storage.
No validator expands N elements to check a compact shape. Count/size/layout
queries retain their own admitted domain, including the registered-query
nonnegative nint bound; this metadata does not widen it.

## Bound local element selection and F+S consumption

Amend **only** the accepted rule requiring every Element targetRef to be a
MethodMap index: kind 3 instead binds this intrinsic operation to a complete
layout row. This is a new fixed-array operation alternative, not a callable
method alias or inference from a user indexer's name/body. Kind 2 retains its
original bound callable contract and cannot manufacture A geometry.

Reuse the exact **24-byte PlaceSelection record 04**, including its zero
common flags/reserved header and zero final flags:

```text
u8 targetKind=3, u8 accessKind, u8 destinationKind=1, u8 baseKind
u16 destinationIndex, u16 baseIndex
u8 indexKind, u8 targetRefKind=3, u16 indexIndex
u32 targetRef                 // FixedArrayLayout row, not a token
u16 referentTypeMapIndex, u16 flags=0
```

* `baseKind=2 Home` selects that local's storage, not its loaded value;
  role must be 3. `baseKind=1 Register` selects an already captured typed
  PlaceHandle rooted in a role-3 local of this activation, including real
  field/nested-array subplaces. Its complete referent must be the row's
  exact array type; its Module/profile must match. Array values, raw pointers,
  references, descriptor cells, heap/static places and ABI copies are not
  interchangeable bases in this bounded route.
* `indexKind=1 Register` or `2 Home` selects one already evaluated complete
  integer value. Admit primitive signed/unsigned fixed integers and
  nint/nuint, preserving the original width/sign; no bool/character/enum,
  pointer, optional or user conversion. Register selectors use R0..R13,
  not untyped R14/R15 constants. The index's complete type comes from typed
  value flow/Home, not the element type.
* Destination is R0..R13 and matches the destination of assigned `D8` or
  record-indexed short `91`; high D8 control bits are zero. The result is a
  PlaceHandle with exact E and accessKind `1..5` under ordinary Read/Write/
  Initialize/Transfer/Reference authority. It is not a stored/public value.
  Implicit short `62` does not encode this explicit layout binding and is
  not an A spelling in this candidate. No new compact alias is assigned.

Body-local indexes retain all u16 values, including 0000/FFFF, with u32
inventory counts through 65,536. Register selectors have zero high bits.
The global targetRef instead uses its required one-based 24-bit row domain.
Writable element replacement/addressing requires actual writable containing
authority, including the root local's rebindable flag; selecting Initialize
does not manufacture initialization/replacement rights or an initialized array.

Evaluate the source base and index once in ordinary source order, preserving
earlier staging effects. Capture inputs before destination rebinding, even
when a register is reused. Check **original numeric `0 <= i < N` before
narrowing, multiplication or payload access**. Compute checked `i*S`;
retain the captured local/storage generation, immediate parent array,
original index, suffix `N-i`, E, stride/extent, actual alignment, authority
and backing/owner/guard dependencies. Later rebinding does not retarget it.
Nested arrays retain each selection, not a flattened count across parents.
Occurrence alignment follows the whole containing path: at displacement
zero it retains actual base guarantee K; otherwise at most `gcd(K,i*S)`,
intersected with any containing field guarantees. Natural A in metadata is
not proof that an enclosing packed occurrence supplies it.

Checked bounds failure selects the existing exact
[Core IndexOutOfRangeException](../specs/runtime/standard-failures.md#3-checked-index-access)
under an independently admitted failure runtime, or its admitted terminal
policy. Earlier base/index failures propagate unchanged. Publish no failed
place/value/reference, do no element read/write, evaluate no later
RHS/argument and make no native call. Unsupported static layout/representation
declines before affected use; malformed geometry is not a runtime index
event. Handler/event codecs are not completed by A.

The unchanged F+S **EC/StorageAddress record 0E** consumes this completed
PlaceHandle with `sourceKind=1 RegisterPlace`. It still produces an ordinary
complete pointer, never exposes the PlaceHandle bits as one. For an element
at i, its positive u32 request Q checks `Q <= N-i` in E elements, then checked
`Q*S` and containment/alignment before pointer publication. Keep the **full**
known suffix, not only Q. Count N may exceed u32; neither N nor the original
index is narrowed to the request's domain. A whole-array Home address is
instead one object of exact array type, not an implicit pointer to E.

Preserve S's initialized **whole counted array**, stable/rootless positive
referent, access and exact pointer-depth requirements. A readonly containing
path cannot yield a writable pointer; Initialize/Transfer authority, zero
extent, partial/vacant backing or padding cannot qualify. Readonly opaque
void projection still derives geometry from actual E, not void or bytes.
In particular, valid zero-extent array/element layouts reject EC; neither
the container floor nor Q>0 satisfies its positive backing requirement.
No one-past pointer, adjacent-field concatenation, hidden copy/root/pin,
initialization, cleanup or lifetime extension occurs. Raw calls stage the
completed pointer with CallSite source-use 1 and retain the saved
NativeParameter access/minimum/NoEscape and original provider contract.
DF/NativeOutput and reservation source-use 6 remain distinct.
Preserve per-element pointer/backing facts through admitted copies and
stores. Known writes/invalidation cannot leave stale facts or turn lost
required facts into external opacity; apply the existing
[pointer-backing boundary](native-parameter-pointer-backing.md).

## Validation, transformation and integration

**Producers** establish truthful complete type/shape, source authorization,
initialization, original index/check timing, storage/access/alignment,
definition/use, backing and invocation facts. Keep the source-owner Module
through bound traversal, substitution, copying, joins, verification,
dependency binding and lowering. A reused `NativeFixedArray` enum value or
per-local layout tuple is not a complete type key.

**Consumers**, before affected use, check exact selection, directory/row and
Body extents, closed discriminators/reserved bytes, map/record/reference
namespaces and bounds, owner contexts, complete E/array/referent relations,
independent E layout validity, the container floor and inherited R,
supported target equations/cycles, requirements and actual bindings.
Check every retained row/use needed for that closure, not just executed
instructions. Ordinary producer facts need not be semantically re-proved
against source; [reader trust](../specs/representation/reader-trust.md)
still permits reliance on truthful published Body facts. Unsupported
lowering/required facts reject rather than become opaque raw backing.
No failure publishes a partial artifact/program/image or mutates a selected
binding; resource exhaustion is reported separately from malformed input.

**Rewriters/mergers** decode and rebuild, not substitute matching octets.
Remap Module rows, TypeSpec child/array rows and Body type tokens, profile
rows, 0012 row references in PlaceSelection, Imports/ModuleRefs/definitions,
Homes, registers, BlockEntryState, record indexes and every dependent field
path/contract. N/S/B/A are quantities, not references. Preserve distinct Homes,
captured selections and original expectations; retain effective per-Module
20 requirements after trimming. Canonical writers order recipes with valid
child dependencies, layout rows by owner then first logical use, and existing
maps/records by their normal first-use rules. Ordinary equivalent recipe/map
orderings remain valid subject to the explicit uniqueness rules.
Re-select short 91 versus D8 after remapping; rebuild mapMask/counts, record/
block/code/section extents, instruction ordinals and applicable SourceMap
slots, or discard optional source data under its rules.

Binding resolves original identities **before** comparing E/N. Imported
signatures containing A compare normalized E and exact N, not row numbers,
layout equality or “at least” capacity. A changed N is a type change; a
changed element nominal identity/depth is not assignability. Preserve all
retained fixed/coupled target/layout observations and code/load origins under
[binding/replacement](../specs/representation/binding-and-replacement.md).
Relevant changes invalidate cached admission/geometry/generated data and
require affected revalidation/re-lowering; they cannot retarget a live
captured place or become compatible by recomputing byte size.

| Integration owner/gap | Required next work; not supplied by this document |
|---|---|
| Accepted complete types, IL metadata/registry, instruction places, manifest/admission and Windows layout owners linked above | On approval, add exactly the constructor, 0012 row, reference discriminator, Feature registration and bounded equations/semantics together, including the family-specific positive container floor with unchanged E layout/R and zero-extent borrowing gates. Preserve nominal/source, native and consumer-trust boundaries. No normative owner is edited here. |
| Source/bound representation and shared traversal/remapping | Replace the incomplete carrier-only identity with exact E/N and original Module context; preserve pointer-element identity/provenance and nested geometry. Separate the existing positive-count/int/4096 support limit from the wire/target domain. No name/sample policy or array-as-fields lowering. |
| [TypeSpec writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableWriter.cs), [Layout writer pattern](../../sources/Terrum.CodeAnalysis/Modules/TrRmLayoutTableWriter.cs), [artifact indexing](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), [Body writer pattern](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyRecordWriter.cs) | Add exact-schema readers/writers, independent structural validation, manifest detection, type equality/binding and explicit public producer/consumer/native selection. Existing patterns validate emitted rows; private RootPin records are not reusable payloads. |
| Executable array path | Preserve actual initialization and element read/write/check operations through production, source-free reconstruction, transformation/re-emission and JIT/AOT. Home allocation, an array row and S's R14 scalar store do **not** initialize an array. Ordinary element initialization/stores need actual authority and complete publication; any required unsupported construction/value/flow carrier remains a separate gate, never implicit zeroing. |
| F+S and RootPin | Extend selected indexed RegisterPlace/native adaptation beyond constant zero, retaining S's exact suffix/access rules and actual local mutation/readback. Future RootPin composition may derive V2 descriptors from bound A facts but needs its own exact schema, managed element/retirement support and provider qualification. |

Current support remains owned by the [implementation reference](../implementation.md)
and [conformance handoff](../implementation/conformance-gaps.md#cg-001);
this proposal changes neither inventory nor the active work plan.

## Acceptance gates and remaining decisions

These are **required future cases, not tests run**. Use unrelated synthetic
types/APIs and independent producers/consumers; private/native-source array
tests and the existing scalar-only F+S path do not satisfy Standard coverage.

| Gate | Required outcome |
|---|---|
| A-01 Identity/positive | Scalar, committed aggregate, pointer-cell/nested-pointer and nested-array E; same E/N shares identity across valid encodings, different nominal E/inner qualification/depth/N does not. Unit and distinct nominal Transparent-over-void elements retain different array identities despite equal zero extents. Preserve genuine nominal wrappers and original Module/import expectations. |
| A-02 Geometry/boundaries | N=0/1, first/middle/last element, aggregate tail padding, packed/overaligned occurrences, maximum u64 count/representable product and overflow/round-up/nested-offset boundaries. Independently admitted unit and nominal Transparent-over-void E with (s,a,R)=(0,0,1), at N=0/1/max-u64, yield exactly (A,S,B)=(1,0,0) and inherited R=1 without changing E's 0/0 layout; nested zero-extent arrays remain valid with positive alignment. Positive-a E keeps A=a and transitive R. Validate E/R/S/A, unsupported children and all-inline cycles even at zero count; no division-derived count or 4096 wire cap. |
| A-03 Index/order/failure | Signed negative, N, too-wide int128/uint128, last valid index and smaller index domains; side-effecting base/index exactly once, rebinding, later failing RHS/argument. Preserve original index category/timing, earlier effects and no-result/no-call state. |
| A-04 Malformed/admission | Bad 14/40/24-byte extents, qualifiers/reserved words, wrong owner/profile/namespace/row, duplicate/conflicting layout key, missing per-Module 20, missing inherited closure, wrong base/index/referent/carrier, unsupported E/layout and unsafe binding. Reject forged A=0, nonzero S/B or any other equation mismatch for valid 0/0 E, including N=0; the floor cannot repair an invalid E or R. Retained unused rows/Bodies do not evade closure. |
| A-05 Access/state | Readonly attenuation versus write upgrade, complete/vacant/partial initialization, packed underalignment, pointer-cell versus pointee, NoAlias/output overlap and invalidated generations. Valid zero-extent array types still reject whole-Home EC backing; valid in-bounds zero-extent elements reject indexed EC even with A=1 and Q>0. No containing padding or fabricated anchor; preserve unit/nominal source-reference distinctions and the zero-extent native borrow gate. Place selection never grants a pointer/root/pin or repairs lifetime/initialization. |
| A-06 S/native | EC Q below/equal/above suffix, positive full-u32 request, N above u32 and Q=0 rejection; typed/opaque pointers, Read/Write/ReadWrite/NoEscape, direct/E9 calls and independently selected/co-located providers. Observe selected original elements and untouched neighbors, not copies or flattened nested-array suffixes. |
| A-07 Re-encoding/replacement | Permute Module/global/profile/layout rows; rebuild shared recipes for distinct Module contexts; move Home/TypeMap/record references across 15/16, 255/256 and 65535, counts through 65536, global rows through FF_FFFF. Reject out-of-range values. Reconstruct/re-emit/reload without source, updating all maps/offsets/source slots and rejecting E/N/fixed-layout replacement mismatches. |
| A-08 Cross-feature/isolation | Reject A forms under unchanged Standard/F/F+S/A1/RootPin; preserve short 2A and E9/DF/EC meanings. No G/enum/scalar-frozen/RootPin composition by Feature or same version; queries/fields/foreign contracts keep their conditional requirements. Reject dropping 20 while retaining a nested/unused A dependency. A2 nominal Copy evidence is not decoded under any other schema. |
| A-09 End-to-end | Public explicit production, source-free load, non-byte-identical semantic rewrite/re-emission, binding and supervised headless JIT/AOT preserve initialization, dynamic nonzero selection, failed checks and original-storage mutation. Qualify actual final compiler/input/configuration; emission or descriptor admission alone is not execution. |

The provisional recommendation settles **structural identity and uint64
count domain, 14/40-byte records, intrinsic bound-place alternative,
Feature 20/1, the A1 base allocation and a positive natural container
alignment with unchanged element layout/R and zero-extent backing gates**.
The integrated F+S+A2 selection and TypeDef Copy evidence remain
unapproved under the linked proposal. Approval of those choices and their
numeric allocations is still required; none is adopted by writing these
proposals. The standalone F+S proposal is not amended or widened.

Still unselected: a nominal Core array API/source registration, general
constant-generic array recipes/frozen values/bulk construction, non-Copy or
managed element state/retirement, additional targets, and C/G/enum/RootPin
composites. Each needs its own complete decision only if required by the
chosen adoption scope. Implementation and all relevant gates above remain
dependencies; completing this identity/shape slice or a scalar-only test
does not complete indexed Standard storage addressing.
