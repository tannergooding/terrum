# RootPin loaded-type descriptor admission

**Status: unapproved; provider-specific interface packaging superseded.**
The [integrated GC runtime proposal](integrated-gc-runtime-boundary.md)
replaces the same-RootPin-provider export and frozen runtime-info/table
dependency with admission through the common runtime/loader boundary.
Typed identity/layout/traversal, transactional interning and retained
retirement/code origins remain reusable obligations. The exact supplementary
export/records below are the prior candidate, not a required successor ABI
or compatibility interface. No adoption, migration or new support is implied.

This is a candidate compiler/loader
handoff, not normative adoption, implementation support or runtime evidence.
It supplements [the RootPin runtime candidate](reference-root-pin-runtime.md)
without changing its operations, value envelopes or function table, and
preserves [the exact RootPin schema decision](trrm-root-pin-schema-selection.md).

## Decision and boundary

Recommend one **cold, transactional loaded-type admission export in the same
RootPin provider**, called by the domain's compiler/runtime loader. It
copies and interns a finite, typed layout graph and returns the opaque
`LoadedType*` values required by the existing helpers, places and support
slots. Descriptors and their retained layout/code/cleanup origins live for
the domain's lifetime. There is no per-type unload or descriptor-release
operation in this candidate.

The existing `windows-x64-root-pin/1` operation table remains exactly
**104 bytes, revision 1, twelve entries**. Its enclosing
`RootPinRuntimeInfoV1` remains exactly **112 bytes**, and
`TerrumRootPinGetRuntimeV1` remains unchanged. The supplementary interface
below is one complete replacement candidate with exact identity
**`windows-x64-root-pin-loaded-types/2`**. It incorporates the fixed-array
descriptor amendment; V1 is not an alias or alternate descriptor decoder.
The operation table/info and supplementary interface must be explicitly
supported before affected initialization or execution. The 104/112-byte
sizes describe only the existing helper table and RuntimeInfo, not V2
descriptor records.

This decision closes descriptor admission and lifetime, not allocation:

* Admission creates no ObjectCell, initialized object, RootCell, PinCell,
  source value or registered root residence. It performs no authored
  initialization or Cleanup.
* Registering an allocation, validating/publishing its completed payload and
  producing its initial GC handle remain the RootPin proposal's **separate
  allocation/construction boundary**. A descriptor is necessary input to
  that boundary, never evidence that arbitrary bytes are an initialized T.
* Host-provided, independently admitted registered objects can exercise this
  interface without establishing general source GC construction.
* Normal shared backing, new ownership representations, external scanning,
  generic/witness/erased/async execution, alternate targets, private or
  Development persistence, and F/G compositions remain outside this decision.
  An unavailable required layout, retirement contract or codec still rejects.

## Why a separate decision is necessary

The identity, layout and retirement **semantics** already have owners; the
handoff producing runtime-owned descriptors does not.

