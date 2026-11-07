# Common GC runtime ABI and bootstrap

**Status: unapproved, provisional Windows x64 interface recommendation.**
This is the focused ABI successor to the
[integrated GC runtime architecture proposal](integrated-gc-runtime-boundary.md),
following the explicit decision:

> It intends one integrated GC runtime interface, for which multiple GC implementations could be provided. Similar to how the .NET GC works

The decision selects an integrated interface, not the physical assignments
below. This proposal recommends one exact compiler/runtime contract, including
its bootstrap and attachment route. It approves no specification, changes no
compiler or runtime, and claims no implementation or qualification evidence.
The analogy selects neither the CLR nor an existing .NET ABI.

The candidate identity is **`terrum-gc/windows-x64/cells-coop/1`**. It is a
case-sensitive, out-of-band agreement on this entire interface, not a TRRM
schema, Feature, TargetProfile number or compatibility claim. Its candidate
target contract is **`windows-x64-integrated-gc/1`**. Neither name reinterprets
the accepted rootless `(1,1)` profile or the prior RootPin `(2,1)` candidate.

## Recommendation and minimum alternatives

Use **one immutable runtime-owned entry table, obtained by trusted host
bootstrap and explicitly installed into each admitted AOT/JIT participant**.
The runtime owns allocation, collection, cell/support bookkeeping, barriers,
mutator coordination and GC Cleanup in one process-wide domain. A selected
collector implements these obligations behind that boundary, directly or
through a runtime-owned adapter. There is no independently selected RootPin
provider or image-private collector.

| Minimum candidate | Assessment |
|---|---|
| Extend the RootPin table and keep separate discovery/admission exports | It can become integrated only by replacing its service, allocation and bootstrap contracts. Preserving its twelve entries, DLL/path and supplementary exports buys no required compatibility. Not selected. |
| Host-known internal calls patched independently by each JIT/AOT loader | Viable inside one jointly implemented runtime, but independent producers still need an exact operation, representation, identity and publication contract. It also needs a common AOT fixup handoff. Not selected as a second physical route. |
| **Runtime-owned table plus explicit host handoff** | One bootstrap export and one image installer suffice. Both compilation strategies consume the same exact table, cells, descriptors and frames. No collector-specific source API or AOT installation path is needed. Selected provisionally. |
| Direct relocating pointers and PC/register relocation maps | A substantive alternative, not a bootstrap shortcut. It replaces stored/call values and every relocation map. Retain the architecture proposal's opaque-cell strategy instead. |
| One statically hosted runtime instead of a selected shared runtime image | Coherent with the architecture if given an equivalent trusted-origin handoff. Not an additional ABI-1 route or fallback. A static runtime in each participant is incompatible with the single domain. |

The selected physical route uses an explicitly selected shared runtime image
and its `TerrumGcBootstrapV1` export. Collector packaging inside that runtime's
admitted closure remains private. Changing to static hosting requires a
separately agreed bootstrap amendment, not probing alternative entry points.

### Baseline envelope

ABI 1 selects cooperative mutators and stop-the-world payload tracing and
relocation. Nonmoving, compacting and generational implementations are
possible behind it. Background work must not race mutable payload access;
concurrent payload tracing, read barriers and managed-reference atomics/RMW
are not admitted by this revision. An implementation requiring them cannot
join by claiming that its write barrier has the same signature.

All managed-edge writes use the generic operations below. A collector may
implement their remembered-set work as a no-op, but compiled code cannot
assume that. Card layout, granularity, heap segmentation, marking algorithm,
allocation headers and implementation/plugin ABI are private.

