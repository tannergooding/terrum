# Exact-place roots and explicit pin runtime

**Status: unapproved; provisionally actionable solely under the user's
authorized missing-contract direction.** Every new ABI, helper, operation,
registration and numeric assignment below is a candidate, not an accepted
contract or implementation claim. This document does not revise
[reference provenance carriers](reference-provenance-carriers.md): its
operation-derived compiler ledger remains the provenance model. This proposal
selects actual runtime support for three already accepted operations, not
another declaration relationship or a serialized graph of runtime instances.

## Decision and scope

Select a **moving-capable, cooperative stop-the-world runtime with stable
opaque cells** for a Windows x64 root/pin subprofile:

* A `gc ref<T>` value is one pointer to a nonmoving **RootCell**. That cell
  identifies one initialized place by allocation identity, payload-relative
  offset, exact complete T, extent and access ceiling. Relocation updates the
  allocation's nonmoving **ObjectCell**, not a cached interior address.
* An explicit `ref *root` creates one **PinCell** and an ordinary two-word
  `ref<T>` descriptor, `(stableAddress, supportCell)`. It pins the containing
  allocation without granting access outside the exact selected T place.
* The accepted explicit `C(gcObject)` conversion uses the same PinCell
  protocol, with a whole-object extent. Its two-word stable C handle preserves
  object identity and the allocation's GC cleanup policy. Copies share that
  pin; they neither clone the object nor create normal shared allocation.

Candidate standard integration is **Feature 14/1**, **TargetProfile 2/1**,
long primary **F0**, and typed Body record **0D**. No new source spelling,
TypeSpec qualifier, Contract clause, global metadata table, exception region,
private semantic revision or private expression tag is selected.

The supported boundary is fully typed, closed, admitted places/objects and
their Copy descriptors in locals, parameters, results and traceable fields.
It includes GC-backed class/box subplaces, not a new whole-box conversion.
It assigns the stable reference/C value envelope needed by these operations;
normal shared class/box acquisition and its support-cell protocol remain
separately gated. Unsupported erasure, generic/witness, async/task, guard,
external-memory scanning or target representations remain explicit gates,
not stack-only source rules or implementation-defined fallbacks.

### Preserved source and runtime rules