| Existing owner/evidence | Available contract; remaining boundary |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms) | Exact selected definitions, Module/source-unit provenance, normalized constructors, ordered arguments and qualifications already determine identity. They do not assign a loaded-type factory or native descriptor interface. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities) | Resolve original provider identities and every retained expectation before comparing contracts. An Import proxy, physical row, export alias or equal layout is not a new nominal identity. This does not produce a `LoadedType*`. |
| [Layout metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [Windows layout](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride) | Layout/FieldDef/LayoutField retain declared inputs and ordered fields; the target derives checked sizes, offsets and alignment. These rows are not runtime traversal records, and unknown stored representations are not pointer-sized leaves. |
| [Logical dependency records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending), [cleanup records](../specs/representation/logical-records.md#owning-fields-and-cleanup-encoding-pending) | Root/support and complete retirement obligations are preserved; their general physical carriers remain incomplete. The narrow Cleanup clause is not the complete GC retirement description. |
| [Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), [native lifetime](../specs/native.md#runtime-and-code-lifetime), [deployment](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Retain complete receivers, actual allocator/cleanup origins, helpers and code/unwind backing until their obligations end. No shared type-registration or origin-lease ABI is assigned. |
| [JIT](../specs/jit.md#admitted-execution) | Exact runtime type evidence is required where needed; this explicitly selects no dictionary ABI, helper signature or public runtime type object. |
| [RootPin layouts/helpers](reference-root-pin-runtime.md#windows-x64-carrier-and-runtime-interface) | Descriptors must be stable, opaque, runtime-owned and complete. `PlaceRequest`, `SupportSlot` and RetainSupport/ReleaseSupport require them, but none of the twelve entries creates or interns them. “Loads type descriptions from the admitted complete layout” does not define an interface. |
| [Current schema boundary](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), [support inventory](../implementation/modules.md) | `RootPinRuntimeDraft` distinguishes structural F0/ReferenceSupport consumption; that is not runtime descriptor admission. |
| [`EmitManagedDescriptors`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedNodeRuntime.cs) | The prototype emits image-relative reference-offset lists and allocation sizes for its private collector. Those lists provide neither complete type identity nor recursive optional/support traversal, shared-domain interning or retained GC Cleanup origins. |

Consequently this is not a second definition of type equality, a missing
implementation of an already selected shared registration API, or a request
to reinterpret the private managed-node descriptor. It supplies the missing
physical handoff and applies the existing semantic contracts.

### Function-table amendment versus separate loader export

| Candidate | Effect |
|---|---|
| Append one AdmitLoadedTypes entry to the operation table | The new entry starts at offset 104; the table becomes 112 bytes/thirteen entries and its enclosing info becomes 120 bytes. A coherent replacement would select a revised operation ABI/discovery record rather than claiming the original exact 104/112-byte V1 contract. This is feasible for an unpublished candidate, but couples cold loader machinery to every operation-table consumer. |
| **Separate same-provider export; recommended** | Leaves every operation offset, signature, acquisition status and 104/112-byte extent unchanged. One additional required export supplies the cold loader handoff. It uses the same admitted provider and D, not another runtime or configurable provider role. The array amendment replaces the supplementary descriptor ABI with V2, not the operation table. |
| Loader-private factory without a shared handoff | Reasonable only inside one jointly implemented loader/runtime. It does not by itself let separately supplied AOT images and JIT code obtain provider-owned descriptors under the selected opaque-provider ABI. A documented, exact handoff is still required at that boundary. |

The recommendation is based on separation of responsibilities, not an
immutability obligation for unpublished numbers. It intentionally does not
add a general runtime reflection/type-construction service.

## Exact supplementary Windows x64 interface

The admitted instance of `terrum-root-pin-runtime.dll` must also export:

```text
u32 __cdecl TerrumRootPinAdmitLoadedTypesV2(
    Domain* D, const RootPinTypeBatchV2* batch,
    LoadedType** vacantOutTypes, u32 outCount)
```

This and all record/callback declarations below are **machine interface
notation, not Terrum source or authored NativeImport declarations**.
Parameters occupy RCX, RDX, R8 and the low 32 bits of R9 respectively; status
uses the low 32 bits of RAX. Use the same Windows x64 stack, shadow-space,
preservation and unwind rules as the existing provider. There is no hidden
context parameter, aggregate return or alternative calling convention.

Resolve this export from the **same admitted provider instance** whose
GetRuntimeV1 supplied D. AOT loader/startup code and JIT use that same identity
check; a matching DLL basename, address shape or export from a second provider
does not suffice. Missing export or unsupported supplementary revision rejects
before affected use, without trying an extended operation table or private
collector. Do not call with a different D to probe compatibility.

This is a loader operation, not a mutator Copy/retirement helper. It may
allocate metadata and coordinate with the domain while admitting a closure.
It invokes no source code, initializes no type/static group and exposes no
native unwind. It is not called from DllMain, while holding the Windows
loader lock, or from the no-safepoint retain/release/frame commit intervals.

### Identity inputs and the loader authority

One domain loader owns the canonical binding/identity map used by all its
AOT and JIT attachments. Modules do **not** mint their own identity integers
or directly submit unbound tables. A standalone AOT startup loader is that
authority; an AOT library loaded into a JIT host uses the host's authority.
If an image cannot participate in that handoff, decline its affected use.
This assigns no general-purpose Module loading API or new provider setting.
The loader serializes key assignment and admission calls for D; it may admit
new code while other admitted activations run only under the provider's
existing domain coordination.

The loader first resolves complete semantic input and original expectations
using the existing admission/binding contracts. It then interns these exact
subjects in its domain-local map:

| Symbol | Exact subject |
|---|---|
| Value type key | Complete normalized type term: atomic/constructor identity; original selected nominal Module and definition, including source-unit provenance; complete ordered type/constant substitution; representation, optionality and every inner/outer access qualification. Preserve pointed-to types even for non-scanning pointer leaves. |
| GC payload key | Allocation family (class versus box), exact nominal/payload identity and substitution, GC backing and admitted complete payload contract. It is independent of which readonly/writable handle first reaches the allocation. Outer handle access remains on each referring Value type key. |
| Field key | Original selected immediate owner/FieldDef identity in the complete containing context. Imported expectations resolve to that field only after checking their original bindings; declaration order remains separate from physical offset. |
| Retirement key | Exact complete GC retirement recipe: selected authored Cleanup or its absence, derived field retirement/order, complete-receiver and backing policy, actual method/helper/allocator origins and required failure/affinity contracts. “No authored body” is a real derived recipe, not missing metadata. |
| Origin key | One exact retained semantic/load/code/allocator origin, including its selected immutable Module/provider revision and load generation where applicable, not an intermediate forwarding image. |

Physical keys below are nonzero `u64` symbols for these full equality objects,
assigned monotonically without reuse by that **one loader for D**. Zero is
never a type/origin identity; exhaustion declines admission. Hashes may index
the map but cannot establish equality. The map retains the full subjects and
original binding obligations, not just integer-to-name associations.
These are in-process loader symbols, not DefIds, TypeSpecRefs, type hashes,
addresses, serialized authorization or new source declaration identities.

This split is deliberate: the loader validates semantic identities, complete
layout derivation and binding against admitted input; the provider checks
and interns the resulting physical graph. The provider need not reparse
TRRM or semantically re-prove producer bodies. A loader that lies about a
key's subject violates the runtime contract; this ABI does not promise to
detect a same-shaped lie. A conflicting graph for an already interned key
is nevertheless a required rejection.

Fresh load generations cannot replace live descriptor bindings by reusing
symbols. Repacking or forwarding maps back to the original selected subject,
not the new physical row or image. Compatible replacement for a new admitted
execution still uses the existing directed relation; this proposal selects
neither live patching nor hot unload.

### Input records

All records are naturally aligned to eight bytes, little-endian. Pointers
are eight-byte machine pointers. Array elements use the exact strides below;
the `u32 originUses` array is four-byte aligned. No C packing option or
implementation-defined flexible tail changes a record's extent.

`RootPinTypeBatchV2` is **72 bytes/alignment 8**:

```text
u32 abiRevision=2, u32 byteSize=72
u32 targetProfileId=2, u32 targetProfileRevision=1
u32 nodeCount, u32 fieldCount, u32 originCount, u32 originUseCount
const TypeNodeV2* nodes
const TypeFieldV1* fields
const TypeOriginV1* origins
const u32* originUses
u32 flags=0, u32 reserved=0
```

`TypeNodeV2` is **96 bytes/alignment 8**. Its first 80 bytes retain the V1
field positions:

```text
u64 typeKey
u32 role, u32 shape
u64 size, u64 alignment
u64 retirementKey
u32 child, u32 fieldFirst, u32 fieldCount
u32 originFirst, u32 originCount, u32 retirementOrigin
u64 retireGcPayloadThunk
u32 layoutKind, u32 flags
u64 elementCount, u64 elementStride
```

The new fields are at offsets 80 and 88; they are not a variable tail or
TypeFieldV1 payload. The `nodes` span has checked extent `nodeCount * 96`.
Every node uses this stride, including shapes 1–8.

`role` is exactly `1=Value` or `2=GcPayload`. The intern key is
`(D, role, typeKey, targetProfile 2/1)`; Value and payload lookup roles never
alias. `flags` permits only `0x00000001=Copy` on Value nodes; payload flags are
zero. This is a checked type fact, not permission to copy a non-Copy value.
`layoutKind` on Fields nodes is the selected completed layout's
`0=Auto, 1=Sequential, 2=Union, 3=Transparent`; other shapes require zero.
This handoff assigns no missing Auto/class/box layout algorithm.

`TypeFieldV1` is **32 bytes/alignment 8**:

```text
u64 fieldKey, u64 offset, u64 knownAlignment
u32 valueNode, u32 reserved=0
```

`valueNode` names the field's exact complete Value type. The owned field
range contains every instance field once in declaration order, including
logical zero-data fields; static fields are excluded. Offset and actual
guaranteed alignment come from the completed target layout, not field names,
assumed natural alignment or a guessed object header.

`TypeOriginV1` is **40 bytes/alignment 8**:

```text
u64 originKey, u64 cookie
u64 retainOrigin, u64 releaseOrigin
u32 kind, u32 reserved=0
```

Kinds are exactly `1=semantic/layout origin`, `2=code/unwind origin`,
`3=allocator/runtime origin`. The non-null callbacks have the exact signatures
`void __cdecl RetainOrigin(void* cookie)` and
`void __cdecl ReleaseOrigin(void* cookie)`. They manipulate prepared loader
leases only: nonfailing, nonthrowing, nonsuspending, no safepoint, no authored
code and no reentry into descriptor admission. The caller holds each lease
through the call; its retained closure includes the callback/cookie backing
itself. Code leases retain executable/fixup/unwind registration and transitive
helpers; allocator leases retain the actual paired release domain.
ReleaseOrigin drops a lease or queues disposal; it cannot unload its own
callback/cookie backing during the call. Actual deregistration/unload follows
the existing loader rules after callback return.

`originFirst/originCount` names an owned range in `originUses`, whose entries
are indexes into `origins`. It must include the node's complete definition,
layout, retirement and helper/allocator origin closure. `retirementOrigin`
indexes the code origin containing its thunk and must occur in that range.
Origin closures may conservatively retain a whole admitted image, but cannot
replace Leaf's origin with an intermediate image returning Leaf's value.

All graph/array indexes are zero-based `u32`. **`FFFFFFFF` means no child or
no retirement origin only in those two fields**; it is never a successful
descriptor or an ordinary field/origin-use reference. Empty field/origin-use
ranges have first/count `(0,0)`. Nonempty ranges use checked first/count
arithmetic. No new limit is inferred from TRRM's separate 24-bit row domain.

### Closed traversal shapes

| `shape` | Required layout and edges |
|---:|---|
| `1 Leaf` | Value with an independently supported, fully known rootless representation and no managed/support traversal. No child/fields. Raw pointer bits are leaves, never a request to follow the pointee. Size alone cannot justify Leaf. |
| `2 Fields` | Value inline aggregate or GcPayload; the complete ordered field range supplies recursive typed traversal. No child. Padding is not a field. Known rootless Union overlap is permitted only under its existing rootless-Copy/no-owning-child contract; overlapping managed/support fields have no active-state protocol here and reject. |
| `3 Optional` | Value wrapping one nonoptional ObjectCell, RootCell, StableRef or StableClass Value child, with identical size/alignment and no fields. Uses precisely the RootPin candidate's null/all-zero presence rule. Other optional representations require independent support; this assigns no generic tag layout. |
| `4 ObjectCell` | Value `gc C` or `gc box<T>`, 8/8; child names its exact admitted GcPayload. The edge describes the allocation type, not inline payload storage. |
| `5 RootCell` | Value `gc ref<T>`, 8/8; child is the exact stored/referent T Value descriptor. The edge does not inline or recursively read T during descriptor Copy. |
| `6 StableRef` | Value stable `ref<T>`, 16/8; child is the exact T Value descriptor. Support is zero only for an independently valid externally bounded stable place, or the admitted PinCell kind. No new normal shared support kind is inferred. |
| `7 StableClass` | Value stable C, 16/8; child is the corresponding GC class payload. Presence requires nonzero PinCell support from the preserved whole-object conversion/provider contract, not normal C construction. |
| `8 BoxPayload` | GcPayload for an independently admitted `gc box<T>`; child is its exact stored T Value, with identical logical payload size/alignment and no fields. Scan the stored T through that child, without inventing a synthetic FieldDef or normal box construction. Identity/header storage remains the separate allocation boundary's responsibility. |
| `9 FixedArray` | `role=1=Value`, with one required exact element Value child, explicit u64 count/stride, and derived complete extent/alignment. Elements are indexed subplaces, not FieldDefs. |

Only Fields has a field range. Leaf and Fields have no child; every other
shape has the one child specified above. Shapes 1–8 require
`elementCount=elementStride=0`; shape 9 requires zero `fieldFirst`,
`fieldCount` and `layoutKind`. A GC class payload requires Fields, and a GC
box payload requires BoxPayload. Unavailable inheritance, variable-size
payload or other layout forms decline rather than becoming an opaque byte
payload. This restriction does not establish or forbid those source
operations; their independent representation and allocation boundary remain
prerequisites. Shape 9 is local to this new descriptor interface, not an
additional F0 action or Body record.

For FixedArray, the child is the complete element type E and may itself be
FixedArray for nested inline storage. The derived stride and descriptor
storage are:

```text
A = a(E) when a(E) != 0, otherwise 1
S = checkedRoundUp(s(E), A)
size = checked(N * S)
alignment = A
```

Validate `elementStride == S`, `size == N*S`, the child layout and all
arithmetic in the Windows x64 `nuint` domain before admission. Preserve exact
count/type identity; never infer N from size or permit extra tail/capacity.
No padding/state bytes appear between elements or after the final element.
The enclosing array does not repack E or strengthen a packed member inside E.
The Value `Copy` flag remains a checked semantic fact, not a consequence of
shape 9; no element ownership or Cleanup policy is synthesized.

**N=0** has size zero but still requires complete E/S/alignment validation;
it has no element residence and never computes `N-1`. **S=0** is allowed
only for independently admitted zero-data unit E with `size=0`, preserving
the logical count and exact type without division or element-address
formation. The array itself has alignment 1. A distinct zero-extent nominal
E retains its own admitted identity/alignment and does not become unit.
Existing unsupported zero-extent root/borrow gates remain unchanged.
Inline array occurrences use the actual alignment supplied by the containing
layout; they do not automatically receive natural alignment when packed.
No new rule admits arrays of unknown/unsupported element types.

FixedArray is an inline-storage edge for cycle checking, like Fields and
BoxPayload. Reject cycles formed by inline fixed-array storage before
publication; ordinary descriptor/reference edges do not expand inline
storage. Recursive traversal uses a finite visited-state worklist and the
existing budgets, never per-element graph expansion from N.

Value nodes have `retirementKey=0`, thunk zero and retirementOrigin
`FFFFFFFF`. Every payload node has its nonzero exact retirement key and
non-null complete retirement thunk, including a derived/no-op recipe.
The thunk contract is specified below. RetainSupport/ReleaseSupport accept
Value descriptors only; a payload descriptor cannot be used as a support
slot's complete type or PlaceRequest.exactT.

Optional normalization has already removed repeated presence layers under
the [existing rules](../specs/language/types-and-normalization.md#optional-normalization).
None is exactly zero for a one-word descriptor and `(0,0)` for a two-word
descriptor. Some requires a nonzero cell/address word; `(0,nonzero)` is
invalid, not another absence form. StableClass Some additionally requires
nonzero PinCell support. StableRef Some may have zero support only under
its independently checked external backing contract. None does not read or
retain a nonexistent payload.

At a Value residence, recursive traversal has three distinct purposes:

* **Scan:** Fields visit their typed fields, FixedArray visits its N typed
  element residences, and Optional visits only Some. ObjectCell/RootCell/
  PinCell descriptors contribute the corresponding object/support graph
  edges. A StableRef's raw address is not independently followed; its
  nonzero support supplies the edge. Leaf bits and padding contribute none.
* **Copy/support retirement:** retain/release the existing descriptor-cell
  holds through Fields, FixedArray elements and Some; retain before retiring
  an overwritten hold. Do not acquire another root/pin, recursively destroy
  the referenced T, follow a stored class handle as an inline object, or call
  authored Cleanup. Descriptor hold counts are not unconditional collector
  roots.
* **Payload scan/relocation:** scan a reached ObjectCell using its registered
  payload node. Relocation transfers existing field residences without
  another logical Copy. Payload bytes are never frame-slot addresses.

Recursive nominal graphs are represented by batch-local backedges, for
example payload fields containing optional GC handles to one another.
Validate the finite graph before publishing any node. Fields, FixedArray,
Optional and BoxPayload are inline-storage edges; cycles composed of those
edges reject.
Descriptor-to-payload/referent edges break storage expansion, but do not
authorize malformed cyclic structural TypeSpecs or infinite substitutions.
Scanning object/support cycles uses the selected collector's visited-state
protocol, not recursive expansion of the type graph or global rooting of
every descriptor hold.

## Validation and atomic admission

The loader must establish complete supported semantic/layout/retirement
facts before constructing the batch. In particular it checks original
bindings, complete field identities/types/order, target layout and all
retained fixed/coupled observations. An absent cleanup/ownership codec cannot
be replaced by a zero thunk or an “admitted” bit. Source compilation or an
independently admitted host boundary may supply facts unavailable in a
particular serialized route; that does not enable that route.

The provider then performs these physical checks before exposing descriptors:

1. Check exact **supplementary ABI revision 2** and record extent, target
   `(2,1)`, closed roles/shapes/flags, reserved zeros, nonzero keys, node
   count and output count. `nodeCount` is nonzero and `outCount` equals it.
   Output words are initially zero. V1 supplementary records are not a
   fallback.
2. Check every declared array extent with checked Windows x64 `nuint`
   multiplication/addition, including `nodeCount * 96`, every
   record/range/reference, role/child agreement and origin records, including
   unused field/origin entries. Require elementCount/elementStride zero on
   shapes 1–8. For shape 9 require Value role, a real element child, zero
   field range/layoutKind and supported retirement fields.
   Zero-count arrays have null pointers; nonzero arrays have readable storage
   through the call.
   The caller supplies valid readable/writable machine spans; this is not
   an arbitrary-address validation or sandbox API.
   Inputs stay immutable through return; outputs are disjoint from inputs
   and provider-owned storage. Repeated origin keys must have identical
   kind/cookie/callback bindings; conflicting bindings reject.
3. Check completed sizes/alignment and typed field extents:
   `offset <= parentSize` and `childSize <= parentSize-offset`, with checked
   nested offset arithmetic. Alignment is a supported power of two, except
   the accepted zero-data unit's zero alignment, including its logical
   BoxPayload. Preserve actual packed placement and explicit alignment
   obligations from target derivation.
   Do not strengthen field alignment or read beyond a field/presence extent.
   Logical void fields contribute no addressable place; existing zero-extent
   nominal root/borrow gates remain unchanged.
   For FixedArray, derive `A=max(1,a(E))`, `S=checkedRoundUp(s(E),A)` and
   `size=checked(N*S)` in target `nuint`; require the submitted stride,
   size and alignment to match. Validate E and arithmetic even for N=0.
   Unit is the only zero-stride case here; preserve its logical count without
   division, per-element storage or address formation. Actual packed
   containing placement remains subject to its selected layout.
4. Validate storage-cycle freedom, complete shape applicability, supported
   overlap and presence rules, payload retirement/code-origin availability,
   and all required implementation capacities. Unknown layout is not Leaf.
   A finite worklist must visit the complete required closure; a recursion
   limit cannot count unvisited nodes as satisfied.
5. Normalize references to their full loader keys, not batch indexes.
   Intern an identical `(role,typeKey,target)` to the **same** LoadedType
   pointer, including repeated requests and recursive graphs. Compare exact
   shape, completed layout, field/type relationships, qualifications retained
   by the loader keys, retirement recipe and origin bindings. Conflicting
   data for that key rejects; distinct nominal/qualified keys never merge
   because their graphs or sizes match.
6. Prepare all runtime-owned immutable nodes, edge maps and origin holds.
   Call retainOrigin once for each newly retained canonical origin, under
   the prepared nonfailure lease contract. Publish the entire graph and fill
   outputs in input-node order only after all fallible work succeeds.
   On failure, roll back scratch and any provisional origin holds before
   return; existing interned descriptors and live objects remain unchanged.

The provider copies graph/field/origin-use data and keeps the required
origin leases, not pointers into batch arrays, caller frames, movable
payloads or image-private offset lists. Input arrays may be released after
the call. Reordering equivalent nodes, fields' storage locations in the
input arrays, or origin-use arrays requires remapping indexes, not changing
type identity. Field declaration order itself is not freely permutable.

Returned pointers populate loader-owned type fixup slots used by generated
code and frame/slot construction. No affected initializer or entry becomes
callable until its whole required type/provider closure is admitted and
ordinary code/unwind publication succeeds. Inspection or structural TRRM
decoding alone does not call this entry.

For RootExact, `exactT` must be a live Value descriptor of the same D.
Runtime layout compatibility matches its exact complete T and requested
extent against the selected initialized subplace in the registered payload
graph, including admitted nested inline fields and established presence.
BoxPayload exposes its child T at payload offset zero, not a fabricated
field or the box's handle slot.
An in-bounds byte range with the wrong T, a payload descriptor substituted
for a handle-slot type, or a same-sized nominal type does not match.
The loader/provider do not thereby prove source initialization, union active
selection, authority, guard or loan truth; those remain the existing producer
obligations. Writable/readonly use may attenuate the acquired access ceiling,
never merge complete types or upgrade access.

### Status and rejection behavior

The admission entry returns exactly:

| Status | Meaning |
|---:|---|
| `0` | Entire graph committed; every output is a non-null interned descriptor. |
| `1` | Metadata/resource capacity exhausted; no new graph or outputs published. |
| `2` | Structurally invalid graph or inconsistent identity/layout/origin binding. |
| `3` | Unsupported supplementary revision, target, required representation or retirement capability. |

For statuses 1–3 all output words remain zero; there is no partially usable
type set. Unknown status rejects the provider contract. Semantic/binding
preflight may reject before calling the export. A foreign/stale D, invalid
machine memory, callback failure/unwind or fabricated loader identity is a
runtime-contract violation, not a successful load or optional absence.

These are **loader admission statuses**, not RootExact/PinExact/PinObject
statuses and not source allocation outcomes. Status 1 does not itself create
Core OutOfMemoryException or choose a source terminal/recoverable policy.
The original helpers retain their exact 0/1 acquisition/rollback contract.
After admission, invalid cell/type/support arguments to those helpers remain
runtime-contract violations, not descriptor-registration retries.

## Lifetime and retained retirement code

The V2 loaded-type choice is a domain-lifetime descriptor arena and origin
lease set.
For the selected process-wide singleton D, successful admission retains
both until process reclamation or a separately established **whole-domain
quiescent shutdown**. No per-node reference count, weak intern entry,
per-image disposal or final source descriptor Copy releases these leases.
Do not reuse descriptor addresses or loader keys within D.

This conservative lifetime avoids a second cyclic metadata-reclamation and
cross-image unload protocol. Its cost is retaining metadata and needed origin
closures longer than their last value. It does **not** retain every source
object, keep every pin active, change the last PinCell-hold endpoint, delay
ordinary support retirement or make metadata an object root. A future
reclaimable-descriptor lease protocol is a separate decision, not an
implementation-defined extension of this V2 interface.

JIT disposal and AOT image unload must respect the retained leases even when
no direct import/call remains. A successful descriptor registration does not
replace code fixup, executable publication or unwind registration; missing
or failed support rejects before affected execution. A retirement thunk or
allocator is retained with its **actual original origin**, including helpers,
callback backing and unwind records, not just the image containing a wrapper.

The exact payload adapter signature is:

```text
void __cdecl RetireGcPayloadV1(Domain* D, void* stableCompletePayload)
```

This is generated code for the **already selected complete GC retirement
recipe**, not another source Cleanup method or a new construction/finalizer
API. It is called only by the existing GC cleanup path after established
unreachability, once, before reclamation. The provider keeps the complete
payload, fields and transitive obligations alive and excludes movement of
that payload throughout the invocation. This internal cleanup residence
creates no source PinCell or observable pin acquisition.

For a class Fields payload, the adapter supplies the selected authored body's
admitted complete receiver, then retires live fields in reverse declaration
order on normal completion. Without an authored body it performs the derived
field retirement. For BoxPayload, its one recipe retires the stored complete
T, including T's applicable body-then-fields retirement, exactly once; there
is no extra wrapper body followed by a second complete T retirement.
Descriptor fields end their holds, not their referents; owning fields follow
their independently admitted complete retirement contracts. The provider
must not run a second field/support drain after that complete adapter.
Unsupported receiver adaptation, transitive ownership, cleanup affinity or
failure transport rejects admission rather than selecting a same-named method.

Authored Cleanup may execute source effects and safepoints under its existing
contract; that permission is confined to this cleanup-worker invocation.
The adapter contains any source failure at the worker boundary using the
RootPin candidate's ordinary unhandled-event terminal outcome; it exposes no
native unwind to the provider. A thrown Cleanup does not resume generated
field cleanup, report successful retirement or authorize reclamation of
outstanding obligations. No cleanup resurrection or republishing is added.
RetainSupport/ReleaseSupport, origin callbacks and admission never call this
adapter or authored Cleanup.

At an orderly whole-domain shutdown, releaseOrigin may run only after all
calls, frames, cells, objects, deferred/in-progress Cleanup and code/unwind
users are gone. Failed deregistration prevents freeing its backing.
If that shutdown cannot be established, retain the origin to process
reclamation; do not release from a last-value heuristic or DllMain.
Process exit establishes no new promise to run source Cleanup.

## Schema and integration consequences

This proposal adds **no TRRM bytes, Feature, TargetProfile, opcode, Body kind,
TypeSpec constructor or private revision**. It supplements only RootPin's
unspecified loaded-type handoff and corresponding code/lifetime obligation,
using the exact supplementary candidate
`windows-x64-root-pin-loaded-types/2`. It does not replace the
operation-derived provenance ledger or the separate LoadedMethod/frame/
call-transfer contract.

Keep these existing selections exact:

* RootPin remains `trrm/1-draft-root-pin-runtime-1` /
  `RootPinRuntimeDraft`: B + RootPin, Feature `14/1`, TargetProfile `(2,1)`,
  F0 and 16-byte ReferenceSupport Body `0D`.
* Preserve all four F identity/selector pairs from the
  [schema-selection owner](trrm-root-pin-schema-selection.md#exact-proposed-selection):
  ForeignInvocationDraft, CopyOriginForeignInvocationDraft,
  ForeignInvocationGeneralPredicateDraft and
  CopyOriginForeignInvocationGeneralPredicateDraft. Their Body `0D` is
  NativeAddress, not ReferenceSupport; no F/RootPin or G/RootPin combination
  follows from sharing a Windows provider.
* B/Standard, historical CopyOrigin, Development and private persistence do
  not gain RootPin or this runtime handoff. Each supplied dependency keeps
  its explicit schema selection and independent directed binding checks.
  Compatible rootless dependencies may still bind; this does not pack their
  retained F operations into the RootPin artifact.

The supplementary V2 loader ABI must be part of the **explicit prospective
runtime agreement** for this candidate; recognizing RootPinRuntimeDraft or
equal `(1,0)` bytes cannot silently supply it. Preserve the selected schema
and catalog rather than inventing a feature-based decoder or new composition.
As [format versioning](../specs/representation/format-versioning.md) requires,
an already agreed draft snapshot does not acquire changed contracts merely
because a linked proposal changed.

On adoption, the Windows target/runtime/deployment owners would add the
export, record/validation and conservative origin-lifetime rules together.
The existing complete-type, layout, cleanup, binding and consumer-trust owners
remain their semantic authorities. Compiler integration would preserve exact
bound identities and retirement recipes through traversal/rewrite and
verification; the loader would reconstruct this graph after binding; AOT/JIT
would use only the returned descriptors. Standard interchange continues to
persist types/contracts/operations, never these keys, pointers, callbacks,
leases or live descriptor graphs. Regenerate target binding data after any
relevant identity/layout/cleanup change.

There is no remaining descriptor-interface choice hidden behind this
recommendation. Adoption, implementation, the independent allocation/
construction and metadata capabilities, and qualification remain gates.
In particular, writing this proposal selects no production provider
configuration and claims no current runtime support.

## Focused acceptance cases

These are future required cases, not tests run or implementation evidence.
Runtime cases assume the separately admitted registered-object boundary;
none requires or demonstrates general source GC construction.

| Case | Required outcome |
|---|---|
| LTD-01 Shared identity | Independently lowered AOT/JIT views of the same selected origin and complete type receive the same descriptor. Remap batch nodes/origin arrays and repeat admission; preserve pointer identity and exact field order. Separate readonly/write, optional/nonoptional, nominal and Value/payload subjects remain distinct. |
| LTD-02 Recursive graph | Admit two unrelated nominal payloads with nested inline fields and optional GC-handle backedges, including a RootCell/PinCell-bearing field; also admit nested FixedArray descriptors for complete element types. Separately describe admitted GC boxes of a scalar, an inline aggregate and a descriptor. BoxPayload preserves exact T without a synthetic field or new construction path. Trace through typed presence/support edges; metadata and field hold counts do not make an unreachable object/support cycle a global root. Reject by-value storage cycles without publishing any node. |
| LTD-03 Presence and leaves | Exercise None and Some for each supported descriptor envelope, including a stable external ref with zero support. None has no hold; Copy shares Some's existing cell. Reject malformed zero-address/nonzero-support values and stable C with absent PinCell support. Known raw pointer fields are not scanned; unknown representations or an unassigned optional aggregate are not admitted as Leaf. |
| LTD-04 Exact places/layout | Root the first/last eligible field, a nested packed scalar and a GC-handle slot in admitted payloads. Validate exact T, extent and actual alignment without following the object stored in the slot. Reject a same-size different type, wrong role, one-past-end, overflow, fabricated alignment and unsupported zero-extent root. Preserve source access/init/guard obligations. |
| LTD-05 Framing/capacity | Exercise V2 node index zero/last valid index, empty ranges, shared recursive edges and large finite graphs. Check `nodeCount*96` and every V2 array extent. Reject unsupported supplementary revision/target, incorrect record extent, unknown shape/role/flags, nonzero count/stride on shapes 1–8, reserved bits, bad count/output count, null nonempty array, sentinel in an ordinary index, out-of-range child/origin and checked extent overflow. Capacity exhaustion is not a successful truncated graph. |
| LTD-06 Semantic/precompiled input | At loader preflight, reject a field/cleanup/layout recipe inconsistent with its admitted declarations, unsatisfied original Import or unavailable required codec. Reject conflicting graph data for an interned key. Equal names, row numbers, sizes, hashes or DLL names do not merge different origins. False producer body/loader facts are not claimed to be semantically re-proved by the provider. |
| LTD-07 Atomic failure | Inject failure during node/edge preparation and before final publication. Output words remain zero, provisional origin retains roll back, existing types/objects are unchanged, and no initializer or Cleanup ran. Keep loader status 1 separate from actual source root/pin acquisition OOM behavior. |
| LTD-08 Origin and lifetime | Free the caller's batch arrays after success; descriptors remain valid. Forward an object through an intermediate image and drop its direct caller/import; retain the actual leaf layout, thunk, allocator and unwind providers. Ending the last pin unpins promptly despite domain-lifetime metadata. Per-image/JIT disposal cannot free retained origins. |
| LTD-09 Complete retirement | Under the registered-object boundary, queue GC retirement, run the exact authored body once then reverse-declaration fields, and release field-held supports once. The receiver remains complete/stable across admitted calls. A thrown Cleanup terminates under the selected worker policy without resumed field cleanup or unsafe reclamation. Retain/release helpers never invoke the adapter. |
| LTD-10 ABI discovery | Accept only the unchanged 104-byte/twelve-entry operation table and 112-byte `RootPinRuntimeInfoV1` plus the same-provider V2 supplementary export and exact `RootPinTypeBatchV2`/96-byte `TypeNodeV2` shapes. Reject a missing export, V1 supplementary record, wrong D/provider, widened operation table, null callback/thunk or unavailable code/unwind origin before affected use. No private collector or rootless-profile fallback. |
| LTD-11 Profiles | Inspect valid RootPinRuntimeDraft input without creating descriptors; load it only after independent runtime/type admission. Preserve NativeAddress `0D` under all four F selections. Reject F0 under F, E9 under RootPin and retained F+RootPin packing; do not infer a decoder from the new export, record extent or Feature rows. Private/Development and alternate-target gates remain closed. |
| LTD-12 Scope boundaries | A valid graph alone cannot register payload bytes, publish a GC handle, authorize ordinary C/box construction, support erasure/generic/witness/async execution or obtain a new root/pin. Whole-domain shutdown requires actual quiescence; absent that proof, retain code/descriptor origins rather than guessing that disposal is safe. |
| LTD-13 FixedArray shape | Derive `A=max(1,a(E))`, checked `S=roundUp(s(E),A)` and `size=N*S`; reject mismatches, overflow, unknown element types and nonzero field ranges. Preserve nested dimensions and exact nominal element identity. Validate E for N=0; permit S=0 only for unit, with alignment 1 and no element address/scan. Preserve actual packed containing alignment. |