The source baseline remains
[exact-place reference formation](../specs/language/references.md#formation),
[backing and explicit pinning](../specs/lifetimes.md#passing-and-storage-access),
[construction](../specs/language/ownership-and-cleanup.md#construction-and-publication)
and [complete Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement).
In particular:

* `gc ref<T>` roots one fully initialized exact place, not adjacent fields or
  the object stored in a rooted handle slot.
* `ref *root` explicitly acquires bounded stability; `C(gcObject)` explicitly
  pins the whole class object. Copies share existing support. Neither changes
  access, independent backing/owner/guard obligations or GC Cleanup policy.
* Ordinary references, raw pointers and unsafe authorization acquire no
  implicit root, pin, marshalling or lifetime extension.
* Failed acquisition publishes nothing and reverses actual partial
  acquisitions. Complete retirement is body-then-reverse-declaration-fields;
  unpublished construction rollback is reverse actual acquisition order.

Closed, typed class/box payloads, inline aggregates, fixed arrays and supported
optional descriptors are eligible when their complete layout and retirement
contracts are admitted. This assigns no missing Auto/class layout algorithm,
normal shared-backing support kind, general optional aggregate representation,
erasure, witness, suspension or source-free codec. A route needing an
unassigned representation rejects before use; it does not change source
eligibility or treat an unknown representation as rootless.

## Machine conventions and results

Every declaration below is **machine interface notation, not Terrum source,
Blittable source data or an authored NativeImport API**. All calls use the
Windows x64 platform convention, shadow space, preservation, stack probing
and unwind rules imported by the
[Windows owner](../specs/targets/windows-x64.md#3-imported-platform-rules).
There are no hidden context, exception or dictionary parameters.
Pointers and `u64` are eight bytes; `u32` results use EAX, address results use
RAX. Arguments beyond the first four use ordinary stack positions.

Records are little-endian, alignment 8, with the exact field order and extent
shown. Array strides equal their record extents; `u32` arrays have alignment 4.
There are no implicit tails or compiler packing choices. All reserved fields
and unspecified flag bits are zero. A revision/extent is exact, not a
minimum-prefix test. Unknown revisions, tails or capability bits do not
select another decoder. Changed drafts need explicit whole-contract agreement.

Opaque pointer names denote runtime-owned membership tokens. `Binding`,
`Authority`, `Attachment`, `Thread`, `Type`, `Method`, `ObjectCell`,
`RootCell`, `PinCell`, `PayloadState` and `RootRegistration` are distinct
namespaces, not interchangeable `void*` capabilities inferred from size.
Consumers never inspect opaque cell/thread/descriptor headers.

### Status domains

| Value | Cold bootstrap/admission/installation result |
|---:|---|
| `0 Ok` | Entire requested operation committed. |
| `1 NoResources` | Valid request exceeds actual metadata/runtime capacity; no partial publication. |
| `2 InvalidData` | Malformed records/ranges or inconsistent submitted layout/map data. |
| `3 Unsupported` | Unsupported exact interface, capability, implementation/configuration or representation. |
| `4 Conflict` | Different process selection, already-bound installation, or conflicting data for a canonical identity. |
| `5 NotReady` | Required attachment/closure is not active, or initial bootstrap has failed. |

These are not source exceptions. `AttachThread`, `EnterCallback`, `Allocate`,
`RootExact`, `PinExact`, `PinObject` and `Collect` have the narrower valid-call
results **0 or 1 only**; their meanings are specified at the operation.
Other table operations return no status and cannot fail for valid admitted
state. Unknown status values violate the runtime contract.

The caller supplies valid, correctly aligned machine spans, immutable inputs
through return and disjoint vacant outputs. Plain output pointer words and
AttachmentBinding/Construction/ThreadBinding records are initially zero.
A vacant output Slot
instead retains its prepared residence/type, with live/ownsHold zero and
zeroed private scratch bytes; those bytes are not an initialized source value.
Within valid spans, cold consumers check every count, reference, closed domain
and checked extent before publication. This is not an arbitrary-address
validation or sandbox API.
Fabricated/stale tokens, invalid machine memory, nonvacant outputs, false
loader identity or unwinding origin callbacks violate the call contract;
they are not successful absence or source OutOfMemoryException.

No runtime entry or origin callback exposes native unwind or an ordinary
Terrum event to its machine caller. Modeled source failure is handled by the
generated source boundary, not by returning an unknown runtime status.

## Process selection and physical bootstrap

### Authority and load instance

One trusted process loader authority chooses the runtime **before any
participating allocation, root registration or affected initializer**.
A standalone AOT launch adapter supplies that authority; embedded AOT
libraries and JIT attachments use the existing host authority.
Rootless execution need not bootstrap this interface.

The selected launch/embedding configuration supplies one explicit full UTF-16
runtime pathname and the exact implementation selector/configuration below.
No pathname is read from participating AOT images. There is no implicit
executable-directory, image-directory, System32, PATH, environment-variable,
basename or “first plugin” runtime selection. This assigns the GC bootstrap
input, not a general argv/configuration-file or application launch API.

The authority preflights the selected runtime/implementation native closure,
then obtains the actual retained runtime module handle **H** using
`LoadLibraryExW` with
`LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`.
The explicit full path selects the runtime; the flags are not a runtime
search/fallback policy. Actual dependency mappings must match the admitted
closure. A trusted matching preload is possible only with its original
mapping/closure history established. Namespace/file stability from first
mapping through admission, and retained selected-file/load references
afterward, are deployment obligations, not facts proved by a pathname query.

Resolve exactly `GetProcAddress(H, "TerrumGcBootstrapV1")`. Its target and the
eventual table/helper/callback targets must belong to the **admitted retained
runtime closure**, not necessarily H's own image. Unchecked export forwarders,
another mapping with equal bytes and matching DLL names do not establish this.
Runtime/collector loading executes no affected source code in DllMain or under
the Windows loader lock.

Only this authority may bootstrap participating Terrum GC execution. Its
process singleton serializes selection and all later joins. A second trusted
host loader must join it or reject; it cannot establish another participating
authority. This is a host correctness obligation, not protection against
arbitrary native code bootstrapping unrelated runtimes in the same process.

### Bootstrap records and state

The selected runtime exports exactly:

```text
u32 __cdecl TerrumGcBootstrapV1(
    const GcBootstrapV1* request, const GcBindingV1** vacantOut)
```

`GcBootstrapV1` is **64 bytes**:

```text
u32 abiRevision=1, u32 byteSize=64
u64 hostAuthorityCookie
u8  implementationId[16]
u64 configurationBytes
const u8* configuration
u64 requiredCapabilities
u64 reserved=0
```

The nonzero cookie identifies this already selected host authority instance;
the host retains its actual identity map and backing. It is not serialized
authorization. `implementationId` is a nonzero, exact 16-octet selector in the
selected runtime's deployment catalog, not a GUID with implicit byte swapping.
The configuration is an exact immutable byte string, null only for zero bytes.
Its schema and interpretation belong to that selected implementation. The
runtime validates it completely and retains a copy/immutable ownership before
success. Unsupported selector/configuration does not choose a default.
The common ABI does not standardize a collector plugin loader or option set.

`GcBindingV1` is **80 bytes**, immutable until process reclamation:

```text
u32 abiRevision=1, u32 byteSize=80
u64 capabilities
RuntimeInstance* runtimeInstance
ImplementationInstance* implementationInstance
Domain* domain
Authority* authority
const GcApiV1* api
u8 selectedImplementationId[16]
u64 reserved=0
```

Offsets are `0/4/8`, `16/24/32/40/48`, `56` and `72`.
All five pointer fields are non-null. The selector equals the request.
The instance/domain/authority tokens identify actual runtime-owned instances;
the host associates them with H, the selected implementation/configuration,
complete origin closure and full semantic identity maps. Equality of table
contents, ABI revision, selectors, cookies, file bytes or domain-shaped bits
is **not** an alternative instance-identity test.

The authority's initial state is `Unselected -> Preparing -> Ready` or
`Failed`. Serialize concurrent requests; publish the binding only after all
fallible selection, retention and validation work succeeds. Initial failure
leaves the output null, retains its diagnostic disposition, and latches
`Failed` for this process's selection. Do not retry another runtime,
implementation, path, profile, RootPin interface or private collector.

After Ready, exact repeat bootstrap by that authority with the same H,
implementation and complete configuration returns the same binding if its
required capabilities are supported. A different selection returns Conflict.
A failed additional capability/attachment request leaves the committed binding
unchanged. No call tears it down. Changing runtime or collector selection,
including configuration, requires a new process.

### One table, not a second set of internal calls

`GcApiV1` is **288 bytes**:
`u32 abiRevision=1, u32 byteSize=288, u64 capabilities`, followed by 34
non-null eight-byte code pointers in this exact order. Its capabilities equal
the binding's. Short parameter type names below refer to the records in this
proposal; `T` denotes a `Thread*`, not a generic source type.

| Offset | Exact entry signature |
|---:|---|
| 16 | `u32 CreateAttachment(Authority*, const GcAttachmentRequestV1*, GcAttachmentBindingV1* vacantOut)` |
| 24 | `u32 AdmitTypes(Attachment*, const GcTypeBatchV1*, Type** vacantOut, u32 outCount)` |
| 32 | `u32 AdmitMethods(Attachment*, const GcMethodBatchV1*, Method** vacantOut, u32 outCount)` |
| 40 | `u32 ActivateAttachment(Authority*, Attachment*)` |
| 48 | `u32 RegisterRoots(Attachment*, const GcRootSetV1*, RootRegistration** vacantOut)` |
| 56 | `void UnregisterRoots(T, RootRegistration*)` |
| 64 | `u32 AttachThread(const GcBindingV1*, GcThreadBindingV1* vacantOut)` |
| 72 | `void DetachThread(T)` |
| 80 | `void EnterFrame(T, GcFrameV1*)` |
| 88 | `void ExitFrame(T, GcFrameV1*)` |
| 96 | `void BeginCall(T, GcCallV1*)` |
| 104 | `void ReturnSupport(T, GcSlotV1* savedResult)` |
| 112 | `void FinishCall(T, GcCallV1*, u32 completion)` |
| 120 | `void Poll(T)` |
| 128 | `void EnterForeign(T, GcTransitionV1* vacantToken)` |
| 136 | `void LeaveForeign(T, GcTransitionV1*)` |
| 144 | `u32 EnterCallback(const GcBindingV1*, Method* boundaryAdapter, GcTransitionV1* vacantToken, GcThreadBindingV1* vacantOut)` |
| 152 | `void LeaveCallback(T, GcTransitionV1*)` |
| 160 | `u32 Allocate(T, const GcAllocationRequestV1*, GcConstructionV1* vacantOut)` |
| 168 | `void InitPart(T, PayloadState*, const GcPlaceV1*, GcSlotV1* source, u32 sourceMode)` |
| 176 | `void RetirePart(T, PayloadState*, const GcPlaceV1*, u32 retirementKind)` |
| 184 | `void PublishObject(T, GcConstructionV1* consume, GcSlotV1* vacantOut)` |
| 192 | `void AbortObject(T, GcConstructionV1* consume)` |
| 200 | `u32 RootExact(T, ObjectCell*, const GcPlaceV1*, GcSlotV1* vacantOut)` |
| 208 | `u32 PinExact(T, RootCell*, GcSlotV1* vacantOut)` |
| 216 | `u32 PinObject(T, ObjectCell*, GcSlotV1* vacantOut)` |
| 224 | `u64 ResolveObject(T, ObjectCell*)` |
| 232 | `u64 ResolveExact(T, RootCell*)` |
| 240 | `void RetainSupport(T, Type* valueType, const void* value)` |
| 248 | `void ReleaseSupport(T, Type* valueType, const void* value)` |
| 256 | `void LoadValue(T, const GcLocationV1*, GcSlotV1* vacantOut)` |
| 264 | `void StoreValue(T, const GcLocationV1*, GcSlotV1* source, u32 sourceMode)` |
| 272 | `void CopyRange(T, const GcBulkCopyV1*)` |
| 280 | `u32 Collect(T, u32 kind)` |

Cold entries through RegisterRoots use the cold result domain. Their detailed
rules below constrain which failures apply; they invoke no source initializer,
method or Cleanup. UnregisterRoots is a prepared nonfailing retirement.
No supplementary RootPin/type/method export is required or probed.

Bootstrap, cold admission and installation run on an unattached or Parked host
thread, outside DllMain, the Windows loader lock and source no-safepoint
intervals. A Cooperative caller first publishes/parks through EnterForeign.
Cold work may allocate and wait for domain coordination without becoming an
unacknowledged mutator that blocks its own rendezvous.

Generated code may call through the table or loader-populated copies of these
exact code pointers. JIT may resolve them after bootstrap; AOT obtains them
through the installation below. Targets and binding remain immutable.
An internal-call name, a familiar code address or another runtime's equal
table is not an equivalent binding route.

## Attachment and AOT/JIT installation

`GcAttachmentRequestV1` is **64 bytes**:

```text
u32 abiRevision=1, u32 byteSize=64
u64 attachmentKey
u64 requiredCapabilities
u32 originCount, u32 reserved=0
const GcOriginV1* origins
u32 typeCount, u32 methodCount
const u64* typeKeys
const u64* methodKeys
```

The authority assigns the nonzero, nonreused attachment key to one native
publication closure. It first resolves semantic identities and all original
binding expectations. The required key lists name the complete type/method
closure needed by this publication, including generated cleanup entries.
They are unique, increasing loader keys, not image-authored trusted integers.
The authority derives them from admitted semantic/native input; native images
supply relocatable requirements and fixup subjects, never already-admitted
runtime pointers or authority keys.

The origin records are unique by key and cover this publication's full
required origin closure. Batch/holder origins must belong to it, including
the retained origins of canonical dependencies already active in the domain.

CreateAttachment copies the request and prepares its origin holds
transactionally. Same key and exact request returns the same attachment;
conflicting reuse returns Conflict. Its state is Prepared, not executable.
Types are admitted first; methods can then refer to them. Type-to-cleanup-entry
keys are resolved at activation, so recursive metadata needs no invocation or
fake no-op thunk during admission.

`GcAttachmentBindingV1` is **16 bytes**:
`Attachment* attachment; const u32* activeWord`.
Success fills both pointers; failure leaves both zero. ActiveWord is a
runtime-owned aligned atomic `u32`, initially 0, release-stored to 1 exactly
at successful activation and never cleared. Its address remains valid to
process reclamation. Exact repeated creation returns the same token/word.
The word conveys readiness only, not type/domain membership or identity.

ActivateAttachment checks the complete requested closure, required services,
all type/entry relationships and retained origins. The authority must already
have completed actual code fixups, every image installation below,
executable/cache publication and Windows unwind registration for that closure.
These are trusted publication facts, not facts inferred from an executable
pointer. Activation performs no authored work. Its final Prepared-to-Active
commit allocates nothing and release-stores activeWord=1. A missing closure
returns NotReady,
inconsistent closure InvalidData/Conflict, unsupported requirements
Unsupported; no prefix becomes active. Active repeat activation is idempotent.

Cold descriptors/origin leases from an earlier successful admission may remain
after later publication failure. They are not authorization to allocate or
invoke: all required descriptors/entries must participate in a successfully
activated closure. No initializer or callable stub is exposed on that failed
route. A retry can reuse identical canonical metadata; it cannot retarget it.

Each affected AOT image provides this compiler-owned installer:

```text
u32 __cdecl TerrumGcInstallImageV1(const GcImageBindingV1* installation)
```

`GcImageBindingV1` is **72 bytes**:

```text
u32 abiRevision=1, u32 byteSize=72
const GcBindingV1* binding
Attachment* attachment
const u32* activeWord
u32 typeCount, u32 methodCount
Type* const* types
Method* const* methods
u32 rootSetCount, u32 reserved=0
const GcImageRootBindingV1* rootSets
```

`GcImageRootBindingV1` is **16 bytes**:
`RootRegistration* registration; GcSlotV1* slots`.
The array follows the image's root-set fixup ordinal order, just as types and
methods follow theirs. The authority prepares/retains these slot arrays with
the exact static/runtime residence addresses and admitted types, and registers
them while inactive before installation. They can be loader-owned stable
arrays; no pre-install image code needs to resolve types or mutate GC slots.
Generated code receives the registration/slot pointers needed for actual
holder updates.

The authority calls the export from the actual admitted image instance, not
by process-wide symbol search. JIT uses an equivalent generated installer
with this same signature/record, not another runtime selector. All arrays
follow that image's immutable, emitted fixup-ordinal order. Their semantic
subjects and actual native entry generations must match the loader's
admitted requirements exactly; equal counts/carriers are insufficient.

Before the first installation, the authority checks actual binding identity,
the **Prepared** attachment/activeWord pair, all resolved type/method subjects
and root-set membership/types/residences against that image's requirements.
It uses its retained admission maps; the installer does not inspect opaque
tokens to re-prove those facts. Missing admitted inputs return NotReady at
this host preflight, without invoking the installer.

The installer checks the exact public interface, counts, required capabilities
and any prior installation. It performs no source work or allocation. It
fills private binding/type/method/helper/root-set slots only after the whole
request is valid, then release-publishes an image-local **installed** gate.
Installation does not activate the attachment or invoke source code.
Affected entries/initializers first acquire that installed gate, then
acquire-load the installed activeWord and require value 1 before use.
Before either condition holds, they report/terminate an unadmitted invocation,
not bootstrap a collector or dereference vacant slots. Unknown word values
violate the contract. All affected source execution in DllMain is excluded.

Repeat of the identical installation succeeds. A different binding,
attachment/publication instance or fixup identity returns Conflict; bad
revision/count/data returns InvalidData/Unsupported. For a conforming invoked
installer these are the only nonzero results: resource/readiness work already
completed for installation, not yet for execution. False opaque-token subjects
violate the authority contract, not
an arbitrary-pointer query supported by the installer. Failure writes no slots
and does not change an existing ready image. The installer copies the vectors
and retains no pointer into caller array records; registered slot/residence
backing itself remains retained under its root registration.

The publication order is therefore:

1. Prepare attachment, types/methods, inactive root sets and ordinary native
   relocation/unwind backing.
2. Install every image's GC binding/fixups with activeWord still 0.
3. Finish code/cache publication, verify the entire installation closure,
   then ActivateAttachment release-publishes activeWord=1.
4. Only then publish callable addresses to the application or run initializers.

An installer success for only a prefix leaves all new affected source entries
unavailable through the common zero activeWord. It cannot authorize allocation
or Cleanup through a half-installed type. Ready dependencies from earlier
attachments remain unchanged. A first installation into an already Active
closure is invalid; identical repeat installations remain idempotent.
This assigns both the GC-fixup/activation ordering and the root-registration
handoff without a circular readiness prerequisite.

This fixes the **physical in-process handoff**. Persisting the ordered fixup
subjects in an independently supplied native artifact still needs that
artifact's exact admitted semantic/native carrier. It cannot be invented from
source names, PE addresses or saved loader keys. The successor TRRM and native
carrier gates below remain explicit; they do not leave a choice between PE
RootPin imports and table injection.

No participating image imports an absolute RootPin path, calls GetRuntimeV1,
selects a collector, or receives a process binding from a saved artifact.
Only the launch/embedding authority loads the common runtime. Independently
compiled libraries, AOT-first/JIT-later and JIT-first/AOT-later all join that
same binding and canonical loader.

## Values, cell identity and support

These are the candidate nonforeign stored/call envelopes:

| Complete value | Size/alignment | Representation |
|---|---:|---|
| GC class handle or GC box | 8/8 | Non-null `ObjectCell*` |
| Exact-place GC reference | 8/8 | Non-null `RootCell*` |
| Stable checked reference | 16/8 | `u64 address` at 0, `SupportCell*` at 8 |
| Stable class handle produced by explicit whole-object pin | 16/8 | Payload address at 0, non-null PinCell support at 8 |
| Optional of these descriptor values | Same as present value | None is all zero; Some has a nonzero first word. |

A two-word zero-address/nonzero-support descriptor is invalid, not None.
Stable references may have zero support only with independently valid bounded
external stable backing. The GC support kind admitted here is PinCell.
Normal shared C/box support and the
[normal-backing candidate](reference-normal-backing-support.md) are not aliases.
Every materialized C in this target has the same 16-byte envelope, regardless
of origin; ordinary normal-backed construction/crossings remain gated until
their compatible support kind is explicitly assigned.

These initialized descriptor shapes assign no stored or general call ABI
for `ref<init T>`, no outer `gc ref<init T>`, and no source type for the
internal construction/retirement tokens. Existing bounded rootless
initialization projections keep their own contract.

Nonforeign calls use the Windows integer/indirect-aggregate/hidden-result
classification for these sizes. A stable C receiver borrows the staged
two-word handle, not a guessed object header; a GC class receiver carries
its ObjectCell. Existing rootless call-extent one-pointer borrow projections
remain only at their separately admitted boundaries. None of these managed
envelopes becomes foreign Blittable data.

ObjectCells preserve domain, allocation identity/generation, payload type,
current base/extent/alignment, state and pin/support edges. RootCells preserve
that allocation plus captured exact type, relative place, extent, alignment
and access ceiling. PinCells preserve their exact/whole selection and one
committed movement-exclusion contribution. These are logical obligations;
their headers and heap allocation strategy are private.

Holds protect valid cells and support endpoints. **Holds are not global GC
roots.** External roots are registered live residences; reached typed payload
fields contribute graph edges. RootCell/PinCell support edges are traced with
objects. Unreachable object/support cycles do not become reachable merely
because their fields retain each other's cells or pins.

Copy retains the same support, without another RootCell/PinCell acquisition.
Transfer changes responsible residence without a retain/release pair.
Register aliases create no independent holder. Retain/release are no-failure,
no-safepoint support operations and never invoke authored Cleanup.
Checked `u64` counters cannot wrap or saturate; one independently retired
descriptor/support edge has real storage, so a valid address-space population
cannot require more than `u64` holds. Smaller internal capacities must be
provisioned before the operation, not fail a later valid Copy.

RetainSupport requires a complete Copy Value descriptor; ReleaseSupport
retires one actual responsible Value residence's support, including the
support subset of a non-Copy value when its source contract permits that end.
Neither accepts a payload-role Type or changes source ownership by itself.
They do not update Slot live/ownsHold: lowering couples the typed bit transfer
and slot update in the same no-safepoint interval. Live payload partial-state
changes use InitPart/RetirePart instead of an untracked raw support drop.
Borrowed scans do not acquire an invisible hold.

At the last PinCell hold, atomically exclude relocation, remove exactly its
pin contribution, invalidate its stable selection and release its supporting
edge. Other pins still exclude movement. Metadata retention does not postpone
this endpoint. Reuse of dead cell storage cannot revive a previous allocation
or place generation; arbitrary stale native pointers have no valid descriptor
contract or promised ABA detection.

## Typed layout and type admission

The authority retains the full equality subjects for nonzero, monotonically
assigned `u64` keys in separate type, method, field, retirement and origin
namespaces. Keys never wrap/reuse. Hashes, names, row numbers and addresses
are not equality. Required subjects include:

* Complete normalized Value type, original selected Module/definition and
  source-unit origin, ordered type/constant substitutions, representations,
  optionality and every inner/outer qualification.
* GC payload family, exact class/boxed-payload identity/substitution and full
  layout/retirement contract, independent of outer handle access.
* Original immediate FieldDef identity in that complete containing context;
  declaration order remains distinct from byte offset.
* Complete retirement recipe, selected authored body/absence, derived order,
  failure/affinity obligations, actual allocator and immutable code origins.
* Actual retained semantic/load/code generation, not an intermediate wrapper
  that forwards values from another provider.

These full maps, not just integer-to-name associations, live with the domain.
The loader checks semantic derivation and directed binding. The runtime
checks the physical graph and consistent interning; it need not reparse TRRM
or re-prove producer-published body liveness/cleanup truth.

Type-key assignment includes the Value/payload role: a key belongs to only
one role, so attachment type-key lists are unambiguous. Reusing it for the
other role is a conflict, not a second interning namespace.

Before admission the authority selects one canonical allocation/retirement
binding for each exact payload subject, including its original immutable
cleanup entry and origin closure. Later AOT/JIT producers of the same subject
use that canonical binding; they do not submit their independently regenerated
thunk as a conflicting replacement for the same Type. Additional generated
method entries can still be distinct. A changed selected provider/retirement
binding needs its own admitted subject and ordinary compatibility checks,
not mutation of an old descriptor or an automatic first-submission winner.

### Exact type records

`GcTypeBatchV1` is **64 bytes**:

```text
u32 abiRevision=1, u32 byteSize=64
u32 nodeCount, u32 fieldCount, u32 originCount, u32 originUseCount
const GcTypeNodeV1* nodes
const GcTypeFieldV1* fields
const GcOriginV1* origins
const u32* originUses
u64 flags=0
```

`GcTypeNodeV1` is **104 bytes**:

```text
u64 typeKey
u32 role, u32 shape
u64 size, u64 alignment
u64 retirementKey
u32 child, u32 fieldFirst, u32 fieldCount
u32 originFirst, u32 originCount, u32 retirementOrigin
u64 retirePayloadThunk
u32 layoutKind, u32 flags
u64 elementCount, u64 elementStride
u64 retirementEntryKey
```

Offsets through elementStride match `0/8/12/16/24/32/40/44/48/52/56/60/64/72/76/80/88`;
the new runtime-entry key is at 96. This is a new common record, **not** an
old 96-byte V2 node with an inferred tail. Role is `1=Value`, `2=GcPayload`.
Value flags permit `1=Copy` and, only for descriptor shapes 4–7,
`2=ImmediateWrite`. These are checked type facts; ImmediateWrite describes
access through that descriptor, not the writability of its containing slot.
Optional records obtain immediate access from their qualified child and do
not repeat bit 2. Payload flags are zero. LayoutKind is
`0=Auto, 1=Sequential, 2=Union, 3=Transparent` only for Fields;
other shapes require zero.

`GcTypeFieldV1` is **32 bytes**:

```text
u64 fieldKey, u64 offset, u64 knownAlignment
u32 valueNode, u32 reserved=0
```

Fields occur exactly once in declaration order, including logical zero-data
fields; static fields are not payload fields. KnownAlignment is the actual
guarantee under the complete containing layout, including packing and
transitive explicit alignment, not an assumption of natural alignment.

`GcOriginV1` is **40 bytes**:

```text
u64 originKey, u64 cookie
u64 retainOrigin, u64 releaseOrigin
u32 kind, u32 reserved=0
```

Kinds are `1=semantic/layout`, `2=code/unwind`,
`3=allocator/runtime`, `4=stable root-holder storage`.
Callbacks are exactly `void __cdecl RetainOrigin(void* cookie)` and
`void __cdecl ReleaseOrigin(void* cookie)`. They operate on prepared leases:
nonfailing, nonthrowing, nonsuspending, no safepoint/source code/admission
reentry. Caller-held backing includes the callbacks/cookie themselves.
Release drops a prepared lease or queues disposal; it cannot unload its
own active callback. Successful domain holds are retained to process
reclamation; Release is needed for provisional transaction rollback,
not early domain shutdown.

The authority checks actual executable thunk/entry addresses, code ranges and
unwind publication against those selected origins. An origin callback/cookie
alone proves none of them. The runtime checks record membership, non-null
required addresses and consistent cross-links; it relies on the authority's
truthful actual-origin/publication facts, not a disassembler or address-name
heuristic.

Origin-use ranges contain unique indexes ordered by resolved origin key.
They cover actual definitions, layouts, retirement, helper, allocator and
code/unwind origins. Distinguished retirementOrigin must name the thunk's
kind-2 origin in that range. Repeated origin keys anywhere in the binding
must have identical kind/cookie/callback bindings.

Counts/indexes are `u32`, zero-based in their own arrays. Empty ranges are
`(0,0)` with null zero-count array pointers. Only `child` and
`retirementOrigin` use `FFFFFFFF` for absence. A Value has retirementKey,
retirePayloadThunk and retirementEntryKey zero and retirementOrigin absent.
Every payload has nonzero retirement/entry keys and a non-null complete
retirement thunk, including a derived/no-op recipe.

### Shapes and traversal

| Shape | Exact use |
|---:|---|
| `1 Leaf` | Fully known rootless Value representation; no child/fields. Raw pointers do not trace their pointee. Size alone cannot justify Leaf. |
| `2 Fields` | Inline Value aggregate or class payload with its complete ordered field range; no child. Padding is not a field. Overlapping managed/support fields reject; a rootless Union still needs its separate valid rootless contract. |
| `3 Optional` | Value with one supported nonoptional descriptor child from shapes 4–7, identical size/alignment and the all-zero presence rule. No generic optional tag layout is inferred. |
| `4 ObjectCell` | 8/8 Value handle, child its exact class/box GcPayload. This is a tracing edge, not inline payload storage. |
| `5 RootCell` | 8/8 Value exact-place reference, child its exact referent Value type. Copy does not recursively read that referent. |
| `6 StableRef` | 16/8 Value, child exact referent Value type; scan its PinCell support if nonzero, not the raw address. |
| `7 StableClass` | 16/8 Value, child corresponding GC class payload; present values require whole-object PinCell support. |
| `8 BoxPayload` | GcPayload, one exact stored Value child at offset zero, with its size/alignment. No synthetic FieldDef or normal box conversion. |
| `9 FixedArray` | Value with one exact element Value child, explicit `u64` count/stride, no fields. |

Only Fields has a field range. Leaf/Fields have no child; all other shapes
have the child specified. Shapes 1–8 have count/stride zero.
Inline edges through Fields, Optional, BoxPayload and FixedArray must be
acyclic. Descriptor edges break inline expansion; malformed infinite
structural types are not thereby permitted.

For element E, derive `A=max(1, alignment(E))`,
`S=checkedRoundUp(size(E), A)`, array alignment A and size `checked(N*S)`
in Windows x64 `nuint`. N=0 still validates E, stride and alignment.
S=0 is admitted only for zero-data unit E, with array alignment 1 and no
element address/scan; preserve its logical count. A zero-extent nominal type
does not become unit. Nested and packed containing layouts preserve actual
element/subfield alignment. No dynamic length/capacity is invented.

Scan visits present, live typed field/element residences and their cells.
Copy/drop recursively adjusts existing support only; it does not acquire pins,
destroy referenced objects or execute Cleanup. Relocation transfers existing
field residences and partial-state scan information without a logical Copy.
Raw leaf bits, padding and absent optional payloads contribute no edges.

### Validation and publication

AdmitTypes requires nonzero nodeCount and `outCount == nodeCount`. It checks
all nodes/fields/origins/uses, even unused submitted records; checked
`count*stride`, first/count and address extents; closed roles/shapes/flags;
nonzero keys; required roles/children; field ownership/order; origin closure;
size, alignment and inline-cycle freedom. Positive alignments are powers of
two from 1 through 8192 consistent with the selected Windows layout; only
admitted zero-data unit Value/BoxPayload may use alignment zero.

Check `offset <= parentSize` and `childSize <= parentSize-offset`, all nested
geometry and exact array formulas. Do not strengthen packed alignment or
substitute a same-sized nominal type. A zero-data field remains a logical
field, not an addressable byte. Missing layout/ownership/retirement support
returns Unsupported, never a Leaf/no-op substitute.

Normalize edges to loader subjects, not batch indexes. Intern an identical
`(binding, role, typeKey, exact target contract)` to the same Type pointer;
distinct qualified/nominal subjects remain distinct. Conflicting normalized
data returns Conflict. Prepare immutable graph copies, capacity and new origin
holds before a no-failure commit. Fill all outputs in input order only after
the entire batch commits. Failure leaves every output zero, including entries
already interned, rolls back provisional holds, and changes no existing graph.
Caller arrays may be released after return.

Cold type admission allocates no source object and invokes no retirement
adapter. Payload retirementEntryKey must resolve at activation to a Method
with the CleanupAdapter flag and the exact retirePayloadThunk address/origin.
Symbolic keys permit type-before-method admission; they are not saved method
pointers or permission to call unfinished code.

## Method and generated root-map admission

Method subjects retain the original selected declaration, closed owner/method
substitutions, every logical receiver/formal/result and mode, complete effects,
outputs/dependencies/cleanup/failure contracts and original expectations.
They additionally identify **one immutable native entry publication instance**,
its generation/variant, exact generated frame contract and actual origins.
Regeneration creates another entry subject; signature/code-address equality
does not merge it. Generated cleanup/boundary adapters have their own derived
entry subjects tied to their recipe/boundary, not fabricated source MethodDefs.

### Exact method records

`GcMethodBatchV1` is **80 bytes**:

```text
u32 abiRevision=1, u32 byteSize=80
u32 methodCount, u32 slotCount, u32 inputCount, u32 originCount
u32 originUseCount, u32 reserved=0
const GcMethodNodeV1* methods
const GcSlotMapV1* slots
const GcMethodInputV1* inputs
const GcOriginV1* origins
const u32* originUses
u64 flags=0
```

`GcMethodNodeV1` is **88 bytes**:

```text
u64 methodKey
Type* resultType
u64 homeBytes, u64 homeAlignment
u32 formalCount, u32 slotFirst, u32 slotCount, u32 inputFirst
u32 inputCount, u32 originFirst, u32 originCount, u32 declarationOrigin
u32 entryOrigin, u32 flags
u64 entryAddress
u32 resultSlot, u32 reserved=0
```

Offsets are `0/8/16/24`, `32/36/40/44/48/52/56/60`,
`64/68/72/80/84`. Flags permit `1=HasReceiver`,
`2=CleanupAdapter`, `4=BoundaryAdapter`. Adapter flags are mutually exclusive
and exclude HasReceiver. An ordinary managed method has neither adapter flag.
The declared result Type is always required, including inhabited void/unit;
DoesNotReturn does not replace its type.

Adapters have no logical formals/input rows, unit result and no resultSlot.
Their fixed machine signatures belong to this cleanup handoff or the
independently admitted foreign boundary, not ordinary source parameter maps.
BoundaryAdapter does not itself admit that foreign signature or failure bridge.

`GcSlotMapV1` is **24 bytes**:

```text
u64 homeOffset
Type* completeType
u32 flags, u32 reserved=0
```

Flags are `1=MayOwnHold`, `2=MayBorrowScan`, `4=SavedResult`; at least one of
the first two is required. SavedResult also requires MayOwnHold.
Each mapped type has managed/support traversal, even when its current optional
value is None or its stable-reference support is zero.
Homes are disjoint, within homeBytes, and preserve the type's complete object
alignment. HomeAlignment is a supported power of two sufficient for every
home; it is 1 when no home is needed. Rootless locals need no map row.

`GcMethodInputV1` is **16 bytes**:

```text
u32 formalOrdinal, u32 entrySlot
u8 sourceRole, u8 handoffKind, u16 reserved16=0
u32 reserved32=0
```

Role is `0=explicit formal`, `1=receiver` with ordinal zero and HasReceiver.
Kind is `0=borrowed scan residence`, `1=owned hold transfer`.
Rows cover exactly the support-bearing materialized inputs once, receiver
first then increasing original formal ordinals, not source evaluation order.
The mapped slot's complete type must match the selected signature and its
flags permit that kind. Scalar/unit gaps do not renumber formals.
No duplicate logical input or entry slot is allowed.

ResultSlot is `FFFFFFFF` exactly for a result without support traversal.
Otherwise it indexes this method's one SavedResult slot of the exact result
type. Optional None still requires the result protocol. No other method
field uses an all-ones sentinel. Origin records/uses follow the type rules;
declarationOrigin is kind 1, entryOrigin kind 2, both in the complete closure.
Actual code, prologs/epilogs/funclets, unwind backing, helpers and allocator
origins survive forwarding.

AdmitMethods validates all spans, owned ranges, map flags/extents/alignment,
logical bounds, same-binding Value descriptors before dereference, origins
and exact entry requirements. Method count is nonzero and output count equal.
All rows/uses have exactly one owner; map/range reorderings require remapping.
The runtime need not disassemble code to re-prove body liveness.
Intern exact repeats to one Method pointer, reject conflicting normalized
maps/entry/origins with Conflict, and publish the whole batch plus new origin
holds transactionally. Failure leaves every output zero. Earlier successful
type admission is not undone.

### Shadow frames, not an implicit PC-map convention

ABI 1 chooses **explicit typed shadow frames with dynamic live/hold state**.
At each safepoint every live GC/support value is in a mapped stable home,
registered holder or runtime-owned state. A volatile/nonvolatile register
alone is not a root; Windows unwind tables are not GC maps.

`GcFrameV1` is **48 bytes**:

```text
GcFrameV1* previous
Method* method
void* homeBase
GcSlotV1* slots
u64 homeBytes
u32 slotCount, u32 state
```

`GcSlotV1` is **24 bytes**:

```text
void* residence
Type* completeType
u32 live, u32 ownsHold
```

Live/ownsHold are exactly 0/1; inactive implies ownsHold zero.
For a support-bearing value, ownsHold denotes the responsibility for typed
support retirement, even for current None/zero-support bits. A separately
prepared rootless staging Slot used by InitPart or the typed helpers may
have live 1 and ownsHold 0 without registration; its actual non-Copy source
ownership, if any, remains the compiler's responsibility. Such a slot never
makes a hidden managed value rootless.
For each frame slot, residence equals `homeBase + mapped homeOffset`, type
matches its immutable map and the exact span/alignment is valid.
Zero slots/homeBytes use null arrays/base. Nonempty homes and slot arrays
occupy stable, fully probed activation storage, not movable payloads.

Before EnterFrame, previous is null, state is `0=unentered`, and homes/input
bits and slot types/addresses are prepared without a safepoint. EnterFrame
checks the actual entry token, map, extents and input states, links the
thread's frame chain and commits `1=active` in one no-safepoint interval.
The ordinary entry consumes only its matching prepared call below.
A cleanup/boundary adapter instead consumes the runtime's one-shot permit
for that exact adapter Method and does not consume an outer pending call.

Stores/Copy/transfer/retirement update bits, live state and responsibility
together before a possible stop. Exactly one residence owns each logical
hold; borrowed scan residences never drain another holder. A field in moving
payload is scanned through its object/state, not registered as a stack address.

ExitFrame requires the active top frame. After actual dependency-valid scope
and finally endpoints, it releases remaining owned support in reverse slot
order, clears slots, unlinks and commits `2=exited`. It invokes no authored
Cleanup, allocates nothing and does not collect. Compiler map/endpoint order
must make that support-only fallback valid. Same-frame handlers retire only
exited scopes, not the entire frame.

Partial prologs keep caller staging responsible until entry commit. Epilogs
have no safepoint between removing the last applicable frame residence and
settling its caller-held result. Accurate native unwind/funclets cover every
supported partial state; code transformation rebuilds affected maps/homes.
No PC range, register patch recipe or async suspension-frame codec is implied.

### Calls and result handoff

`GcCallV1` is **40 bytes**:

```text
GcCallV1* previous
Method* target
GcSlotV1* prospectiveResult
u32 inputCount, u32 state
GcInputTransferV1* inputs
```

`GcInputTransferV1` is **16 bytes**:

```text
GcSlotV1* stagedSource
u32 formalOrdinal
u8 sourceRole, u8 flags=0, u16 reserved=0
```

Inputs are exactly the ordered owned-kind subset of the target input map;
borrowed inputs remain caller-held. Each stagedSource is distinct, live/owned
and exact-typed. ProspectiveResult is null only for a rootless result;
otherwise it is vacant private caller scratch of the exact result type.
All plan/input storage is stable and preallocated in its responsible frame.

1. After Check/Prepare, BeginCall pushes state `0=vacant -> 1=prepared`.
   No authored operation/safepoint occurs before the actual callee entry.
2. The callee spills inputs and EnterFrame transfers each owned hold into its
   mapped residence, clearing caller ownership without count changes.
   Commit `2=entered` atomically with frame visibility. Borrowed slots are
   live/unowned. A fault before commit leaves prepared caller holds responsible;
   afterward callee endpoints/unwind own them.
3. After normal checks and required cleanup, ReturnSupport transfers the
   fixed saved-result hold into prospectiveResult and commits
   `3=result prepared`. It is traceable private pending data, not yet source
   publication. Machine result/hidden-buffer bits agree.
4. FinishCall with `completion=1` accepts entered/rootless or result-prepared
   state as appropriate and closes the plan for normal source Publish.
   With `completion=0`, it releases still-prepared inputs and any prospective
   result; entered inputs follow their callee endpoints, not a second drain.
   It clears responsibilities, pops the plan and commits `4=closed`.
   Both normal and admitted exceptional paths settle it before reuse.

Nested calls use a reentrant stack. Helpers/callbacks cannot consume an
unrelated plan; callback permits bookmark the outer chain. Tail calls need an
equivalent proved transfer or ordinary calls. An admitted native host crossing
uses the same protocol; a raw foreign call does not become an owned transfer.
No hidden machine context argument or extra source retain extends a pin.

## Mutators, external holders and transitions

`GcThreadBindingV1` is **16 bytes**:
`Thread* thread; const u32* pollWord`.
AttachThread prepares all thread coordination resources before success and
attaches the current OS thread to the selected binding in Cooperative state.
Output remains zero on NoResources. Duplicate direct attachment is invalid;
callback nesting uses EnterCallback, not another AttachThread.
Thread tokens are OS-thread-affine. DetachThread is nonfailing only with no
frames, calls, construction states, transition scopes or outstanding permits.
Its token/pollWord then become invalid; it does not tear down the domain.

Poll is a safepoint. The runtime may stop a thread only after coherent roots
and partial states are visible, and returns it to Cooperative state after
relocation/updates. Polls are required at managed invocation boundaries,
before fallible allocation/root/pin slow work and on every potentially
unbounded loop backedge. A compiler may omit a poll only with a bounded
no-safepoint proof, not a “normally short” loop assumption.

Acquire/publish, typed load/store, support Copy/drop, frame/call commit and
partial-state update intervals are **no-safepoint and no-callback**. They
allocate no bookkeeping after their prerequisites have succeeded. Runtime
coordination linearizes them with stops and pin transitions without waiting
for a mutator while holding a lock that prevents its rendezvous.
No-poll bulk work may delay collection; ABI 1 promises no fixed pause bound.

Helpers that can rendezvous before their final commit retain every provisional
cell/state through runtime-owned temporary roots. Caller-visible input roots
remain live too; an uncommitted cell or returned volatile register is not an
unregistered gap in the graph.

`GcRootSetV1` is **32 bytes**:

```text
u32 abiRevision=1, u32 byteSize=32
u32 slotCount, u32 flags=0
GcSlotV1* slots
u64 storageOriginKey
```

RegisterRoots prepares a static/runtime holder set for its attachment.
Each slot has an admitted support-bearing Value type, stable exact-extent
residence and live/ownsHold initially zero. Its type is in the attachment's
required closure. Registered slot/residence storage cannot overlap another
root set or frame's independent responsible storage. StorageOriginKey names
a retained kind-4 origin covering slots and residence backing. No arbitrary foreign
memory or conservative scan is admitted. On success the runtime copies
registration metadata and retains the actual stable slot/residence storage,
not the short-lived RootSet record; no source value is initialized.
The set becomes usable only with its active attachment.

Generated code manages its live states under the same coherent root-commit
rules and actual source synchronization. UnregisterRoots requires all slots
inactive and all uses settled; it removes scan visibility nonfailingly.
Registration/code retention does not keep inactive holders rooted. Foreign
threads cannot mutate these states outside admitted transitions.

`GcTransitionV1` is **48 bytes**, alignment 8, six opaque `u64` words initially
zero. The runtime owns those words while the token is active; generated code
does not inspect them. Tokens live in stable caller storage and nest LIFO.

* EnterForeign publishes all roots, pending-call state, provisional
  constructions and actual pin dependencies, then commits Cooperative to
  Parked. It may rendezvous. Parked frames/slots are immutable until resumed.
  The caller/native code cannot touch moving payload or mutate managed edges;
  foreign access is only through its independently valid stable/raw boundary.
  This helper acquires no pin and grants no retention.
* LeaveForeign waits out a collection and resumes Cooperative before any
  source access or holder mutation. Native-return and admitted native-unwind
  paths both execute it, including before normalization/source finally work.
  Forced unwind, longjmp, process termination and cancellation are not repaired.
* EnterCallback checks the active exact BoundaryAdapter and its independent
  foreign/failure contract. On an unattached OS thread it prepares a new
  attachment; on the same thread parked in this domain it bookmarks/resumes
  the enclosing foreign scope. It establishes a one-shot adapter entry permit
  and returns the thread binding. NoResources invokes no source code and is a
  boundary/host outcome, not a fabricated source OOM. A different domain or
  unadmitted cooperative reentry violates the boundary.
* The adapter immediately establishes its frame before a safepoint, then uses
  ordinary BeginCall for the managed target. LeaveCallback requires all inner
  frames/calls/constructions settled and reverse-boundary failure contained.
  It restores Parked for the enclosing foreign call, or detaches a newly
  attached thread. It neither consumes the outer call plan nor releases its
  still-needed roots/pins.

These operations assign GC coordination, not a universal callback signature,
automatic marshalling or new native exception bridge. Direct raw foreign
signatures remain caller-unsafe under their actual target contracts.

### Declared fast path

Capability **`0x1=InlinePollWordV1`** is the only optional capability assigned
here. Other bits are unsupported. Without it, pollWord is null and generated
code calls Poll at every required site. With it, pollWord names a runtime-owned
aligned atomic `u32`, stable until thread detach. The exact expansion is:

```text
if (acquireLoad32(threadBinding.pollWord) != 0)
    api.Poll(threadBinding.thread)
```

The runtime release-publishes nonzero when rendezvous is required; generated
code never clears/writes it. Each required site executes a fresh load, with
no hoisting/caching across loop iterations, root-state changes or calls.
The runtime waits for the actual cooperative acknowledgment, not merely a
flag load. Code using this expansion declares capability 0x1 before publication.
Unsupported AOT rejects; a JIT can generate a different, explicitly admitted
baseline version before publication, not silently reinterpret specialized code.

There is **no inline allocation, cell-layout, barrier/card-state or read/RMW
capability in ABI 1**. Even a nonmoving/no-op-barrier implementation must
support all entries. Collector-neutral descriptor bit transfer within the
specified no-safepoint protocols and ordinary rootless payload access are
not permission to inline opaque runtime operations.

## Allocation, unpublished construction and publication

`GcAllocationRequestV1` is **24 bytes**:

```text
Type* payloadType
Type* resultValueType
u32 flags=0, u32 reserved=0
```

ResultValueType is the nonoptional ObjectCell Value whose child is payloadType.
Both are same-binding, active complete descriptors. PayloadType fixes class
versus box, extent, actual alignment, GC allocation/release pairing and the
complete retirement entry. No raw address/header registration substitute is
provided.

`GcConstructionV1` is **16 bytes**:
`ObjectCell* object; PayloadState* state`.
It is a target-internal affine record, not a source GC handle or an init
reference type.

Allocate performs one reached source allocation, possibly collecting first.
Before success it obtains correctly aligned payload/identity storage, the
ObjectCell and **all** bookkeeping needed for subsequent valid partial-state
updates, barriers and eventual retirement scanning. Internal zeroing is
allowed but initializes no source field. Even a zero-byte boxed unit has
allocation identity; no exposable unit address is invented.

Success creates state Constructing and installs one runtime-owned provisional
root on this thread before returning the two non-null tokens. Failure reverses
all actual unpublished runtime acquisitions, returns NoResources with both
words zero, and leaves previous values unchanged. Allocator behavior/fast
paths remain private behind this exact operation.

PayloadState tracks the initialized **managed/support scan subset**, not a
second public source field-state system. Constructing scans only established
typed parts, never uninitialized payload bytes. Runtime storage for later
mask/split/merge work is already reserved; valid InitPart/RetirePart cannot
introduce a new source allocation failure.
Logical rootless/zero-data initialization and complete type/value validity
remain producer facts checked by source verification and published contracts.
The runtime does not re-prove them by observing zero bytes.

### Typed partial-state operations

InitPart takes one captured typed path into this PayloadState's object and a
complete source slot. `sourceMode` is `1=Copy`, requiring Copy, or `2=Transfer`,
requiring the actual responsible source. It installs into an eligible vacant
subplace, retains/transfers existing support, performs the runtime's required
barrier work and atomically marks that typed part scan-live. Transfer clears
source live/responsibility without changing support counts.
No user initializer, evaluation, Cleanup or safepoint occurs inside the helper.
Nested fields/elements can be initialized independently; completed aggregate
staging may instead install a whole subtree.

The compiler retains its **actual acquisition order**, including interleaved
partial children, replacements and transfers. Runtime scan state is not that
source rollback journal. Before invoking source work or a safepoint it has
recorded every outstanding acquired child in a valid responsible residence.
Source operations resolve the current ObjectCell base afresh; a constructor
cannot keep an interior address over a safepoint merely because it owns state.

RetirePart applies to Constructing state or the runtime's Retiring state
described below. `retirementKind=1` drops one live Copy subtree's support and
clears its scan state in the same no-safepoint interval; no referent Cleanup
runs. `retirementKind=2` forgets an actually retired subtree after generated
source retirement has discharged its obligations. Every nested support leaf
has already been retired/cleared; this mode cannot silently discard live
support. Rootless retirement truth remains a producer obligation.

Generated complete-child retirement uses these updates as its nested fields
end, **before the next safepoint**, so a partly retired parent is never scanned
as a still-complete raw payload. The same mechanism supports the partial live
subset during failed construction and GC field retirement. InitPart may repair
a vacant part of a Retiring receiver only under its already checked complete-
receiver replacement contract; it cannot publish or resurrect that receiver.
An authored body's replacement must restore its complete-receiver boundary
within one no-safepoint commit interval before reentrant/source observation.
ABI 1 does not assign arbitrary partial-state mutation of a Published GC
object or bypass a missing owning-field replacement contract.

An independently eligible initialized child can be rooted while its parent
is Constructing. Its full source dependencies still prohibit invalidating
that child during rollback or ending storage while a surviving dependent
requires it. No whole incomplete-object handle or outer GC init reference
is published.

### Commit and abort

PublishObject is called only after **all** constructor/caller initialization,
validation and complete-value obligations succeed, not merely at constructor
return. Runtime checks include complete required managed scan coverage and
matching result type; full source validity is a truthful producer assertion.
In one no-failure/no-safepoint commit it changes Constructing to Published,
transfers the provisional hold into the exact vacant, registered output slot,
sets that slot live/owned, removes the construction root and zeroes the consumed
Construction record. Ordinary source/output publication follows its own bounds
and CPU synchronization.

On failure before that point, generated rollback retires only actual remaining
acquisitions in reverse acquisition order. Complete children run their own
complete cleanup; the incomplete parent's complete Cleanup is never invoked,
even if validation failed after all fields were written.
AbortObject requires all acquired obligations and scan-live parts ended,
and no surviving root/pin/loan that still requires that storage generation.
It nonfailingly removes the provisional root, releases storage through the
same allocator pairing, invalidates the tokens and zeroes the record.
It invokes no authored Cleanup and cannot “fix” an escaped dependent by freeing
its backing. Source failure during rollback retains its actual failure rules.

## Exact places, resolution and explicit pins

`GcPlaceV1` is **32 bytes**:

```text
u32 stepCount, u32 accessBits
const GcPlaceStepV1* steps
Type* exactValueType
u64 knownAlignment
```

`GcPlaceStepV1` is **16 bytes**:
`u32 kind, u32 reserved=0; u64 selector`.
Access is exactly `1=read` or `3=read/write`; it is a producer-checked ceiling,
not permission discovered by looking up an address.

Steps select `1=immediate Fields declaration ordinal`,
`2=FixedArray element index`, or `3=BoxPayload value` with selector zero.
Each step matches the current shape; no step follows an ObjectCell stored in
a field, unwraps an optional payload or crosses a pointer. Index checks use
the full captured `u64` before narrowing/address arithmetic.
Zero steps selects the current **Value**; it cannot retype a class payload as
a handle slot. BoxValue explicitly reaches its child at offset zero.
Paths are captured/evaluated once, contain no callbacks and need not survive
the helper call.

The runtime derives relative offset/extent from the admitted graph using
checked `nuint` arithmetic, exact T and established presence/state. Requested
alignment is a supported power of two no stronger than the path's preserved
guarantee, including packing; accidental current alignment cannot justify a
promise after relocation. Zero-data unit and unsupported zero-extent nominal
places have no root/pin acquisition in ABI 1. This is a representation gate,
not a conflation of those source types.

RootExact requires a live responsible ObjectCell residence and one fully
initialized, source-eligible selected place. The object is Published, or an
eligible initialized child in Constructing state; retirement-worker access
has the stricter ticket bounds below. It checks same-domain membership,
exact typed geometry and the output RootCell Value type/access. Source
initialization, owner/guard, authority and nonescape truth remain producer
obligations, not deductions from numeric bounds.

The output descriptor's immediate access, `3` for ImmediateWrite and otherwise
`1`, must be a subset of accessBits. A shared
cell may retain a greater ceiling than a particular attenuated source view;
verification still limits every Copy, pin and use to that view's authority.
PinObject obtains its requested ceiling from the output StableClass type,
with compatibility against the source view remaining a producer obligation.

The runtime allocates a fresh RootCell/support edge and commits into the
vacant registered output slot before another safepoint. It stores the captured
allocation/relative selection, not the source handle variable or a payload
address. NoResources reverses partial acquisitions and leaves the entire
output vacant/zero. Existing destination values are replaced only after
successful staging, not overwritten by an acquisition helper.

PinExact requires a present RootCell; it preserves exact T/access and other
dependencies and produces a StableRef Value. PinObject requires a complete
GC class object and produces the same nominal StableClass Value, not a box
conversion or normal shared allocation. Each explicit acquisition:

1. Prepares its PinCell/support while the source remains rooted.
2. Under relocation exclusion resolves the current base and adds exactly
   one pin contribution for that containing allocation.
3. Commits the two-word value into the registered output with no intervening
   fallible work/safepoint.

Separate explicit pins have separate PinCells; Copies share their existing
cell. Failure removes any unpublished contribution/edges before returning
NoResources and no value. Whole-allocation movement exclusion grants only the
selected place's access for PinExact. It improves no alignment and preserves
GC allocation/Cleanup policy.

Complete T's object alignment is not automatically the alignment of a
referent reached through a root/reference, including after separate
compilation. Without a stronger preserved fact, lowering uses exact-extent
unaligned-safe accesses, preserving required primitive widths. An aligned
atomic or native typed-pointer crossing still needs its separate actual
alignment proof; the two-word envelope and pin cannot supply it.

ResolveObject/ResolveExact are nonallocating, non-safepoint current-address
lookups for an already live, admitted object/place. The address is usable
only for the following bounded typed access/transfer in a **no-safepoint,
no-callback interval**. It cannot survive a poll, allocation, managed call,
foreign transition or staged source work. A pin-derived stable address instead
has its actual bounded support lifetime; raw pointer projection remains
explicitly unsafe and does not retain it.

At a stop, moving collection copies typed live payload/partial state,
transfers existing field residences without Copy, updates ObjectCell's current
base before resumption and retires old storage. It never relocates an object
with a live pin contribution or an active cleanup movement exclusion.
Failed relocation allocation leaves the old object/roots valid; no half move
is published. Object/RootCell identity and selected-place generation survive.

## Generic managed loads, stores and bulk copy

`GcLocationV1` is **24 bytes**:

```text
u32 kind, u32 reserved=0
void* basis
const GcPlaceV1* suffix
```

Kinds are:

* `1 Object`: basis is a rooted ObjectCell and suffix is required, starting
  at its payload.
* `2 ExactRoot`: basis is a rooted RootCell; null suffix selects its exact T,
  otherwise selection begins within that T and cannot widen it.
* `3 Pinned`: basis is a live exact-typed `GcSlotV1*` containing a StableRef
  or StableClass with PinCell support. Null suffix selects its referent;
  a nonnull suffix is bounded by that selection. The runtime checks the actual
  address/support/typed relationship and derives the containing GC object.

The basis and captured suffix remain supported throughout the helper.
External zero-support or normal-backed stable references are not managed
heap locations by address coincidence. They use their separately admitted
storage protocol. No helper guesses an ObjectCell from header subtraction.

LoadValue copies one initialized Copy value into an exact-typed vacant output
slot, retaining its support and atomically making it live/owned when applicable.
It is a typed copy, not a read barrier: ABI 1 excludes collectors needing one.
Reading a unique value by value is not made Copy; ordinary bounded borrowed
access still uses its checked source contract.

StoreValue replaces one initialized **Copy** destination. Mode is `1=Copy`
or `2=Transfer` from exact-typed prepared source; transfer source storage is
disjoint from the destination and becomes vacant. It:

1. Completes all validity/access checks and required pre-write collector work.
2. Retains incoming Copy support before retiring old support, or transfers
   the staged hold without another retain.
3. Replaces the typed value and completes all post-write collector work before
   a safepoint can observe it.

Exact self-Copy is valid and must not drop/reacquire its pin. Replacement
executes no authored Cleanup, no acquisition, allocation or safepoint.
Non-Copy initialization/retirement uses the explicit partial-state protocol;
an unsupported owning replacement cannot be a byte store.
Source destination/RHS evaluation occurs once in source order before final
location resolution, with required snapshots/roots retained through nested work.

`GcBulkCopyV1` is **80 bytes**:

```text
GcLocationV1 destination
GcLocationV1 source
u64 destinationFirst, u64 sourceFirst, u64 count
u32 flags=0, u32 reserved=0
```

Both locations select initialized FixedArray Values with the **same exact
Copy element type/stride**, not necessarily the same array count.
Check both full ranges by subtraction before multiplication/addressing.
The operation has typed **memmove/snapshot-of-original-source** semantics for
overlapping whole-element ranges, without promising an atomic aggregate
snapshot to other threads.

Retain all incoming support before releasing any overwritten destination
support, then transfer bits in the correct overlap direction and complete
required pre/post range barriers. This can use multiple nonallocating passes;
temporary holds are bounded by the no-safepoint operation and never become
extra source roots/pins. Unsupported partial-element/managed-overlap layouts
reject at admission. Count zero validates types/ranges but touches no elements;
unit zero stride performs no address formation/support work. No division by
stride or `count-1` is required. Splitting into polling chunks is allowed only
with a preservation proof, not if it changes overlapping-copy observations.

All three helpers are no-safepoint, no-callback, nonallocating and nonfailing
for valid admitted inputs. They preserve actual packed alignment, exact access
extent and required primitive access widths. Padding is not interpreted as
initialized field data, no access exceeds the selected representation extent,
and no unaligned/aggregate atomic access is promised.

A write requires immediate write access under the captured place/root/pin
ceiling and the actual source view, including member/access restrictions.
The physical cell's possibly broader ceiling cannot upgrade a readonly view.

Every edge-affecting store into GC payload, including through a pin, must use
StoreValue/CopyRange/InitPart or the specified retirement operation.
Support retain/release and collector barriers are separate effects; performing
one does not discharge the other. The runtime can maintain cards/remembered
sets privately, including recording whole ranges, without exposing addresses
or sizes to generated code.

Rootless scalar accesses can use ordinary typed instructions after bounded
resolution where no support/tracing edge changes; their required CPU
atomicity/order remains the [memory model](../specs/memory-model.md).
Managed descriptor access here requires its checked confinement/guard/exclusion
protocol. This ABI assigns no concurrently racing multiword descriptor store,
managed atomic publication/RMW or implicit locking promise. CPU release/acquire,
source handoff, GC visibility and reclamation remain independent.
Foreign raw writes cannot install managed cell bits without this protocol,
even when the object happens to be pinned or the caller is unsafe.

## Collection and complete Cleanup handoff

Collect accepts only `kind=0`, requesting a complete reachability pass in this
domain. It is an internal host/runtime operation, not a new source GC API.
It rendezvous/parks all participating mutators, includes registered holders,
construction states, pending calls/results and cleanup-worker roots, and
traces the typed object/support graph. NoResources means the pass could not
be prepared and changes no object retirement/publication state. Ok does not
promise compaction, immediate Cleanup completion or a particular amount freed.
Relocation can remain best-effort while preserving every old allocation.

An unreachable Published object moves exactly once through
`Queued -> Retiring -> Retired -> Reclaimed`. Queuing does not run authored
code on the collector thread. Before dropping the stopped graph, the runtime
establishes internal cleanup roots for the complete receiver and everything
its valid retirement can still access. Pending/in-progress Cleanup and its
fields are not reclaimed or scheduled incompatibly beneath that obligation.
Unreachable support cycles are processed as graph state, not globally rooted
hold counts.

The runtime attaches an unspecified cleanup worker to the same domain and
invokes the admitted complete thunk with the exact signature:

```text
void __cdecl RetireGcPayloadV1(
    Thread* thread, ObjectCell* object,
    void* stableCompletePayload, PayloadState* retirementState)
```

The Type's retirementEntryKey identifies its exact CleanupAdapter Method.
The runtime supplies its one-shot frame-entry permit, internal receiver/root
residence and movement exclusion before the call. PayloadState has kind
Retiring and initially complete live scan coverage. StableCompletePayload
is the current exact complete payload, not a public source PinCell or
permission to republish a handle.

The adapter contains the lowering of the authored body and derived field
retirement against that internal complete-receiver context. It does not call
an ordinary C receiver ABI with a guessed payload/header pointer or fabricate
a stable C descriptor from the ticket. The internal receiver preserves its
GC backing and nonescape restrictions. Source root/pin/reference operations
inside Cleanup still obey their ordinary explicit acquisition rules; the
runtime's private movement exclusion grants no extra source conversion.

The generated adapter:

1. Establishes its admitted frame before source effects/safepoints.
2. Supplies the selected complete receiver and executes the authored Cleanup
   body once, if any.
3. On normal body completion, retires live fields in reverse declaration
   order, recursively using each complete child's actual recipe. It updates
   partial scan state via RetirePart before subsequent safepoints.
4. Returns normally only after all applicable transitive obligations ended.
   Runtime verifies its retirement scan state is empty, marks Retired and
   reclaims only when remaining valid obligations permit.

For a box the one recipe retires stored complete T, not wrapper Cleanup
followed by a second T retirement. Descriptor fields release their support,
not the referenced object's Cleanup. The runtime never performs a second
generic field drain after the complete adapter.

The adapter may call/safepoint under the actual synchronous Cleanup contract;
the receiver remains immobile and its still-needed closure valid throughout.
Temporary root/pin operations needed inside it are bounded by the retirement
ticket and checked nonescape/nonresurrection rules. They cannot publish the
retiring object or outlive the ticket.

Queued/Retiring objects in that still-valid closure are accessible only under
the worker's active retirement authority and the admitted dependency schedule.
Nested managed calls preserve that authority. It is not a general reopening
of a retired object; all ticket-bounded roots/pins end before adapter return.

An uncaught source failure in that worker follows ordinary unhandled-event
termination **inside the adapter boundary**. It never unwinds into the
collector, returns “retirement failed but continue,” resumes remaining generated
field cleanup, or authorizes reclamation of outstanding obligations.
Source construction rollback retains its distinct event/cleanup rules.
Support/origin drop never invokes this adapter.

Thread-affine obligations without a valid transfer, and inter-object
Cleanup/dependency schedules with no valid complete-receiver order, reject
before affected use. A support-only GC cycle is not such a schedule.
Admission/producer checking must establish validity for the admitted graph
patterns; there is no late “scheduler failed” status permitting lost Cleanup.
ABI 1 invents no topological reorder, affinity scheduler, resurrection,
per-copy finalizer or joint normal/GC cycle-collection promise.

## Failure, lifetime and replacement boundaries

| Failure/endpoint | Required disposition |
|---|---|
| Allocate/RootExact/PinExact/PinObject NoResources | Reverse actual unpublished runtime acquisitions first; output remains vacant. Generated lowering then follows the operation's admitted recoverable or explicitly terminal source policy. |
| Recoverable source exhaustion | Prepare/propagate the exact fresh Core OutOfMemoryException at the failed operation with actual event, roots, cleanup and surviving code resources. Event preparation failure has its own admitted outcome; no cached/fabricated event. |
| Missing recoverable support | Reject before affected use. Do not silently convert an admitted recoverable operation to terminal handling or optional None. |
| Cold metadata/loader/thread/callback resource failure | Host/admission/boundary result, not automatically a source allocation event. Existing source outputs/holders remain intact. |
| Invalid runtime state or false producer/loader promise | Runtime-contract violation/terminal handling as applicable, not NoResources, checked index failure, or fallback success. Consumer checking does not promise semantic re-proof or safe recovery from arbitrary native faults. |
| Source expression/index/requirement/Cleanup failure | Its actual earlier/ordinary source event and holder state, not a reinterpretation as GC allocation failure. |
| Last actual support/pin endpoint | Prompt nonthrowing support retirement at the checked endpoint, independent of process-lifetime metadata. Source finally/cleanup needing it runs before that support ends. |
| Image/JIT disposal | Does not release retained canonical metadata, selected binding or actual original code/helper/allocator/unwind origins. |
| Process exit | No added promise to run source Cleanup. No early whole-domain teardown or release API is selected. |

The [standard failure owner](../specs/runtime/standard-failures.md#4-recoverable-allocation-exhaustion)
remains authoritative. Runtime status 1 is not an exception payload and this
ABI does not assign a general exception-event transport. An independently
consumed source route still needs its complete admitted failure/handler codec.

Retain the runtime, implementation/configuration, authority maps, descriptors
and successful origin leases to **process reclamation**. Per-image detach,
empty imports, no current frames or a final pin is not a quiescence proof.
No unload/rebootstrap, live collector swap or early whole-domain shutdown is
allowed. This conservative code/metadata retention does not retain all source
objects, extend loans/guards, or delay support endpoints.

Compatible Module/method replacement is separate. Admit new immutable
type/entry subjects and generated maps only after complete directed binding,
actual ABI/fixup and stronger-relied-on-fact checks. Old type/layout/thunk,
allocator and entry generations are not retargeted while obligations can
reach them. Failed unwind deregistration never permits freeing its backing.
A new process can select another conforming collector without relowering
collector-neutral AOT code; capability-specialized code retains its requirements.

## Supersession, owners and cross-layer integration

This proposal fills the architecture proposal's deliberately unassigned ABI,
bootstrap, maps/frames, allocation/publication, barrier and typed-admission
handoffs with **one provisional candidate**. It supersedes those gaps within
the unapproved proposal set, not normative owners or accepted source semantics.

It replaces the old RootPin dedicated DLL/GetRuntime/info/table,
absolute-path-in-AOT selection, same-DLL helper-membership recipe and
supplementary type/method export packaging. It reuses their exact-place,
typed traversal, call responsibility, failure, provenance and origin analysis,
but does **not** require their byte sizes or aliases. The old
[semantic/runtime](reference-root-pin-runtime.md),
[type](reference-root-pin-loaded-type-descriptors.md),
[array](reference-root-pin-array-layout-descriptors.md) and
[method](reference-root-pin-loaded-method-descriptors.md) proposals remain
the record of their respective prior candidates.

In particular, this assigns **no** successor TRRM schema, Feature revision,
TargetProfile numeric identity, primary opcode, Body kind or private revision.
[Exact RootPin selection](trrm-root-pin-schema-selection.md), Feature `14/1`,
TargetProfile `(2,1)`, F0 and ReferenceSupport `0D` remain their prior,
unapproved exact snapshot. Existing F selections still use NativeAddress
`0D`; no F/G/RootPin composition or decoder inference follows.
The accepted registry remains
[`trrm/1-draft-layout-queries-1`](../specs/representation/registry.md).
Changing draft IDs is allowed, but must be an explicit whole-schema decision,
not a compatibility reader or DLL-string/tag patch.

On approval, integrate these exact owners together:

| Owner | Intended amendment and consequence |
|---|---|
| [Runtime index](../specs/runtime/README.md) | Add a focused `runtime/gc-runtime.md` owner for one process binding/domain, service responsibilities, source/runtime failure distinction, graph support and cleanup handoff. Link semantic owners rather than copy their source rules. |
| [Windows profile](../specs/targets/windows-x64.md), [layout](../specs/targets/windows-x64-data-layout.md), [calls](../specs/targets/windows-x64-call-interface.md) | Add a separate concrete integrated-GC target owner/profile with these value, table, type/method/frame/call/state and transition layouts. Assign its numeric TargetProfile only in the coordinated registry decision. Leave `(1,1)` and its rootless foreign/call-extent rules unchanged. |
| [Windows deployment §§3–5](../specs/targets/windows-x64-deployment.md) | Add common-runtime bootstrap/instance retention, installer/fixup-ordinal handoff, active-closure publication and no-loader-lock rules. Replace no accepted launch surface implicitly; the selected launch adapter must actually supply these GC inputs. |
| [References](../specs/language/references.md), [lifetimes](../specs/lifetimes.md), [types](../specs/language/types-and-normalization.md), [ownership/Cleanup](../specs/language/ownership-and-cleanup.md) | Preserve exact-place versus handle-slot identity, explicit pins, complete access/backing/generation, partial construction, prompt endpoints and one complete backing-specific retirement. Add references to the physical owner, not new source syntax or a collector-specific API. |
| [Memory model](../specs/memory-model.md#compiler-runtime-module-and-ffi-obligations) | Link generic managed-edge operations and independent safepoint, CPU order, source exclusion and reclamation obligations. No language-level card table or managed atomics support is inferred. |
| [Native lifetime](../specs/native.md#runtime-and-code-lifetime), [JIT](../specs/jit.md), [Modules](../specs/modules.md), [sets](../specs/module-sets.md), [graphs](../specs/native-graph.md) | Require shared binding/authority, actual entry generation and origin retention in both strategies. Topology/private packaging does not establish heap sharing, trusted bootstrap or early unload. Compatible ordinary replacement is not collector replacement. |
| [Logical dependency/cleanup records](../specs/representation/logical-records.md), [requirements](../specs/representation/requirements-and-admission.md), [binding](../specs/representation/binding-and-replacement.md) | Preserve all allocation/root/pin/support/store/cleanup/failure requirements through declarations, expected imports, bodies and replacement. Assign any missing semantic/partial-state carriers in their own exact-schema integration; native descriptors do not replace them. |
| [Registry](../specs/representation/registry.md), [versioning](../specs/representation/format-versioning.md), [reader trust](../specs/representation/reader-trust.md), [persistence](../specs/representation/persistence.md) | Select the complete successor schema/catalog before decoding/writing, reconcile numeric candidates deliberately and validate structural/reference/binding requirements. TRRM persists semantic contracts/operations, never H, cells, trusted keys, tables, origin callbacks, selected collector paths/configuration or saved admission authority. |
| Compiler and libraries | Binder/shared bound traversal, substitution/rewriting, verification and source-free binding preserve typed places, responsibility, state/failure paths and original origins. Lowering derives maps, installers and generic barriers from those facts. Safe wrappers remain authored code; no sample/API-name policy, implicit native pin or compiler-owned collector API. |

Private semantic/native/set/graph routes need their own exact preserved
requirements and native fixup subjects. Saved pointers/“already admitted”
flags do not authorize a new process. Rewriting remaps semantic references
and rebuilds affected native maps/entry generations/SourceMaps; it is not
octet substitution or provenance-ledger serialization.

## Proposed conformance cases

These are required future outcomes, **not tests executed by this documentation
pass**. Runtime cases assume complete admitted source/value/failure routes.

| Case | Required outcome |
|---|---|
| GCA-01 Substitution | Run identical baseline AOT bytes and equivalent JIT code in fresh processes with two conforming implementations, including a moving collector and a remembered-set-sensitive collector. Preserve source observations without collector-specific imports/source APIs. A nonmoving run is not relocation evidence. |
| GCA-02 Bootstrap identity | Concurrent AOT/JIT joins return the authority's same binding/domain. Reject second mappings, equal tables/bytes/selectors, conflicting implementation/configuration and a second participating authority. No plugin wins selection. |
| GCA-03 Selection failure | Missing runtime/export/implementation, malformed config, unsupported capability and closure-load failure publish no binding/code. Initial failure stays failed; no alternate path, collector, RootPin, rootless or CLR retry. |
| GCA-04 Installation | AOT-first/JIT-later and reverse order, diamond forwarding, independent libraries and repeat installation preserve fixup subjects and actual native entry generations. Install all GC fixups/root sets before activation; a failed image prefix leaves activeWord zero and all new entries unavailable. Wrong ordinal/count/type/method/binding writes no partial slots. Unready entry invocation cannot run source code. |
| GCA-05 ABI framing | Check every exact size/stride/offset, reserved field, unsupported revision/tail/flag and missing table entry. Verify fifth/later argument positions and preservation independently. No 104/112-byte RootPin alias or inferred 96-byte type stride is accepted. |
| GCA-06 Typed graphs | Reorder/remap recursive class/box graphs, nested arrays, optional descriptors and packed fields; preserve full qualified identities and original origins. Reject inline cycles, unknown Leaf claims, malformed presence, wrong role, managed Union overlap and same-size nominal substitution. |
| GCA-07 Boundary geometry | Exercise first/last indexes, zero counts, unit zero stride, zero-data fields, aligned/packed places and representable high extents. Check full-width range/rounding/multiplication, all-ones sentinels only where assigned, and capacity exhaustion without truncation. Zero-extent roots remain gated. |
| GCA-08 Admission atomicity | Inject resource failure before each type/method/origin commit; all outputs remain zero, provisional leases roll back, old descriptors stay unchanged. Identical repeat returns canonical pointers; conflicting key/map/entry/origin data rejects. False semantic body facts are not claimed to be re-proved. |
| GCA-09 Maps | Force collection across later arguments, nested calls, loop edges, spills, saved/hidden results and exception paths. Only registered typed live homes/holders/runtime state root values. Unwind records, volatile/nonvolatile registers, metadata retention and hold counts alone do not. |
| GCA-10 Call settlement | Exercise prepared abandonment, partial prolog fault, entered parameter early retirement, borrowed inputs, nested callbacks, normal result and exceptional epilog. Each hold transfers/drains once; no invisible caller hold delays the final pin. An equal-signature wrong entry cannot consume a plan. |
| GCA-11 Construction | Collect/move during partial nested construction with initialized managed children and uninitialized siblings. Scan only actual initialized parts. Interleave acquisitions, Copy/transfer and validation failure; reverse actual outstanding acquisitions, never the incomplete parent's Cleanup. |
| GCA-12 Publication | Constructor return before caller initialization publishes no whole object. Final publication transfers the provisional root atomically. Abort with remaining dependents/scan state is invalid; rootless zero bits do not prove source initialization. |
| GCA-13 Exact roots | Root a handle slot, nested scalar, packed field and fixed-array element; later rebind the original handle and relocate backing. Preserve captured selection/type/access without following a stored handle or granting siblings. Root an eligible initialized child without exposing its incomplete parent. |
| GCA-14 Pins | Acquire overlapping exact and whole-object pins, Copy/reborrow them, drop the original root/handle and retire copies in different orders. Keep the actual movement unit stable until each last dependency, then permit movement; preserve original GC Cleanup policy and actual alignment. |
| GCA-15 Acquisition failure | Fail after each private cell/edge/pin preparation stage. Reverse partial acquisitions before status 1, leave old destinations/roots/pins live and output vacant. Recoverable source paths publish the exact actual OOM event or its real preparation failure, not optional None. |
| GCA-16 Barriers | Under a generational implementation mutate heap fields, optional/nested RootCell/PinCell values and pinned destinations. Exercise self-Copy and staged transfer. Retain new before releasing old and preserve both remembered edges and support counts; CPU fences alone cannot replace either. |
| GCA-17 Bulk copy | Forward/backward/exact overlap, nested support leaves, first/last element and count-zero/unit ranges have original-source memmove semantics and correct retain-before-release effects. Reject partial-element overlap, wrong element type/stride and overflow. No chunking changes observations. |
| GCA-18 Transitions | Collect while a foreign call is parked; synchronous/nested callbacks resume the same domain, preserve outer plans and repark. New native-thread callback attach failure invokes no source. Native unwind resumes coordination before source handling. Raw pointers gain no pin/retention. |
| GCA-19 Hold graph | Collect an unreachable object/RootCell/PinCell cycle without treating every hold/pin as an external root. Keep queued complete receivers and required closure intact. Normal/mixed strong cycles gain no collection guarantee. |
| GCA-20 Cleanup | Invoke one complete adapter on a valid stable receiver, body then reverse fields, including nested support retirement and safepoints between fields. Never scan cleared fields, drain twice, resurrect, schedule an invalid dependency order or release backing early. An uncaught body failure terminates within the worker boundary. |
| GCA-21 Capabilities | Baseline always calls Poll. Capability-1 code performs a fresh acquire load at each required site and uses only its live thread pollWord. Unsupported required capability rejects; no card address, cell header, read barrier or managed RMW is inferred. |
| GCA-22 Lifetime/replacement | Dispose intermediate JIT/image handles while values, frames and deferred Cleanup retain actual leaf origins. New compatible entries receive immutable new maps; old ones remain valid. No live collector/configuration swap or early domain shutdown; last-pin retirement remains prompt. |
| GCA-23 Schema isolation | Preserve accepted Standard/rootless and exact prior RootPin/F selections. Reject missing successor schema/carriers, feature-selected decoders, saved runtime authorization and F/G composition inferred from a common runtime. Remap semantic/native identities, never header/tag/DLL strings alone. |
| GCA-24 Scope | Rootless `(1,1)` programs run without GC bootstrap. Foreign data remains directly interoperable only under its real native contract, with explicit unsafe pin/backing obligations. Ordinary normal-backed values do not bind merely because their envelope is also 16 bytes. |

## Dependencies and implementation boundary

The exact table/records/route above are a provisional choice, not an
open-ended physical-route menu. Collector algorithm, card organization,
private implementation packaging and installation product remain runtime
choices behind that contract. A runtime may reject an unsupported selected
implementation; it cannot silently choose another.

Before implementing an independently consumable source/artifact route:

1. Approve/amend the architecture and this physical candidate, then integrate
   the runtime/target/deployment owners together. Numeric TargetProfile and
   semantic capability assignments need their coordinated decision; this
   proposal does not silently reuse Feature `14/1`, TargetProfile `(2,1)`,
   F0 or Body `0D`.
2. Select an exact successor whole schema and complete allocation,
   initialization/publication, ownership/retirement, stored/suspended state
   and failure/handler codecs needed by that route. Existing RootPin B-only
   selection remains unchanged; no missing codec is filled by native keys.
3. Supply the trusted loader's full identity maps, original binding checks,
   retained origin leases and exact native fixup-subject carrier. Implement
   bootstrap/installer/readiness and all generated method/frame facts for
   both AOT and JIT, including foreign and partial unwind paths.
4. Implement a conforming integrated runtime/collector, then independently
   qualify final selected inputs through separate compilation, moving/pinning,
   barrier-sensitive collection, partial construction, failure and complete
   Cleanup. Runtime-only synthetic probes, emission and nonmoving runs each
   establish only their exercised boundary.

The [implementation summary](../implementation.md),
[native inventory](../implementation/native.md) and
[CG-027](../implementation/conformance-gaps.md#cg-027) own support claims.
They record an image-private managed collector and separately scoped old
RootPin descriptor/cell/frame/relocation and synthetic JIT-bootstrap evidence.
They do not record this integrated allocation/barrier/bootstrap ABI or
compiler-produced cross-image GC execution. The
[normal-backing candidate](reference-normal-backing-support.md) also remains
distinct, not an implicit shared-domain implementation.

Concurrent/read-barrier collectors, managed atomic/RMW protocols, optimized
barrier/allocation expansions, normal-backing interoperability, suspension/
erasure layouts, general Published owning-field mutation/repair, reclaimable
metadata, early shutdown and live collector migration need separate exact
amendments if required. They are not hidden fallbacks or prerequisites to the
bounded baseline selected here.
No compiler/runtime implementation, normative approval, schema migration or
broader milestone completion follows from writing this proposal.