[References](../specs/language/references.md#formation),
[complete types](../specs/language/types-and-normalization.md#qualification-and-source-positions)
and [lifetimes](../specs/lifetimes.md#passing-and-storage-access) already select:

| Operation or distinction | Preserved requirement |
|---|---|
| `gc ref place`, or fresh `ref place` with complete expected `gc ref<T>` | Evaluate one eligible, fully initialized GC-backed place once; acquire an independent exact-place root. Without that expected type, ordinary `ref place` does not root. |
| `ref *root` | Require a present nonoptional `gc ref<T>`; explicitly pin its backing for the bounded stable reborrow. No authority upgrade or optional pinned-result fallback. |
| `C(gcObject)` | Explicit whole-object pin conversion of the same nominal GC class, with no greater access. Copies share support through the last converted handle/dependent. |
| Ordinary reference formation and descriptor Copy | Preserve existing support and all independent dependencies; acquire no new root/pin. Readonly is not exclusive, deeply immutable or a snapshot. |
| `ref<C>` and `gc ref<C>` | Designate the selected handle **slot**, not automatically the object named by the handle stored there. |
| Native raw pointer projection | Separate explicit unsafe operation from a stable checked reference. Pointer bits do not root, pin, retain an owner or extend any endpoint. |
| Resource/guard/lease, publication, code and affinity | Keep their actual obligations independently. GC reachability and address stability discharge none of them. |
| Initialization | No outer `gc ref<init T>`, implicit root/pin for init references, root of vacant/partial T, or borrowable `void` payload. An independently live child retains its ordinary eligibility. |

These are accepted-source fragments, not currently executable examples.
This proposal adds no `pin ref<T>` type, `gc ptr<T>` alias, automatic
marshalling, inferred source contract or implicit native-boundary pin.

## Evidence establishing the missing contract

These are specification and source observations, not new test results.

| Owner/evidence | Existing fact and missing boundary |
|---|---|
| [Logical dependency records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) | Explicitly leaves retention/root/pin/cleanup carriers, capability registration and target layouts unassigned. Its accepted exact-place, whole-object pin and GC Cleanup rules are the baseline here. |
| [Windows x64 data layout](../specs/targets/windows-x64-data-layout.md#1-scalar-storage-and-validity) | An eight-byte raw pointer does not assign stored ref/class/box representations. Null-as-absence likewise does not complete their layouts. |
| [Windows x64 call interface §5](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | One-pointer stable rootless call borrows are not stored-reference or general reference-result ABIs; managed root/pin/barrier support is separate. |
| [Instruction set](../specs/il-instruction-set.md#fields-elements-and-ranges), [clauses](../specs/representation/contract-clauses.md#6-dependency), [registry](../specs/representation/trrm-1.registry.json) | PlaceSelection, ordinary ReferenceFormation, register aliases and Dependency rows do not acquire a root/pin. The registry already assigns GC qualifier `02`; “GC encoding unassigned” prose must not allocate a duplicate qualifier. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | `PrimitiveType`/`ManagedClassTypes` describe bounded managed classes/nodes, writable/optional variants and native pointer carriers. `BindType` has no complete exact-place GC root/pin type and operation model. These prototype identities cannot supply the new carrier. |
| [`NativeManagedNodeRuntime.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedNodeRuntime.cs) | `PrepareManagedRoots`/`RegisterManagedRoots` describe frame slots and active flags. `EmitManagedAllocation` uses HeapAlloc; `EmitManagedCollector` marks descriptor-described object pointers and sweeps with HeapFree. It contains no relocation-update or PinCell protocol. A root frame here is not implementation of accepted `gc ref<T>`. |
| [`NativeManagedClassRuntime.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedClassRuntime.cs), [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), [`NativeJit.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs) | Class factories use that same allocation/root mechanism; emitter/JIT carry an image-private 24-byte managed-runtime area. Reusing it does not establish a shared runtime domain or independent managed ABI. |
| [`TrRmTypeSpecTableReader.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs) | Structurally recognizes assigned qualifier bits and complete-type forms, separately from executable type support. Type decoding alone is not runtime acquisition. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) and the [provenance proposal](reference-provenance-carriers.md#private-exchange-candidate-revision-93) | Current structured private arithmetic revision is 89; candidates 90–93 address outputs, NoThrow retirement and declaration relationships. None encodes general GC reference types/operations or runtime cells. |
| [`NativeManagedNodeTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeManagedNodeTests.cs), [`NativeManagedClassVerifierTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeManagedClassVerifierTests.cs) | Existing tests exercise bounded tracing, copied handles, layout/factory verification and `native-18/19`, not moving exact-place roots or explicit pin retirement. The test project has no Compile exclusions; that does not turn these different assertions into root/pin evidence. |

### Minimal amendment versus redesign

**Selected:** stable opaque cells, fixed typed value envelopes, one effectful
Body family and an exact runtime interface. Runtime internals can change
without rewriting reference values; all modules in a domain agree on one
interface. Existing place/store/call operations and the provenance ledger
continue to own their existing semantics.

**Substantive alternative:** direct interior pointers plus precise stack,
register, field and suspended-state relocation maps at every safepoint.
This can avoid some indirections/cell allocations, but requires a different
stored/call representation, relocation patch protocol and complete map/ABI
contract across independently compiled code. It is not a small RootMap table
added to today's frame flags.

Making all GC storage permanently nonmoving, pinning at ordinary ref/native
call formation, using raw addresses as roots, or adding a universal owner
anchor to every view would not implement the selected distinctions. The
first can remain a private implementation strategy only where it preserves
all specified observations; it cannot establish relocation qualification.
No unpublished compatibility obligation requires retaining the prototype's
32-byte object header, single-pointer class ABI or image-local collector.

## Windows x64 carrier and runtime interface

### Value layouts and identity

All sizes/alignments below are bytes; words are little-endian, eight-byte
aligned. These are **unapproved nonforeign target layouts**, not Blittable
foreign records or semantic-interchange row shapes.

| Complete value | Size/alignment | Fields |
|---|---|---|
| `gc C`, `gc box<T>` | 8/8 | Non-null `ObjectCell*` |
| `gc ref<T>` | 8/8 | Non-null `RootCell*` |
| Stable `ref<T>` | 16/8 | `u64 address` at 0; `SupportCell* support` at 8 |
| Stable class handle C, including whole-object pin results | 16/8 | `u64 objectPayloadAddress` at 0; `SupportCell* support` at 8 |
| Optional of one of these values | Same size/alignment as present value | Absence is the all-zero descriptor; for a two-word value both words must be zero. Presence never establishes access/backing by itself. |

The two-word reference layout is uniform for a complete `ref<T>` regardless
of source. Support is zero only for a checked externally bounded stable place
whose actual storage/owner/guard contract supplies the whole needed extent;
it is a PinCell for the pins selected here. A zero support word does not mean
independent lifetime. Stable C from this proposal has nonzero PinCell support.
Other normal shared support kinds are **not** assigned by this proposal and
cannot be guessed from the same two-word envelope.

Under TargetProfile `(2,1)`, every materialized value of nominal class `C`
uses the 16-byte stable-C layout, including signatures and fields that contain
that type; it cannot vary by value origin within one module. This proposal
admits such a value only when its retained origin is an explicit
`C(gcObject)` PinObject result or a compatible provider preserving that exact
Feature/profile contract. Ordinary non-GC `C` construction, its normal shared
support cell and `box<T>` backing remain unsupported; reject those producers
and uses rather than publishing a zero-support C whose object is not retained.
Profile `(1,1)` one-word C handles do not bind to Profile `(2,1)` values.

Changing readonly/write access changes neither size nor support identity.
It still changes the complete type and checked authority. A nonoptional
descriptor cannot use the zero representation. Zeroing unpublished helper
scratch is not publication of an optional, a default T or an initialized
source variable.

Cells and runtime type descriptors are stable opaque runtime allocations,
not pointers into caller frames, movable payloads or private module tables.
**Opacity is the selected ABI:** consumers call the interface below and may
not read cell headers. Their internal byte layouts are runtime-private, not
missing public record assignments. One admitted domain interns loaded type
identities by complete Module/definition/substitution, preserving exact
allocation and cleanup/code origins. Equal row numbers, type names,
addresses, runtime DLL names or layouts do not establish identity.

The runtime maintains these exact logical contents:

* **ObjectCell:** domain and nonreused live allocation identity/generation;
  current payload base, extent/alignment, loaded complete payload layout,
  publication/retirement state, descriptor/support-edge holds and active-pin
  count.
* **RootCell:** ObjectCell identity, captured relative offset/extent,
  complete T, known actual place alignment and acquired access ceiling;
  descriptor/support-edge holds. It names the place, not a snapshot of T.
* **PinCell:** ObjectCell, exact-place RootCell or whole-object selection,
  captured stable address/extent/alignment and holds; exactly one committed
  pin-count contribution while this cell is active.
* **Loaded type descriptor:** complete value/payload layout, typed field and
  optional-presence traversal, support-bearing fields, selected authored and
  derived retirement, allocator/runtime and code/load origins. Unknown
  representations are not leaf bits merely because their size is known.

Allocation headers need not precede payloads. The runtime is given the actual
payload base and completed typed layout by its admitted allocation/provider
boundary; this proposal supplies no guessed header subtraction. Class/box
payload layouts, construction, inheritance or generic/witness adaptations
must independently be admitted. A provider cannot register arbitrary bytes
as an initialized GC object. A zero-data void field has no exposable place;
zero-extent nominal place support remains gated rather than being assigned a
neighbor's byte. A GC object can still have identity storage even when it
contains no addressable payload field.

### Exact helper ABI

Candidate logical runtime ABI is `windows-x64-root-pin/1`, represented for
target layout observations by **TargetProfile `(2,1)`**. It uses the existing
Windows x64 platform calling convention/preservation/unwind rules, with
explicit parameters and no hidden managed-context argument. It supplements,
not aliases or silently widens, TargetProfile `(1,1)`'s fixed rootless scope.
Profile `(2,1)` is a complete Windows x64 profile: all common scalar, field,
call, primitive-layout and unwind rules are identical to `(1,1)` except for
the explicitly listed nonforeign GC/reference layouts and runtime support.
Modules cannot combine the two profiles' representations for one complete
type or bind their differing C handle layouts by name or size.

The loader supplies one non-null `Domain* D` and an immutable **104-byte**
function table: `u32 abiRevision=1, u32 byteSize=104`, followed by twelve
eight-byte code pointers at these offsets. Signatures below are **machine
interface notation, not Terrum source declarations or registered library APIs**.

| Offset | Entry and exact machine signature |
|---:|---|
| 8 | `u32 RootExact(D, ObjectCell*, PlaceRequest*, RootCell** vacantOut)` |
| 16 | `u32 PinExact(D, RootCell*, StableValue16* vacantOut)` |
| 24 | `u32 PinObject(D, ObjectCell*, StableValue16* vacantOut)` |
| 32 | `u64 ResolveExact(D, RootCell*)` |
| 40 | `void RetainSupport(D, LoadedType*, const Value*)` |
| 48 | `void ReleaseSupport(D, LoadedType*, const Value*)` |
| 56 | `void EnterFrame(D, SupportFrame*)` |
| 64 | `void ExitFrame(D, SupportFrame*)` |
| 72 | `void Poll(D)` |
| 80 | `void BeginCall(D, CallTransfer*)` |
| 88 | `void ReturnSupport(D, SupportSlot* savedResult)` |
| 96 | `void FinishCall(D, CallTransfer*, u32 completion)` |

Pointers and D occupy ordinary integer positions. Status returns use the
low 32 bits of RAX; ResolveExact returns its address in RAX. `StableValue16`
has exactly the relevant two-word value layout above. `PlaceRequest` is
40 bytes/alignment 8: `u64 offset, u64 extent, u64 knownAlignment,
LoadedType* exactT, u32 accessBits, u32 reserved=0`.
Access bits are exactly `1=read` or `3=read/write`; other values reject.
The verified typed acquisition supplies this ceiling, which is retained with
the RootCell; requesting stronger access than the place is a producer
violation, not a runtime permission lookup by address. Readonly projection
still restricts each use even when a shared cell has a greater ceiling.

Acquisition status is exactly `0=committed success` or
`1=resource exhaustion with no published result`. Unknown statuses, foreign
domain/cell/type arguments, invalid cell state, impossible count transitions
or inconsistent runtime layout are runtime-contract violations, not optional
absence or OutOfMemoryException. A conforming provider does not expose native
unwind from these entries. Runtime corruption/host faults outside the modeled
failure contract are terminal; they cannot be reported as successful support.

### Candidate provider discovery and domain sharing

TargetProfile `(2,1)` resolves exactly one **candidate provider** export
`terrum-root-pin-runtime.dll!TerrumRootPinGetRuntimeV1`, imported as a
compiler-generated runtime function by affected AOT images and resolved to
the same export by JIT/NativeJit. It has the Windows x64 `__cdecl` machine
signature `const RootPinRuntimeInfoV1* TerrumRootPinGetRuntimeV1(void)`,
does not throw or safepoint, and returns one immutable, naturally aligned
**112-byte**
`RootPinRuntimeInfoV1`:

```text
u64 domain
u32 functionTable.abiRevision=1
u32 functionTable.byteSize=104
u64 functionTable.functionPointer[12] // exact order and signatures above
```

The 104-byte function table begins at offset 8 in this structure, so its
function offsets in the preceding table are relative to that subobject. The
nonzero `domain` is the process-wide singleton for this provider instance;
the export and all function targets remain loaded while any admitted code,
cell, object or Cleanup can reach them. Every module using Profile `(2,1)`
must resolve to the same provider identity/domain. A module cannot substitute
an image-private collector, a second same-version runtime, NativeImport
declarations chosen by source, or an equal-shaped function table. JIT and AOT
perform the same identity, revision, extent, non-null-entry, target and domain
checks before affected code becomes callable. Unsupported/mismatched provider
state rejects before any root/pin acquisition; it is not an absent optional
runtime feature.

The provider is an internal target-runtime dependency selected by the
profile, not an authored NativeImport API, TargetRequirement, ordinary
dependency provider, or serialized trusted authorization row. Its exact
library/export identity is a candidate assignment requiring the owning
Windows x64 deployment/runtime chapters to adopt together with Profile
`(2,1)`. The profile's 16-byte C layout also requires Feature 14 for every
materialized C value, even when a particular function does not execute F0.

RootExact/PinExact/PinObject may allocate runtime bookkeeping, coordinate with
collection and fail. The other entries allocate no bookkeeping, suspend no
source operation and raise no ordinary source failure for valid inputs.
Poll may rendezvous/collect; ResolveExact, retain/release, call handoff and
frame commit sections do not safepoint or invoke authored code. Frame and call
handoff storage/slots are prepared before registration, not allocated by the
entries. `completion` is exactly `0=abandoned/exceptional` or `1=normal`.

The runtime loads type descriptions from the admitted complete layout and
retirement contracts; it does not infer them by invoking source attributes,
getters, cleanup or a same-named method. Registering an allocation and
publishing its initial GC handle remain the already selected allocation/
construction boundary's responsibility. An implementation lacking that
boundary can test host-provided registered objects, but cannot claim general
source GC construction support.

The exact domain/interface, layout/type/cleanup providers and helper code are
admitted and retained before any affected initializer or execution. A runtime
table is target binding data, not a NativeImport declaration, producer-authored
accepted flag or permission to bypass Module admission. Missing/mismatched
support declines before use, with no private collector or CLR fallback.

### Roots, cells, copies and collection

Descriptor/support holds manage cell lifetime and prompt unpinning.
**A nonzero hold count is not by itself a collector root.** The graph has:

1. External roots in live registered activation/staging/result residences and
   explicitly registered static/runtime holders.
2. Typed descriptor fields reached while tracing a live GC object.
3. RootCell-to-ObjectCell and PinCell-to-root/object support edges.

Cells are traced support nodes. An unreachable GC object containing a
RootCell/PinCell descriptor does not make itself globally rooted by that
field's hold count. Trace support cycles together with object cycles; after
establishing unreachability, retire unreachable field holds/cells under the
cleanup rules before reclaiming their backing. Do not put every cell or
every pinned allocation into an unconditional root list. No collection
guarantee is added for mixed normal-shared/GC strong cycles.

Cell hold/pin transitions and root-slot visibility linearize with the
collector's domain coordination. Relocation cannot race a pin commit or
retirement; concurrent admitted holders cannot lose a retain/release.
Rendezvous must not wait for a mutator while holding a lock that prevents
that mutator from reaching its safepoint. These internal synchronization
guarantees do not grant source sharing, handoff or publication authority.

ObjectCells remain valid through their allocation's queued cleanup and
reclamation, not merely through the last external GC handle's retirement.
Static/runtime root registration outside activation frames requires its
independently admitted holder/publication/unload boundary; neither an unknown
static-field codec nor this function table implicitly registers one.

A source Copy creates another descriptor holder, retains the **same** cell
and carries the same exact referent and dependencies. It neither allocates
a new root/pin nor increments ObjectCell.pinCount for a PinCell copy.
Register `mov` merely aliases one logical value and creates no additional
holder. Transfer changes the responsible residence without retain/release;
all aliases of a consumed identity become unavailable. Typed field reads,
stores, calls, optional wrapping and supported aggregate Copy apply those
same distinctions recursively.

Retain must be nonfailing for an admitted live Copy. Counts use checked u64
arithmetic and cannot wrap or saturate. Each independently retired hold has
an actual descriptor/support-edge residence of at least one native word;
register aliases do not add holds. The number of simultaneously representable
residences/allocated support edges is therefore below u64 capacity in this
address space. Implementations with a smaller counter/table capacity must
establish the required capacity before admitting the operation/domain; they
cannot turn a later Copy into hidden root/pin acquisition failure.

Traceable fields need their complete loaded type layout and typed support
updates. Descriptor store retains the new hold before retiring the old one
and commits the destination under its ordinary confinement/initialization
rules. Full stop-the-world tracing needs no concurrent/generational write
barrier, but **does** need this typed hold accounting and correct publication.
A raw pointer store is not such a barrier or a valid way to install a root.
Unsupported native/external memory containing managed descriptors requires a
separate registered scanning/lifetime protocol; no address-to-owner registry
or conservative stack scan supplies it automatically.

### Relocation and exact-place access

The selected runtime coordinates attached mutators at cooperative safepoints:
managed call transitions, allocation/acquisition boundaries and loop
backedges. Entry attaches to the admitted domain before publishing roots.
Foreign transitions park the mutator only after its live roots and stable
pin dependencies are visible. Callback entry must attach/resume the same
admitted domain under its own supported boundary.

At a stop, the collector traces the complete root/object/support graph.
It may relocate a reachable allocation only while its committed active-pin
count is zero. Relocation preserves object and selected-place generations:
copy the complete admitted payload as a **relocation**, transfer its existing
field/support residences without another logical Copy, update
ObjectCell.currentBase before resuming mutators, and retire the old physical
storage. Cell pointers and RootCell relative selections remain unchanged.
Failure to obtain destination storage leaves the old allocation valid; it
does not publish half a relocation or clear an existing root.

Reading/writing `*root` uses its current initialized place, including permitted
later Copy assignments. It does not pin. ResolveExact obtains the current
address only for the following bounded typed memory access in a **no-safepoint,
no-callback interval**. The address cannot survive a call, poll, branch into a
potential safepoint or staged user operation. A larger typed load/store must
use a supported no-safepoint bit-transfer or an appropriate rooted staging
algorithm; it cannot keep a stale address while copying support-bearing fields.
Compute failure-prone RHS work before the final resolution/store.

Place formation captures object identity and evaluated field/index selection
once. Later relocation changes the physical base, not that selection; later
rebinding of the original receiver does not redirect it. Follow a separately
stored handle only when the source operation actually reads that handle.
No ordinary write to a still-live Copy place creates a new storage generation.
Retirement/reuse of the designated place is forbidden while a live root or
dependent still requires that generation.

A root may physically keep the whole containing object traceable to preserve
its complete cleanup receiver. That is retention, not sibling-field access.
Pinning likewise immobilizes the whole containing allocation, the selected
runtime's movement unit, without widening an exact-place reference.
Bounds/offset arithmetic uses checked nuint and `extent <= payloadSize-offset`;
indexes are checked before narrowing/address calculation. Preserve actual
alignment, including admitted packed-place access. Pinning never improves
alignment; a projected native typed pointer additionally needs the required
alignment proof. Unsupported unaligned typed accesses decline rather than
silently using aligned instructions.

## Acquisition, publication, retirement and errors

### Exact-place root acquisition

1. Evaluate/capture the receiver, selectors, checks and complete initialized
   place once at the source formation point. The captured moving place has a
   live supporting GC handle/root; a bare address is ineligible.
2. Prepare a vacant result residence and the exact PlaceRequest. RootExact
   validates the selected domain, published allocation, numeric bounds and
   layout compatibility needed by the runtime. Source initialization/access,
   independent owner/guard and declared-source truth remain producer facts;
   numeric bounds alone cannot prove them.
3. Acquire a fresh RootCell/support edge and make its result residence visible
   to tracing before any safepoint or publication to another holder. The
   helper's final commit/return and the caller's live-slot installation form
   one no-safepoint interval; no collector can observe an unrooted successful
   return in between.
4. Publish the non-null `gc ref<T>` only after all required support is
   committed. An expected readonly type may attenuate available access, not
   upgrade it. Failure publishes no descriptor and leaves any previous
   destination value intact under ordinary replacement rules.

The RootCell does not depend on the original handle variable's slot. Ending
that variable can leave the exact-place root valid. Rooting an inline field
instead of its neighboring field, or a handle slot instead of its referenced
object, remains a materially different selection.

### Exact-place pin and whole-object pin

PinExact takes one live, unwrapped RootCell and preserves its full dependencies.
PinObject takes a live GC class ObjectCell under the selected exact nominal
conversion. Both:

1. Prepare vacant result storage; allocate the PinCell and acquire its
   supporting root/object edge while the source is still rooted.
2. Under the same domain coordination that excludes concurrent relocation,
   resolve the current base, establish the exact/whole selection and increment
   the object's active-pin count **once for this acquisition**.
3. Publish `(base+offset, PinCell*)`, or `(base, PinCell*)` for C, into the
   already prepared traced residence. There is no fallible allocation between
   the committed pin transition and publication.

Each reached explicit pin is an acquisition; separate pins may share an object
but have distinct PinCells. A Copy shares its existing PinCell. Retiring the
root descriptor before a surviving pinned reference is allowed when its other
dependencies permit: the PinCell retains the necessary root **support**, not
the descriptor's old local slot. A reference to that local slot is different.
The bounded pin ends only after every copied descriptor and dependent that
requires it ends; neither a private last-use guess nor leaving a native call
while a declared retention remains ends it.

Whole-object conversion does not acquire native resource ownership or turn
GC Cleanup into final-normal-handle cleanup. It preserves the original
allocation/cleanup policy and nominal/code origin. Copying the stable C
result shares the same pin. A field reference formed through it carries the
same pin support, even after that particular C descriptor retires.
No general `gc box<T>` to `box<T>` conversion or class-to-pointer conversion
is added.

### Failure and partial acquisition

On resource exhaustion, unwind private acquisitions in reverse acquisition
order: remove an uncommitted/committed unpublished pin-count contribution if
present, drop its supporting cell edge, unregister a prospective result and
free its private bookkeeping. Existing source roots/pins and destination
values remain live. Rollback performs no T Cleanup, cannot fail for valid
state and completes **before** returning status 1.

Lowering then follows the existing
[recoverable allocation contract](../specs/runtime/standard-failures.md#4-recoverable-allocation-exhaustion):
an admitted recoverable path creates/propagates the exact fresh Core
OutOfMemoryException at the root/pin operation, with complete preparation,
transport, roots, cleanup and surviving code resources. Actual event
preparation failure follows its own admitted outcome. A deliberately selected
terminal policy terminates after the partial-acquisition rollback and
promises no general language cleanup after termination. Lack of recoverable
support is a before-use gate, not a runtime switch to terminal handling.

No failure returns absent optional support, a null nonoptional descriptor,
false Boolean success, a default T, an ordinary reference to moving storage,
or a pinless retry. Initialization/access/index evaluation failure remains
its actual earlier failure. Runtime-invalid cell/domain/layout state is not
classified as resource exhaustion.

### Endpoints, ordering and object cleanup

Ending a descriptor removes exactly its hold/residence. Ending one alias does
not destroy T, retire the object, revoke another copy or release an explicit
owner/lease/guard. On the final PinCell hold, coordinate with relocation,
decrement the object's pin count, invalidate that PinCell's stable address,
then release its root/object support edge and bookkeeping. Other committed
pins still exclude movement. A RootCell is reclaimed only after its descriptor
and supporting PinCell edges end; cell address reuse never revives an old
logical allocation/place identity.

Normal local/field/parameter/result retirement uses actual holder state and
the existing dependency-valid source order. Release a pin only after all
uses and any cleanup/finally code requiring its stable address. Failed
unpublished construction releases acquired support in reverse acquisition
order. Staged arguments remain caller-held until Invoke; saved results and
published output/retained fields keep their own holders, including after
later failure. Rebinding cannot prematurely release support required by a
reference to the descriptor slot or by another retained dependent.

Runtime support release is nonthrowing/nonsuspending and invokes no authored
Cleanup. It occurs when that support's actual extent ends on normal or
admitted exceptional control flow. It is **not** permission to synthesize
user-resource Cleanup on unwind, change field order or discard retained
object/capture state. Crossed authored finally blocks execute with their
needed support live; release only the residences whose scope actually exits.
A handler inside the same activation must not cause an entire-frame release.
An unimplemented ExceptionRegion/event/cleanup-routing carrier still gates
source-free catch/region execution.

GC object Cleanup follows
[its accepted backing policy](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement):
once after collector-established unreachability, before reclamation,
nondeterministically on an unspecified cleanup thread. Queue/retain the
complete object, its fields and cleanup/code dependencies until retirement.
The cleanup receiver cannot resurrect/republish the object. Run the authored
body once, then live fields in reverse declaration order on normal completion;
field-held roots/pins release as their own descriptors, not as referent
destruction. A thrown Cleanup does not resume remaining generated field
cleanup or authorize freeing its outstanding obligations. In this candidate
runtime the uncaught cleanup-worker event terminates under ordinary unhandled
failure rules. No affinity-preserving cleanup scheduler is introduced;
transitive obligations must be thread-neutral or safely transferable.

Unreachable support cycles with Cleanup are kept intact until their applicable
object cleanup order is valid. A protocol requiring a currently unsupported
inter-object cleanup/guard schedule rejects admission; a sweep cannot invent
a topological order, release a live dependent early or suppress Cleanup.

## Compiler, Body and target integration

### Source admission and common bound representation

The binder distinguishes ordinary stable formation, exact root acquisition,
relocating referent selection, explicit exact pin, whole-class pin and unsafe
raw projection after complete type/alias/substitution resolution. An expected
GC root selects acquisition only for a fresh eligible formation, not a cast
of an existing ordinary reference or raw pointer.

Use typed bound operations carrying their already evaluated place/value,
complete source/result types, access/alignment, selected runtime contract and
normal/failure publication effects. Shared visitors, cloning, substitution,
rewriting, mandatory producer verification and lowering must all preserve
them. Do not encode pinning as an ordinary cast, hide it in DD/67 or infer it
from a class/sample/API name.

Apply the prerequisite provenance ledger without serializing its keys:
Root acquisition creates the corresponding actual Root capability; explicit
pin creates a Pin capability linked to that root/object; Copy shares the
capability; transfer changes residence; retirement discharges only that
holder's extent. Keep own selected storage versus forwarded backing,
independent owner/guard/lease, publication, code/load and affinity separate.
Initialization revisions, result/publication epochs and storage generations
must not be collapsed. Joins and loops preserve all guarded alternatives;
unavailable facts never become Known empty.

Verification checks that every support-bearing computed value has a
responsible residence through calls, failures and joins. A borrowed operation
does not manufacture one. An acquisition's register rebinding/discard cannot
lose an unretired hold; store/transfer or explicitly end it. Broader source
dependency permissions cannot be narrowed by examining today's runtime cell.

### Candidate typed Body program

All assignments in this subsection are **unapproved**. Keep instruction
encoding `01`, existing maps/index widths, BlockEntryState carrier kinds and
all ordinary record schemas. Add the four-byte long family:

```text
F0 action:u8 referenceSupportRecordIndex:u16
```

The index is little-endian, zero-based in this Body's typed-record namespace,
not a root identity, TypeSpec row or native address. Add one **16-byte**
`ReferenceSupport` record:

```text
u8 recordKind=0D, u8 flags=0, u16 reserved=0,
u8 sourceKind, u8 resultCarrier, u16 sourceIndex,
u8 resultRegister, u8 sourceMode, u16 sourceTypeMapIndex,
u16 resultTypeMapIndex, u16 reserved=0
```

`sourceKind` is `1=Register` or `2=Home`. A register source is R0..R13
with no high index bits. `resultCarrier` is `0=None`, `1=Value`, or
`2=PlaceHandle`; non-None results use R0..R13. None requires
`resultRegister=15, resultTypeMapIndex=0`, with the latter unused rather
than a reference to TypeMap[0]. `sourceMode` has this record's closed domain
`1=borrow existing value/place`, `2=create independent Copy holder`,
`3=consume one holder`; these are **not** CallSite source-use numbers.
Both TypeMap indexes otherwise designate exact complete types; for a
PlaceHandle they designate its referent T.

| F0 action | Source → result and exact mode |
|---:|---|
| `01 RootExact` | Internal GC-backed initialized PlaceHandle T → Value `gc ref<T>`; borrow mode. Source must be a Register place, not a Home holding a descriptor. |
| `02 ReferentPlace` | Nonoptional `gc ref<T>` or stable `ref<T>` → PlaceHandle T; borrow mode. Preserve relocating versus stable backing and exact access; acquire no support. |
| `03 PinExact` | Nonoptional `gc ref<T>` → Value stable `ref<T>`; borrow mode, explicit acquisition. Preserve outer write authority or attenuate it. |
| `04 PinObject` | Nonoptional `gc C` → Value stable C of the same admitted nominal class; borrow mode, explicit acquisition and no access upgrade. |
| `05 CopySupport` | One eligible supported descriptor → independent Value holder of the identical complete type; Copy mode. Retain existing support, never reacquire. |
| `06 EndSupport` | One supported descriptor holder → None; consume mode. A Home becomes vacant; all register aliases of the consumed identity become unavailable. Does not end T or an independently retained copy. |
| `07 ProjectPointer` | Stable `ref<T>` → Value `ptr<T>` with no greater write authority; borrow mode and explicit unsafe authorization. Preserve required backing/alignment/provenance. |

Action `00` and `08..FF`, unknown record modes/carriers and nonzero reserved
fields reject. Results cannot use discard R15 or contextual R14. Copy/End
also apply to admitted optional descriptors after their presence state is
accounted for: None has no support hold; Pin/ReferentPlace still require
explicitly established presence. PinObject does not apply to boxes,
interfaces or arbitrary equal-sized handles.

Existing PlaceSelection captures fields/elements in admitted GC backing as
relocating **internal places**, with its evaluated base and selectors.
Its required receiver/base value stays live through the place's borrowed
uses. F0/02 supplies a direct descriptor referent place without treating the
descriptor's Home as the referent. Existing typed reads/stores/takes and
retirement operate on those places using current resolution as above.
PlaceSelection alone acquires neither a root nor a pin.

DD/67 and compact field-reference kind 04 remain ordinary stable-reference
formation, with no GC root acquisition or implicit pin. They can retain a
PinCell already supporting their selected stable place. Existing loads,
stores, field Copy and call parameter/result binding retain/transfer support
as their declared modes require; F0/05 is needed to express an independent
computed descriptor Copy where `mov` would only create an alias. Existing
place retirement can end a stored descriptor; F0/06 additionally handles a
computed holder without fabricating a temporary T place.

Pin/root failures have no normally published result. Body register/Homes and
BlockEntryState carry complete types; operation/incoming-edge reconstruction
carries actual Root/Pin identities and failure states. There is no new dynamic
instance-ID table or serialized ledger. Source-free exception handling remains
conditional on its separate complete event/region representation; F0 does
not activate B6/EA region exits.

As candidate **record/instruction fragments**, for source place R1 of
TypeMap[2] and root result R0 of TypeMap[3]:

```text
ReferenceSupport record 0:
0D 00 00 00  01 01 01 00  00 01 02 00  03 00 00 00
Acquire that exact root:
F0 01 00 00
```

The fragments need their owning Body maps, selected moving place and complete
artifact admission. They are not a currently accepted/executable byte vector.

### Frame support, JIT and AOT lowering

Generated code registers a SupportFrame before a value can reach a safepoint.
Its candidate target ABI is a 32-byte/alignment-8 header:
`SupportFrame* previous, u32 slotCount, u32 flags=0,
SupportSlot* slots, LoadedMethod* entryMethod`.
Each 24-byte SupportSlot is
`Value* residence, LoadedType* completeType, u32 live, u32 ownsHold`;
the last two fields are exactly 0/1.
LoadedMethod is an opaque admitted method identity containing its complete
signature/binding and formal/receiver-to-entry-slot map. It is derived from
the selected signature and generated frame, not a serialized source grant
or a pointer equality substitute for declaration binding.

The frame/slots occupy stable activation storage. Their addresses and total
extents are checked before use. A live slot names an initialized typed
residence, possibly optional None. Only one slot is responsible for releasing
any one owned logical hold; extra borrowed/alias scan residences use
`ownsHold=0`. Register aliases are not counted as independent holders.
Moving-object fields are scanned from their loaded object layouts, never
registered as frame addresses into payloads that may relocate.

Stores/transfer/EndSupport update residence, live and responsibility together
inside the no-safepoint commit interval. A value saved only in a volatile
machine register at a safepoint is not rooted. Spill/root staging, hidden
results, later-argument calls and exceptional return snapshots are included.
Normal retirement clears a slot after releasing/transferring its hold.
ExitFrame visits slots in reverse array order, releases each still-live owned
hold once and clears it, skips borrowed/inactive slots, then unregisters the
frame. Generated actual scope endpoints must already have preserved the
dependency-valid source cleanup order; this final support-only drain cannot
invoke authored code or collection and does not replace that order.
It does not invoke source Cleanup. Native unwind invokes it only after
applicable finally/inner-scope support endpoints; same-frame handlers need
their per-scope release paths, not ExitFrame.

These frames are generated target root/unwind data, not another standard Body
record. The typed Body operations already determine the needed support
residences and endpoints. AOT emits the frames, exact helper bindings,
loaded type/cleanup data and applicable Windows unwind support. JIT creates
the same structures, resolves/retains providers, publishes code and registers
unwind information before execution. Both use one admitted D for the selected
closure; neither silently copies an image-local collector into each module.
Failure to register/publish/unregister code has the existing no-use/no-free
outcomes; live cell/type/cleanup dependencies keep the responsible provider
loaded even when no direct machine import remains.

### Calls, results and raw native projection

Within this nonforeign subprofile, an eight-byte GC descriptor uses the
ordinary integer carrier; a 16-byte stable ref/C value uses the ordinary
indirect aggregate input and hidden-result rules. Argument staging owns the
required hold for by-value Copy; borrowed receiver/input paths keep their
supporting holder live. Hidden result storage is an actual traced residence,
not an implicit loan, root acquisition or extra source formal.

A GC class instance receiver carries its ObjectCell word. A stable C instance
receiver is a pointer to the caller-staged two-word handle descriptor, borrowed
for the invocation, **not** a pointer guessed to be the object header.
The callee obtains its payload address from that stable handle. Hidden result,
receiver and explicit arguments retain the existing nonforeign C-style
physical order; these new forms do not change rootless unique receivers or
the distinct native-member `this` ABI. All source evaluations remain once
and in source order. Callee normal return transfers its saved descriptor hold
to the caller result; exceptional completion publishes none.

For nonforeign support-bearing value calls, use an explicit **target-internal
call-hold handoff**, so partial prologs, results and unwind cannot lose a hold
or silently extend a pin through an extra caller retain. No extra machine
argument, Body record or CallSite source-use is introduced.
CallTransfer is 40 bytes/alignment 8:
`CallTransfer* previous, LoadedMethod* target, SupportSlot* prospectiveResult,
u32 inputCount, u32 state, InputTransfer* inputs`.
An InputTransfer is 16 bytes:
`SupportSlot* stagedSource, u32 formalOrdinal,
u8 sourceRole, u8 flags=0, u16 reserved=0`.
Role `0` names an explicit formal; role `1` names the receiver and requires
ordinal zero. Entries uniquely identify actual staged **owned** descriptor
holds to pass, in mapped formal order; borrowed inputs/receivers remain
caller-held and are not transferred. The result pointer is zero only for a
result without runtime support. A nonzero result points to vacant private
caller scratch of the exact result type, not a live user destination.

* After ordinary Check/Prepare and immediately at Invoke, BeginCall pushes
  this preallocated plan on D's per-thread pending-call stack. Initial state
  `0=vacant` becomes `1=prepared`; staged slots still own the holds. There is
  no safepoint or authored operation between this handoff and callee entry.
* The callee spills incoming values before any safepoint, then EnterFrame
  registers its frame and matches the prepared target/signature. Using the
  loaded entry-slot map, it transfers each input's one hold: mark the callee
  residence live/owned and the caller staging residence vacant/unowned.
  Counts do not change. Commit `2=entered` in that no-safepoint interval.
  A relevant compact body also needs this entry protocol; a tail call cannot
  bypass it without an equivalent proved handoff.
* A native fault before that commit leaves the caller's prepared holds
  as the responsible runtime residences; it does not restore source ownership
  or revive a moved source. After commit, callee parameter retirement/transfer
  and its admitted scope/frame unwind own them. Thus the callee can end its
  final parameter pin during its body; an invisible caller copy does not keep
  it pinned until return. Borrowed call-extent inputs remain bounded by their
  independently required caller hold.
* After all callable checks/cleanup, ReturnSupport moves the saved normal
  result hold into the prospective result slot, clears its old responsibility
  and sets `3=result prepared`. It performs no retain or new acquisition.
  This is still a private pending result through the machine epilog; RAX or
  hidden-buffer contents agree with those bits.
* FinishCall with normal completion requires the appropriate entered/result
  state, then makes that prospective result available for ordinary Publish.
  With exceptional/abandoned completion it releases still-prepared caller
  inputs and any prospective result; entered inputs instead follow callee
  cleanup. It clears those slots before a surrounding frame drain, marks
  `4=closed` and pops the plan. Both normal and admitted exceptional call
  paths execute it. A reused plan must be reset to vacant after settlement.

The stack is reentrant; a nested call pushes its own plan. Runtime helpers and
unrelated callback entries cannot consume another method's pending plan.
An admitted host invoking this nonforeign ABI uses the same protocol; a raw
native export is not such a host boundary. These are physical support-hold
transitions for the existing call phases, not new source ownership/capture
permissions or an alternative provenance ledger.

The existing one-pointer stable rootless call-extent reference projection
remains available where its contract applies. It passes only the exact stable
address and leaves support with the caller through invocation and cleanup.
It is not the stored/result two-word ABI, a pin acquisition, general reference
return or permission to capture the projected descriptor.

Foreign signatures still accept only their directly interoperable forms.
No RootCell, PinCell, gc C/ref, stable C or two-word checked descriptor becomes
foreign data by being pointer-shaped. F0/07 exposes only the address word
under explicit unsafe authorization and an independently valid ptr<T>
boundary. The pointer acquires **no** support hold. The program must keep the
actual pin/root/owner/guard residence valid for every permitted use or native
retention; do not synthesize a hidden retain to repair an escape.

A known dangling pointer may survive as inert unsafe data under the accepted
warning policy, not as proof that later dereference is valid. Native writes
must satisfy the selected T validity/access/init contract; raw writes cannot
install managed cell bits without typed support accounting. Rooting a wrapper
does not keep a disposed native resource/pool rental usable.

## Allocation, admission, exchange and binding

### Whole-namespace collision and capacity check

The inspected baseline is
[`trrm/1-draft-layout-queries-1`](../specs/representation/trrm-1.registry.json).
Numbers here are proposed **only** for this new capability; the registry is
not edited by this document.

| Namespace | Baseline, interaction and candidate |
|---|---|
| Instruction partitions | `00..0F` tiny, `10..BF` short, `C0..FF` four-byte long. No prefixes, escapes or variable widths. There are 39 assigned/recognized long primaries and 25 unassigned ones; aggregate DF consumes one of the latter. |
| Long family placement | C0..CC scalar/recipes; D0..DE conversions/storage/queries; aggregate DF completes that neighborhood. E0..EB contains calls/control and shape-only EA; E9 and EC..EF remain free but do not form a managed-support family. Select **F0** as a standalone long family, leaving F1..FF and the other gaps free. After DF and F0, 23 long primaries remain; no current short/tiny form changes. |
| F0 control capacity | Seven closed actions of 256; no overloaded register nibble, reserved control alias or recursive decoding. Future managed operations need an explicit schema amendment, not “any helper” dispatch. |
| Body records | Assigned `01..0B`; aggregate **0C** is OutputReservation. Select **0D** ReferenceSupport, leaving `0E..FF` unassigned. Record count/index capacity remains 65,536, with both 0 and FFFF usable; the kind does not change record-index widths. |
| Features | Registered native `1..7` and layout `13`; assigned/incomplete `9..12`; provisional aggregate **8/1** and bounded selector **10/1**. Select independent **14/1 RootPinRuntime**, not an alias or replacement for 8. Feature IDs/revisions are full u32, not bits or ordinal-dependent dependency edges. |
| Target profiles | Existing `(1,1)` Windows x64 rootless profile. Select **(2,1)** Windows x64 root/pin runtime/layout subprofile. Target requirement `1=Windows-x64-native` remains the platform selector; it is not profile ID 2. |
| Types and tokens | TypeSpec forms `01..0F`, qualifier bits `01=write,02=gc,04=init`; `08..80` remain reserved, including the retired pin bit. Reuse them exactly; no new GC/Pin type constructor. F0 instruction is unrelated to F0/F1 Body pseudo-type tokens and their separate retirement proposal, or F2/F3 complete TypeSpecRefs. |
| Contract kinds | Existing `0001..0005`, `000A..000C`; `0003` Transfer remains inactive, `000C` NativeCallable. No new kind, flag or payload. Root/pin are actual operations, not a static Dependency/Effects assertion. Static-initialization/predicate candidates do not compete for a new slot here. |
| Sections/definitions | Core section kinds through `004B`, SourceMap `7F00`, optional nonsemantic `8000..FFFF`; current DefId kinds `13/14/16/19/1E/21`. No new table/DefId; reserved holes/nonsemantic extensions cannot hide root programs. Global typed rows remain `1..00FF_FFFF` with reserved-zero high bytes. |
| CallSite/source-use | Existing 1..5 plus aggregate candidate **6**. No addition/reinterpretation; ReferenceSupport.sourceMode is a distinct local enum. |
| Private schemas | Current structured revision 89; candidate output/retirement/provenance revisions **90–93** and expression tag 41 stay unchanged. No 94 or native-container version is allocated here. |

The aggregate proposal's preference for 8 rather than an output alias at 14
is preserved. This independent later capability is why 14 is now a candidate.
The older logical Generic 6–8 candidates are not current reservations and
cannot displace the registered native family or provisional aggregate 8.
Numeric ordering has no semantic prerequisite meaning, and low fixed-width
IDs save no bytes. Choosing one complete family in the F band avoids
consuming the last D primary twice or scattering resource operations through
exception/control slots. A more extensive reallocation is possible before
release, but has no compensating capacity/codec benefit for these seven
operations.

The registry conformance case that rejects **F0 as a prefix** remains valid;
adoption must separately replace its unassigned-primary example because F0
would become a four-byte operation. Do not reinterpret old same-version
streams by plausible operands.

### Candidate Feature 14/1 registration

Register coherent `RootPinRuntime`, governing edition `1`, with **no
unconditional Feature prerequisites**. Its scope includes the exact typed
root/pin/Copy/end/projection program, GC/support graph and relocation
obligations, supported descriptor layouts/crossings, runtime/failure/cleanup
interface, binding and rewriting described here. This is not a claim of
general normal backing, reference erasure, GC construction or handler support.

Require 14/1 for every retained supported GC type, root/pin descriptor contract,
F0 use, corresponding supported field/result/call/cleanup or target-layout
dependency, including uncalled declarations/expected imports. A retained
unsupported normal backing or erased representation is still a gate; the
feature does not authorize silently treating it as one of the supported forms.
Exact target/runtime support includes `(2,1)`, compatible D, full loaded
layout/tracing/retirement, actual failure policy and required code lifetime.
Knowing feature/profile numbers is insufficient.

Conditional closure remains exact: native call/address/import/interface uses
add their existing Features 1–7 as applicable; actual FieldMap/FieldPath/query
or FieldDef binding uses add 13; retained frozen/predicate/evaluation uses
add their own complete registration. Merely lowering a runtime helper does
not create a source NativeImport or require native-output selectors.
The aggregate-output combination requires **8/1 and its 1/2/10 closure**,
plus 3 or 5 for its actual call role, **beside 14/1** when pin/root support is
retained. No 8, 10, 11, 12 or 7 prerequisite is inferred just from rooting.

Support includes the recoverable failure/event and exception-routing substrate
only where the selected operation/path requires them. The existing standard
runtime-policy value `0` does not acquire a new wire meaning here; source-free
local recovery remains gated until its complete separate records exist.
Terminal support and recoverable support must be advertised truthfully;
consumers cannot substitute one for the other.

Adoption requires coordinated catalog, manifest, opcode/record, target and
semantic-owner changes under one exact out-of-band draft schema agreement.
Format `(1,0)` remains a draft pair, not a compatibility selector. No new
header flag, alternate decoder, producer runtime pointer or feature-number
maximum selects the schema.

### Standard exchange and private persistence

Standard semantic exchange persists existing complete TypeSpecs, normalized
declaration contracts and the typed Body operations, **not live cells,
addresses, counts, ObjectCell generations, support frames or ledger caches**.
After loading/binding, runtime acquisitions execute anew at the represented
operations. Structural decoding never itself allocates a root or pins data.
Reconstruction cannot replace an unsupported F0 program with DD or a
Dependency row.

No private codec is needed to persist runtime handles: those handles must
never be persisted. The current private `.trm` structured type/body codec and
its native-package/set/graph embeddings cannot represent this new complete
program merely by adopting revisions 90–93. They must explicitly reject
retained unsupported root/pin types/operations before writing, restoring,
image load or execution, including declaration-only requirements. Version-2
TNPK/TNSP/TNGP wrappers and valid checksums do not bypass that gate.

The provisionally implementable persistence route here is standard typed
Body exchange. Source-to-JIT/AOT can use the same bound operations without a
private save. Standard semantics plus an explicitly admitted precompiled
image can use the target ABI after complete semantic/runtime/provider binding;
this proposal assigns no new private package wire format for that pairing.
Adding general reference type/body serialization to the private codec, if
actually required later, needs its own exact-schema delta rather than an
invented revision-94 prefix or serialization of the provenance ledger.
This gate is deliberate; root/pin support does not claim private-module
round trips or completion of the full adoption plan.

The explicitly selected development `.trrm` executable profile is also
separate. It has no Feature 14 or F0/0D root/pin codec and must reject retained
root/pin types, declarations and operations before affected execution. It may
not reinterpret the new operations as DD/reference formation, lower them to
the private revision-93 ledger, or select an older executable decoder.

### Producer, consumer, replacement and rewriting obligations

Producers establish initialized exact-place identity, access, truthful source
permissions, supporting lifetime, no-safepoint access, balanced actual holders,
correct partial acquisition/publication and cleanup order. Consumer
obligations follow
[reader trust](../specs/representation/reader-trust.md), not a new requirement
to re-prove arbitrary producer body or foreign semantic truth.

Consumers safely decode and check exact schema, known feature/profile/runtime,
record extents/counts/flags, opcode/action-record agreement, selector namespaces
and bounds, complete source/result types, available representation and
binding/target agreement before affected use. They reconstruct the
operation-derived facts required by lowering; optional deeper flow/alias/
initialization or provider-truth audits remain separately scoped.
An unsupported/missing required carrier rejects; a semantically false but
structurally consumable producer fact remains a producer violation, not a
promise of detection.

Binding compares complete type/authority and normalized source/capture
relationships under the prerequisite's directed rules, then the exact
runtime domain/interface, layout, allocation/cleanup and code origins needed
at the crossing. A nonforeign GC/reference signature cannot bind a raw
pointer signature, image-private prototype class ABI, different runtime
domain or rootless profile by equal width. Preserve every original expected
declaration and any stronger fact actually relied upon. Whole-object pin
results retain the defining object/provider, not an intermediate forwarding
image's allocator. Support code/type/unwind providers cannot unload while
live objects, roots, pins, calls or deferred Cleanup need them.

Re-encoding/remapping visits complete TypeSpecs/TypeMap, Home identities,
FieldMap/FieldPath and MethodMap/bindings, Dependency rows, all
ReferenceSupport source/type/record indexes, register/Block entry flow,
ordinary store/retirement/call records and result destinations. Rebuild CFG,
instruction/record offsets, maps and SourceMap step associations; acquisition,
failure, pin and end are distinct effectful steps, not movable presentation
noise. Inlining/cloning creates new operation occurrences and reconstructs
the ledger, not duplicate persisted runtime identities. Regenerate native
frames/type/helper bindings when inputs change. No opcode-octet substitution,
stale root map or row-number equality is an acceptable rewrite.

## Integration owners and conformance gates

On adoption, make these changes together in the owning documents/code; this
proposal does not edit them:

| Owner | Exact integration effect |
|---|---|
| [Reference formation](../specs/language/references.md#formation), [lifetimes](../specs/lifetimes.md), [native boundaries](../specs/native.md) | Preserve accepted syntax/authority and link the now-selected physical root/pin protocol; distinguish stable C representation from original allocation/cleanup policy. |
| [Logical records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) | Replace only root/pin carrier/target “unassigned” claims covered here. Correct stale claims about GC qualifier encoding; retain normal shared backing, unsupported capture/erasure/handler/construction gates. |
| [Instruction set](../specs/il-instruction-set.md#typed-operand-records), [registry](../specs/representation/trrm-1.registry.json), [manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Add exactly F0, record 0D, Feature 14/1, TargetProfile 2/1 and the conditional closure/validation rules; preserve DF/0C/8/10 and ordinary operation domains. No new Contract applicability. |
| [Windows layout](../specs/targets/windows-x64-data-layout.md), [calls](../specs/targets/windows-x64-call-interface.md), [deployment](../specs/targets/windows-x64-deployment.md) | Add the bounded value envelopes, opaque runtime/helper and frame ABI, nonforeign crossings, safepoints and shared-domain/code lifetime; do not widen foreign admission. |
| Binder/types, common bound traversal/rewriting, verifier and native emitter/JIT | Implement typed acquisition/selection/pin/Copy/end, graph/holder and failure states, target resolution and real relocation/exclusion; replace rather than rename prototype-specific policy. |
| Standard readers/writers, binding/replacement and private/module/native paths | Decode/remap the complete selected program; retain before-use gates on unsupported private schemas, domains, profiles and independently required representations. |

All non-Windows-x64 targets remain gated until their own complete layouts,
call/frame/runtime/relocation/cleanup and failure contracts are selected and
implemented. Portable metadata inspection does not confer execution support.

Integration order is contractual, not a private-version ladder:

1. Select the combined exact schema and target/runtime contract, including
   the unchanged aggregate/provenance candidates; retain all unsupported
   route gates. Implement and admit the loaded object/type/domain boundary.
2. Carry the typed operations and common ledger through source/public bound
   programs, verification and native lowering. Establish actual root, pin,
   failure and cleanup behavior without requiring private serialization.
3. Integrate standard types, F0/0D, manifests, binding and remapping together;
   qualify independently supplied semantic inputs and shared-domain JIT/AOT
   images. Existing DF/0C/8/10 support is a prerequisite only for combinations
   that actually retain the aggregate-output capability.
4. Enable each supported stored/result/crossing route only with its full
   prerequisite/evidence closure. Private saved modules and alternate targets
   remain rejected; no completion of this slice removes those gates.

The following are required cases, not claimed test results. Use unrelated
synthetic types/APIs rather than the motivating sample or prototype names.

| Family | Cases and required outcome |
|---|---|
| Positive exact roots | Root distinct scalar, nested inline, array-element and handle-slot places in admitted GC storage; end the original handle local; move the object repeatedly and observe the current selected place. Permitted Copy assignment remains visible without changing its generation. An exact handle-slot root does not follow a later replacement to some old object. |
| Positive explicit pins | Resolve a relocated root, pin, mutate/read through the stable reference, Copy into a second holder/field and retire the first. No movement while any required pin dependent survives; movement becomes permissible after the final hold. Separate pins of the same object have independent acquisitions/count contributions. |
| Whole-object conversion | Convert gc C to C with matching readonly/write authority, Copy/return/capture it under admitted contracts, form a field reference and end the source gc/local converted handle. Identity/code origin persists; the surviving field reference keeps the shared pin; no duplicate object Cleanup or new native owner. |
| Negative source/authority | Reject rooting local normal storage, ordinary ref/pointer promotion, a value-returning property, vacant/partial whole T, void payload, gc ref<init T>, direct optional-root pin, sibling access through an exact root, write upgrade, incompatible complete T and treating C as an explicit address descriptor. |
| Bounds/alignment | First/last valid selected element, offset zero, maximum representable offset/extent, overflowed sum, one-past-end, packed/over-aligned fields and zero-extent nominal gates. Preserve checked index timing and known alignment; pinning cannot repair an invalid native typed-pointer projection. |
| Failure/partial acquisition | Inject failure before RootCell allocation, after support-edge allocation and at pin bookkeeping/commit preparation. No published descriptor, leaked count or premature old-value/root retirement. Test recoverable Core OOM and actual event-preparation failure separately from supervised terminal outcomes. |
| Copy/alias/end | Register mov versus independent Copy; copied optional Some/None; nested aggregates; source descriptor rebinding and a reference to its slot; End through a register alias; use/double-end after consumption. No per-Copy root/pin allocation or lost hold on register overwrite. |
| Loops/calls/joins | Repeated acquisition at one loop site creates fresh dynamic support, not an immortal site root. Named/reordered arguments, later receiver rebinding, nested calls/collection, hidden results and conditional paths preserve snapshots, selected places and all possible obligations. Reject unavailable required merges rather than erase them. |
| Graphs and relocation | GC object/support cycles without external roots are collectible under the admitted cleanup schedule. A support hold count or unreachable pinned cycle alone is not a global root. Relocation transfers field support without duplicate retain/release; destination-allocation failure preserves the old payload. |
| Cleanup and failure routing | Root/pin support remains through required finally/cleanup reads; same-frame catches release only exited residences. No generated user-resource Cleanup on unwind. GC Cleanup runs once, cannot resurrect, respects field order/affinity and fails without resumed generated cleanup or unsafe free. |
| Native and retained pointers | Explicit stable projection to rootless scalar/aggregate native inputs under unsafe authorization; callbacks/parking where independently admitted; declared retention keeps the explicit supporting holder until its real endpoint. A pointer alone, return status or cancellation observation cannot retain/unpin. Dangling inert data differs from invalid dereference. |
| Native-callable bodies | A root/pin acquisition can fail; a native-callable body needs its actual scoped no-escape proof or admitted containment before use. The helper table, Feature 14 and a successful acquisition test do not restore a general NoThrow promise or authorize outward Terrum exception propagation. |
| Owner/guard/lease/publication | A rooted object wrapping a disposed handle, returned pool rental or released guard remains invalid for that independent operation. Rooting/pinning adds no exclusivity, NoAlias, handoff eligibility, publication or cleanup-thread affinity. Known violations remain rejected under unsafe. |
| Aggregate outputs | Explicitly pin an initialized GC-backed input, project its supported native pointer and combine it with a separate vacant local output Home using DF/0C source-use 6 and selectors 8/10. Preserve independently declared backing through the input/output relationship without making the pointer a new checked source place. Pre-Invoke cancellation, success, complement, maybe-live and indeterminate post-Invoke failure retain the output proposal's exact states/dependencies. Cancelling the reservation does not cancel an independently needed pin or authorize output-address escape. A GC-backed vacant output place is not an admitted 0C local Home and cannot acquire an exact-place root/pin by this protocol. |
| Malformed Body | Truncated/overlong 0D, bad record kind/action/mode/carrier/reserved bits, wrong source/result complete types, R14/R15 misuse, wrong Home/TypeMap namespace, absent maps, out-of-range indexes, missing feature and DD substituted for root/pin. Reject before affected code generation/execution. |
| Capacity and rewriting | Body record/type/Home indexes 0 and FFFF, counts 65,536 and over-limit, global typed row endpoints/high bytes, checked frame/slot arithmetic, register pressure beyond fourteen staged descriptors. Reorder/duplicate permissible maps/records, change Block order and clone bodies; rebuild all references/offsets/source steps and retain meaning. |
| Runtime ABI/admission | Wrong 104-byte table/revision, null helper, malformed frame/call-transfer maps or states, incompatible target/domain/layout/cleanup provider, unsupported private 89–93 input and images with missing retained requirements. Reject before initialization/invocation, not a loader fallback. |
| Call handoff boundaries | Fault before/within entry registration, nested/reentrant calls, callee early parameter retirement, result checks/epilog failure, normal/exceptional FinishCall and plan reuse. Each acquired/staged/result hold transfers or releases once; no leaked/double count, premature publication, or hidden caller pin extension. |
| Separate compilation/load | Independently encode producer/consumer and diamond origins; share one domain across selected JIT/AOT modules. Accept compatible replacement with unchanged consumer bytes; reject changed descriptor/runtime/source/layout/cleanup contract. Deferred object Cleanup/root/pin keeps its actual helper/provider loaded. |
| Cross-feature gates | Closed generics/substitution preserve gc/access and descriptor size; unsupported witness/erased/async/task, external managed storage, normal shared support kind or alternate target does not become supported through Feature 14 or a recognized TypeSpec. |

### Required evidence, not acceptance by emission

First establish source/public-bound-program verification and independent
standard codec/admission/remapping cases for the selected schema. Inspect
emitted PE/JIT code, actual descriptor/helper/frame layouts, safepoint spills,
unwind registration and partial-acquisition paths separately.

Then run **supervised headless JIT and AOT executions** under each admitted
failure policy: force actual relocation while an unpinned exact root survives;
prove address stability during each overlapping pin; observe final-pin
retirement and later movement; inject acquisition failures; assert object/
support/cleanup counts, stale-generation rejection, caller/callee staging and
cross-module load retention. A collector that happens not to move an object,
or successful PE emission, proves none of the relocation/pin assertions.

Use the pinned SDK, normal developer outputs and existing NUnit supervision/
cleanup patterns, with compiler/library/input hashes and configuration tied
to the final patch. Confirm selected tests actually ran; skips, crashes,
timeouts and zero matches are not passing evidence. No visible-window or
hardware opt-in is needed for these cases. Existing managed-node ABI probes
remain evidence for their asserted prototype behavior, not qualification of
this new ABI.

Completing this slice would select and implement the root/pin runtime carrier.
It would not approve this document, implement normal shared backing or every
reference representation, migrate private modules, complete independent
failure/construction/handler capabilities, or accept the full compiler
contract-adoption milestone.
