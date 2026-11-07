# Integrated GC runtime boundary

**Status: unapproved architecture and owner-amendment recommendation.**
The explicit user decision is:

> It intends one integrated GC runtime interface, for which multiple GC implementations could be provided. Similar to how the .NET GC works

That decision establishes the integrated-interface direction, not a particular
DLL, collector, ABI layout or implementation. The process-wide selection,
lifetime and deployment rules recommended below remain unapproved. This
proposal changes no normative specification, compiler or runtime support.
It supersedes the separate-provider recommendations listed under
[disposition](#rootpin-proposal-disposition), not the accepted root/pin
source semantics or every remaining RootPin representation candidate.
The analogy does not select the CLR, .NET's ABI, or a .NET GC implementation.

The focused [common ABI and bootstrap candidate](integrated-gc-runtime-abi.md)
now recommends exact Windows x64 signatures, records, table/host attachment,
frames, allocation/publication, barriers and admission handoffs for the gaps
identified here. It remains a separate unapproved physical-interface proposal:
this architecture does not adopt it, revise the prior exact RootPin schema or
claim compiler/runtime support.

## Problem and scope

The [RootPin runtime candidate](reference-root-pin-runtime.md) supplies useful
exact-place, cell, frame and support-retirement analysis, but makes its
consumer boundary a distinct `terrum-root-pin-runtime.dll` with
`RootPinRuntimeInfoV1` and a twelve-operation table. The
[deployment candidate](reference-root-pin-provider-deployment.md) adds an
absolute provider path in each affected AOT image and JIT selection.
[Loaded-type](reference-root-pin-loaded-type-descriptors.md) and
[loaded-method](reference-root-pin-loaded-method-descriptors.md) admission
then require supplementary exports from that provider instance. Allocation
and completed-object publication remain separate gates. This is not yet a
single contract covering allocation, tracing, roots, pinning and barriers.
Renaming the DLL would leave that architectural split intact.

The accepted baseline already separates the necessary obligations:

| Owner | Established boundary |
|---|---|
| [References](../specs/language/references.md#formation) and [lifetimes](../specs/lifetimes.md#passing-and-storage-access) | An exact-place root preserves reachability; explicit pinning establishes bounded address stability. Neither replaces access, backing, owner or guard obligations. |
| [Memory model](../specs/memory-model.md#access-widths-representation-and-progress-guarantees) | Managed accesses need collector support, exact roots across safepoints and barriers independently of CPU ordering. Atomic pointer bits do not retain their referents. |
| [Ownership and Cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement) | GC unreachability triggers one complete, non-resurrecting retirement obligation before reclamation, not one Cleanup per descriptor. |
| [Windows x64](../specs/targets/windows-x64.md#1-scope-and-identity), [deployment](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) and [JIT](../specs/jit.md#admitted-execution) | The accepted `(1,1)` profile is rootless. Managed representations, runtime admission and retained code origins are separate requirements, not consequences of loading an image. |

This proposal assigns responsibility and the runtime/deployment boundary for
GC-enabled execution. It does not design a general host API, installer,
package resolver, scheduler, reflection service, normal shared allocator,
weak-reference API, or new source syntax. Rootless programs do not acquire a
GC dependency merely by using Core; the
[library dependency boundary](../specs/libraries.md#dependency-layers-and-package-boundaries)
remains unchanged.

## Recommendation: one common GC runtime ABI

Generated code and its loader target **one common GC runtime ABI**.
The selected runtime owns one GC domain and coordinates all services below.
Multiple GC implementations may implement that contract, directly or behind
a runtime-owned adapter. There is no independently selected root/pin service
beside the allocator/collector. A collector implementation can be packaged
separately inside the runtime's selected deployment closure without becoming
a second compiler-facing RootPin provider.

| Responsibility | Common runtime obligation; implementation freedom |
|---|---|
| GC allocation and collection | Own GC allocation identity, registered payload layout, construction/publication coordination, tracing, relocation where applicable, and reclamation. Allocation and collection must use the same domain and descriptor contract. Normal/native allocation domains remain distinct. |
| Root visibility | Consume generated GC maps/frames and admitted typed object/support layouts; account for live stack/register staging, arguments, saved results, static/runtime holders and admitted foreign transitions. A nonzero support hold alone is not an external root. |
| Pin/unpin and support retirement | Establish explicit exact-place and whole-object stability, exclude relocation during each live pin, share support on Copy, and release it at its actual endpoint. Pinning does not grant adjacent-place access. |
| Managed access barriers | Supply the generic barrier/helper or intrinsic contract used by generated managed stores and other admitted accesses. The implementation owns remembered sets, card tables and any collector-specific barrier state. |
| Safepoints and mutators | Coordinate attachment, cooperative rendezvous, root publication, relocation exclusion, foreign parking/resumption and admitted callback entry. Code generation and runtime agree on every possible safepoint and no-safepoint interval. |
| GC-related cleanup | Retain and schedule the complete retirement recipe after unreachability, keep its receiver and dependencies valid, and reclaim only after applicable obligations permit. Support release is not an authored Cleanup invocation. |

“Multiple implementations” does not promise that arbitrary collectors work
with unspecified barriers or maps. Each implementation must satisfy the
same selected value/call representations and all required runtime services.
An implementation needing an unassigned read barrier, managed-reference RMW
protocol or different safepoint discipline cannot be substituted merely
because it exports familiar names.

### Minimal integration versus redesign

A name-only DLL amendment or a RootPin shim beside an independent collector
preserves the split ownership and is insufficient. Extending the old table
could express a common runtime only by replacing its root/pin-only contract;
keeping its extent/export compatibility adds no required semantics.
Recommend integrating the existing typed cell/support work at the common
runtime boundary instead. A substantive direct-pointer representation redesign
would additionally replace value/call layouts and relocation maps throughout
independently compiled code. That is possible, but not required by the user
decision and not the smaller integration.

### Preserve source semantics and reusable representation work

The accepted [storage/GC cases](../specs/conformance/source-storage-and-gc.md)
remain the source baseline:

* `gc ref<T>` roots one fully initialized exact place, including the
  distinction between a handle slot and the object stored in that slot.
  Selection evaluates once and survives relocation without widening access.
* `ref *root` explicitly pins the present root's backing for the bounded
  stable reference. `C(gcObject)` explicitly pins the whole object and keeps
  its original GC allocation/Cleanup policy. Copies share existing support;
  ordinary reference formation and raw pointer projection do not acquire it.
* Descriptor lifetimes, support holds, referent generations, provenance,
  owners, guards, publication, affinity and code origins remain separate.
  No new `pin` qualifier, implicit marshalling or lifetime extension follows.
* Root/pin acquisition failure publishes no result and reverses actual
  partial acquisitions under the existing
  [allocation-failure contract](../specs/runtime/standard-failures.md#4-recoverable-allocation-exhaustion).
  Recoverable support cannot silently become terminal handling or optional
  absence. Runtime-admission failure is not source allocation exhaustion.
* Last-pin retirement remains prompt at its checked endpoint even when the
  runtime and metadata live longer. GC Cleanup remains once-only,
  non-resurrecting, before reclamation and on an unspecified cleanup thread.
  Normal strong cycles and mixed normal/GC cycles gain no collection promise.

Preserve complete Cleanup's authored-body-then-reverse-declaration-fields
order and the distinction from reverse-acquisition construction rollback.
Support release remains nonthrowing/nonsuspending and invokes no authored
Cleanup; scope/finally endpoints keep required support live rather than
inventing user-resource Cleanup on unwind. Preserve the prior candidate's
worker containment: an uncaught GC Cleanup event follows ordinary unhandled
failure termination, not native unwind to the collector. Failed retirement
does not resume generated field cleanup or authorize premature reclamation.

Reuse the RootPin proposal's **stable opaque-cell strategy** as the starting
common representation rather than introducing a second root model.
Object identity/current payload location, exact-place selection and bounded
pin support remain distinct. A moving implementation updates the runtime-owned
location under domain coordination; code cannot cache an interior address
across a possible safepoint. A nonmoving implementation can satisfy the same
contract but must still preserve root visibility, support endpoints and
cleanup; its selection is not a fallback for missing pin support.

The existing byte layouts, frame/call-transfer records and helper signatures
are still unapproved candidates, not an already assigned common ABI.
Retain their useful semantics, including no-failure Copy/drop, explicit
responsible residences, atomic call/result handoff and unwind ordering.
Fix one consistent physical representation in the eventual common target ABI;
do not require the old table extents or duplicate its function surface just
to preserve a prototype. Changing to direct relocating pointers would be a
separate representation redesign, not necessary to integrate the runtime.

### JIT/AOT maps, barriers and card tables

JIT and AOT emit and publish GC maps/frames, typed layout and retirement
descriptions, safepoint/transition information and calls/intrinsics against
the **common runtime ABI**. They do not consume a separate RootPin provider.
Source-free lowering reconstructs those facts from supported, admitted
semantics; a declaration or raw pointer cannot substitute for them.
Inlining, cloning, optimization and regeneration rebuild affected maps and
fixups, preserving partial prologs, staged values, hidden results and
exceptional endpoints. Unwind records alone do not describe GC roots.

Recommend a generic runtime barrier call as the baseline generated managed
write boundary. Intrinsic/inlined forms are permitted only after an exact
runtime-owned expansion, state-access and code-publication contract is
assigned. AOT cannot bake in a collector's card address, granularity or
no-barrier assumption and still claim unrestricted substitution. Any such
specialization retains an explicit runtime requirement and must be satisfied
or regenerated before use, not silently rebound.

A **card/remembered table is collector-specific internal state**, not a
language-level root carrier. It records the heap regions/edges the selected
collector needs to revisit. It neither enumerates live stack/register
residences nor stabilizes movable objects. Even an implementation whose
generic write barrier is a valid no-op still needs exact roots and explicit
pin semantics. Conversely, typed support retain/release does not discharge
a required generational/concurrent barrier. Bulk copies, field replacement
and initialization need their actual admitted barrier and support effects.
CPU synchronization/publication remains independently required.

Use the cooperative safepoint model as the initial common coordination
boundary. Collector algorithms may differ behind it; admission of concurrent
collection or managed atomics additionally needs their complete load/store/
RMW, progress and relocation contracts. Those capabilities are not selected
by this architecture proposal.

## Process-wide selection, deployment and replacement

### One selection and one domain

Recommend **whole-runtime, process-wide GC selection at bootstrap**, before
any participating GC allocation, root registration or affected initializer.
The host selects one runtime instance and one GC implementation/configuration
for it. “Whole-runtime” means a coherent selection for allocation, collection,
roots, pinning, barriers, safepoints and GC cleanup, not necessarily replacing
the JIT, loader or all non-GC services when choosing another collector.
There is no per-Module, per-image, per-graph or per-JIT-handle selection.
Multiple heaps/generations inside that implementation remain one domain for
these contracts, not separately interoperable runtimes.
This scope concerns participating Terrum GC execution; an unrelated foreign
runtime in the host process gains no interoperability or access to that domain.

One trusted loader authority serves independently compiled AOT libraries,
the AOT root and all JIT attachments. It resolves original semantic identities,
retains every expected binding, and admits types and native method/map
instances into that same domain. Sharing a process, DLL basename, ABI revision
or equal-shaped table does not establish membership. A second runtime/domain,
including another mapping of equal runtime bytes, cannot join by passing its
cell/descriptor pointers to the first.

The committed binding identifies the exact runtime instance, selected GC
implementation instance/configuration, domain, supported ABI/target contracts,
canonical admission authority and retained original code/allocator origins.
A collector implemented inside the runtime need not have a separate library
identity. If it is a separate binary, retain its actual selected load instance
and required closure too. Helper targets may belong to that admitted closure;
requiring every target to reside in one RootPin DLL is no longer the rule.
Trusted bootstrap establishes this identity; table shape or serialized
“already admitted” data cannot establish it.

### Bootstrap and native deployment boundary

The launch/embedding adapter establishes that common runtime binding before
publishing affected initializers or callable entries. AOT carries common-ABI
requirements, generated maps and relocatable bindings; it does not carry an
independently selected RootPin/collector path in every image. JIT uses the
already selected binding and the same admission authority. A library joining
an existing host must attach to that binding or decline affected use; it
cannot bootstrap its own collector.

No runtime DLL name, installation directory, absolute PE import, environment
setting, provider-address slot layout or public startup API is assigned here.
The minimum target bootstrap/binding carrier still needs an exact assignment
before independent AOT/JIT implementation; see
[prerequisites](#minimum-abi-decisions-and-prerequisites).
Static hosting of one process runtime and a shared runtime library are
packaging possibilities for that one boundary, not fallback routes.
A selected target/deployment must define and support its actual route rather
than guessing between them. Physical packaging of a GC implementation is
owned by runtime deployment, not authored NativeImport or an ordinary Module
provider chosen by source.

Preflight the selected native closure and required ABI/services, then perform
actual runtime/domain/descriptor admission and code/unwind publication.
Successful OS loading is insufficient. Affected source execution in DllMain
or under the loader lock is not enabled. Concurrent bootstrap/attachment must
serialize around one selection; failed admission publishes no affected code
and does not replace a committed binding.

Missing selected runtime/implementation, identity mismatch, unsupported ABI,
barrier or map contract, and unavailable required cleanup/representation
support reject before affected use. There is **no fallback** to a different
collector, another path/version, an image-private heap, rootless ABI, old
RootPin table or CLR. An explicitly selected nonmoving implementation in a
fresh process is a valid implementation choice, not a repair for a failed
moving-runtime admission. No resolver/search policy is inferred.

### Lifetime and replacement

Recommend retaining the selected runtime/GC binding, canonical identity maps,
admitted descriptors and required origin leases until **process reclamation**.
Per-image unload, JIT disposal, an empty import table or the last pin does not
release that binding. This is conservative code/metadata retention, not
rooting of all source objects or an extension of loans or pins.
Process exit promises no additional source Cleanup.

GC implementation or runtime-instance replacement is **not live replacement**:
changing selection requires a new process. No hot collector swap, second
domain, unload/rebootstrap or early whole-domain teardown is selected.
Those would require a separately approved exact migration/quiescence protocol
covering objects, frames, cells, barriers, pending calls, cleanup and code.

Ordinary compatible Module/method replacement remains distinct. New admitted
code joins the existing domain and preserves all retained contracts and
stronger relied-on facts. A regenerated entry has its own immutable method/
map publication identity; old entries, types, cleanup thunks and allocator
origins are not retargeted while obligations can reach them. Original type
identity is not replaced by a new row/name/layout. Failed unwind deregistration
still prevents freeing backing. A new process may select another conforming
GC without relowering collector-neutral AOT code; code specialized beyond the
common contract retains its additional requirements.

## RootPin proposal disposition

These are supersessions **within the unapproved proposal set**, not formal
adoption, a compatibility obligation or an implementation migration.

| Prior candidate | Disposition under this recommendation |
|---|---|
| [RootPin runtime](reference-root-pin-runtime.md) | Supersede its dedicated DLL/GetRuntime discovery and root/pin-only runtime boundary. Remove the requirement for `terrum-root-pin-runtime.dll`, `TerrumRootPinGetRuntimeV1`, the 112-byte `RootPinRuntimeInfoV1` and fixed 104-byte/twelve-entry table. Neither widening nor retaining that table is required. Reuse exact-place/cell/support, failure, cleanup, provenance and call-handoff analysis under the common ABI. |
| [Provider deployment](reference-root-pin-provider-deployment.md) | Supersede its recommendation as a whole: no required absolute ASCII RootPin path in AOT PE imports, same-P JIT loading, RootPin-specific exports/IAT checks or single-DLL target-membership recipe. Preserve no-fallback, selected-instance/domain identity, trusted load/closure obligations and process-lifetime retention at the common runtime boundary. A pathname/file identity remains evidence, not semantic or runtime-instance identity. |
| [Loaded-type admission](reference-root-pin-loaded-type-descriptors.md) | Replace the same-RootPin-provider `TerrumRootPinAdmitLoadedTypesV2` export requirement with typed admission by the common runtime/loader boundary. Preserve complete normalized identities, typed traversal/presence/support graphs, checked geometry, atomic interning/publication, original retirement/allocator/code origins and truthful producer versus consumer obligations. Cold admission remains distinct from allocating or initializing a source object. The V2 record layout is reusable design input, not a compulsory successor ABI. |
| [FixedArray amendment](reference-root-pin-array-layout-descriptors.md) | Preserve the unapproved typed element/count/stride, nested traversal and zero/unit validation analysis. Its frozen RootPin-info/table dependency is superseded; no requirement to keep the old supplementary record extent follows. No new source array or allocation API is selected. |
| [Loaded-method admission](reference-root-pin-loaded-method-descriptors.md) | Replace `TerrumRootPinAdmitLoadedMethodsV1` and same-DLL discovery with common runtime/loader admission. Preserve loader-derived closed method identity, distinct native entry generations, exact receiver/formal/result support maps, transactional publication and retained code/unwind origins. “Same provider” becomes the same selected runtime/domain and admitted origin closure, not matching DLL/export names. |
| [Exact RootPin schema](trrm-root-pin-schema-selection.md) | Preserve exact pre-decode selection and its no-F/G-composition boundary for existing candidate inputs. That snapshot does not silently acquire this common ABI. Feature `14/1`, TargetProfile `(2,1)`, F0 and Body `0D` remain prior candidates, not assignments made or approved here. Select an exact successor runtime/target and whole-schema agreement before exchange; revise or reuse draft numbers deliberately rather than guessing from equal version bytes. |

No old export becomes an alias for a new common entry, and no dual decoder or
compatibility adapter is required for unpublished candidates. An explicitly
selected old prototype remains evidence only for its actual contract.
The proposed integration must replace its runtime requirement coherently,
not reinterpret old artifacts by changing a DLL string.

## Owner amendments and cross-layer consequences

On approval, integrate the following together. No normative owner is edited now.

| Owner | Proposed amendment or preserved obligation |
|---|---|
| [Abstract runtime index](../specs/runtime/README.md), [lifetimes](../specs/lifetimes.md) | Establish one focused GC-runtime interface owner for the service/domain/selection contract. Keep exact roots, explicit pins, backing, support and actual endpoints in their existing semantic owners; link rather than duplicate those rules. |
| [References](../specs/language/references.md), [complete types](../specs/language/types-and-normalization.md), [Cleanup](../specs/language/ownership-and-cleanup.md) and [standard failures](../specs/runtime/standard-failures.md) | Preserve accepted syntax, complete backing/access identity, once-only GC retirement, failure ordering and partial-acquisition rollback. Change no source eligibility or failure category to accommodate a collector. |
| [Memory model](../specs/memory-model.md#compiler-runtime-module-and-ffi-obligations) | Link the common managed-access barrier and safepoint obligations. Keep exact roots, collector barriers, CPU ordering and reclamation independent. Do not prescribe a language-level card table or infer managed atomics support. |
| [Windows x64 profile](../specs/targets/windows-x64.md), [data layout](../specs/targets/windows-x64-data-layout.md) and [calls](../specs/targets/windows-x64-call-interface.md) | Assign one coherent GC value/call/frame/map and common-runtime ABI, with exact support/revision gates. Do not widen accepted rootless `(1,1)`, foreign Blittable admission or the one-pointer stable call projection implicitly. |
| [Windows deployment §§3–5](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support), [native lifetime](../specs/native.md#runtime-and-code-lifetime) and [JIT](../specs/jit.md) | Add common-runtime bootstrap/attachment, identity, GC-map and barrier publication, shared-domain admission and retained runtime/collector origins. Use the same boundary for AOT and JIT; keep code/unwind lifetime and compatible method replacement separate from collector replacement. |
| [Modules](../specs/modules.md), [sets](../specs/module-sets.md) and [graphs](../specs/native-graph.md) | Require compatible independently compiled participants to join the selected domain. Topology, forwarding or private packaging does not establish heap sharing, bootstrap a runtime, merge nominal identities or grant safe unload. |
| [Logical dependency/cleanup records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending), [requirements](../specs/representation/requirements-and-admission.md) and [binding](../specs/representation/binding-and-replacement.md) | Retain root/pin/cleanup/barrier/target requirements through declarations, expected imports, bodies and replacement. Replace only the carrier gaps actually assigned by the eventual ABI/schema. Preserve complete directed binding and original stronger relied-on facts. |
| [Registry](../specs/representation/registry.md), [format versioning](../specs/representation/format-versioning.md), [reader trust](../specs/representation/reader-trust.md) and [persistence](../specs/representation/persistence.md) | Agree on an exact schema/catalog and runtime requirement before writing/decoding. Standard `.trrm` carries semantic contracts/operations, not runtime/domain pointers, trusted loader keys, selected implementation paths, live maps or admission authorization. Native target data may carry relocatable maps/bindings, never a saved process binding. |
| Binder, common bound traversal/rewriting, verifier and lowering | Preserve typed allocation/root/pin, access/provenance, holder/publication/failure states and cleanup across every path. Derive and publish target maps and generic barriers from those facts; reject unsupported required routes rather than recognize a sample or call the old private collector. |
| Libraries and host adapters | Author safe wrappers against actual pin/owner/retention obligations. Keep raw native declarations caller-unsafe, with no automatic rooting or marshalling. Supply an admitted startup/embedding boundary without turning the runtime ABI into a new source library API. |

Consumers still perform structural, bounds, reference, schema-support and
binding checks required for safe consumption; the common runtime need not
re-prove producer-published body liveness or arbitrary native behavior.
Source compilation checks authored source. False producer/loader facts remain
contract violations, not promised detections by table membership checks.

## Minimum ABI decisions and prerequisites

This proposal deliberately assigns no export signatures, table sizes, wire
records, DLL name or numeric IDs. The
[focused ABI candidate](integrated-gc-runtime-abi.md) provisionally answers
the physical decisions below without changing their approval or independent
schema/implementation gates. Before independent binaries can target the
recommendation, the owners must select these exact minimum contracts:

| Required decision | Minimum content to assign |
|---|---|
| Common ABI and target requirement | Exact interface/profile identity and revision, supported services and collector capability envelope, calling convention and compatibility rules. Reconcile the existing Feature/TargetProfile candidates; do not infer support from number ordering or the old twelve-entry table. |
| Bootstrap/attachment carrier | One supported target route by which startup and later AOT/JIT attachments obtain the selected binding; exact readiness, instance/domain identity, origin retention, concurrency and failure handoff. Assign any native imports/fixups or host handoff this route actually needs, not a new independent RootPin locator. |
| Values, roots, frames and transitions | Exact common cell/value/call representations; typed map/frame schemas, live/owned states, static/runtime holder registration, mutator attach/park/resume and safepoint/no-safepoint rules, including prologs, results and admitted unwind/callback paths. Reuse existing candidate analysis without assuming its byte layouts are adopted. |
| Allocation/construction publication | Same-domain allocation/type inputs, provisional-root and initialization visibility, completed-object commit, failed-construction rollback and actual recoverable/terminal failure transport. Typed admission alone cannot publish initialized objects. |
| Generic barrier/intrinsic contract | Exact managed write operations and bulk/initialization effects, permitted helper/intrinsic forms, memory effects and runtime-state access/publication. Explicitly gate any needed read/RMW protocol; card layout and collector algorithm remain internal. |
| Typed type/method admission | Exact handoff of complete bound identities, traversal/layout/retirement recipes and native entry/support maps, transactional validation/interning and origin leases. Decide the common carrier, rather than automatically retaining V2/V1 exports or record sizes. |
| Cleanup/support/failure interface | Nonfailing Copy/drop and call settlement, pin/unpin commit/rollback, complete GC retirement invocation/containment, movement exclusion, metadata-admission errors versus source failures, and required runtime/code lifetime. No hidden shutdown or finalization policy. |

These are prerequisites, not implementation details that a compiler or
collector may independently guess. Complete source-free allocation, cleanup,
ownership, handler and stored/suspended representation codecs remain gates
for each route that needs them. A common runtime ABI does not complete them.
Any changed draft schema requires explicit agreement; it does not amend the
existing RootPin selector in place.

Remaining implementation/deployment choices include the first collector
algorithms, static/shared/plugin packaging, trusted installation/configuration
input and whether to standardize optimized barrier expansions. No default
collector, path or automatic selection policy is proposed. Concurrent/read-
barrier collectors, managed atomics, reclaimable metadata, early domain
shutdown and live collector migration need additional explicit contracts;
they are not prerequisites for a bounded cooperative baseline that excludes
them truthfully.

## Proposed conformance cases

These are future required outcomes, not executed tests or support claims.
Runtime cases require their complete admitted object/call/failure boundaries.

| Case | Required outcome |
|---|---|
| GCR-01 Multiple implementations | In separate fresh processes, select two implementations of the exact common ABI and run the same collector-neutral AOT code and equivalent JIT code. Preserve source observations and explicit support endpoints. Include a moving implementation; nonmoving success is not relocation evidence. |
| GCR-02 Shared domain | Exercise AOT-before-JIT, JIT-before-AOT, independently supplied libraries and a forwarding/diamond closure. All GC services and canonical loaded types use the selected domain; native JIT/AOT entry instances retain their distinct method/map identities. |
| GCR-03 Conflicting selection | Reject a conflicting participating domain/runtime/collector instance, including equal bytes, names, ABI revisions or table shapes. Concurrent attachments cannot create competing selections or choose a runtime from the first plugin. |
| GCR-04 Real root visibility | Force collection/relocation across loop safepoints, nested calls, later arguments, volatile-register spills, hidden/saved results and exceptional paths. Every live required residence is visible; a card table, unwind table or nonzero hold count alone cannot save a missing root. |
| GCR-05 Exact pins and places | Move an exact-place-rooted object, pin a field and separately pin its whole object, Copy support across admitted calls/fields, and end holders in different orders. Preserve selected-place/type/access and address stability until each final dependency ends; permit movement afterward. Handle-slot roots do not follow the value stored there. |
| GCR-06 Barriers | Under a collector requiring remembered edges, mutate managed fields, nested support-bearing values, arrays and bulk copies through the generic contract. Preserve both tracing and support accounting. Reject unsupported barrier/intrinsic requirements; adding CPU fences does not repair a missing GC barrier or vice versa. |
| GCR-07 Graphs | Collect an unreachable object/support cycle without globally rooting every cell or pinned allocation. Preserve optional presence and complete typed traversal; unknown layouts are not rootless leaves. Normal/mixed strong cycles gain no extra guarantee. |
| GCR-08 Transitions | Park/resume mutators and enter supported callbacks in the same domain with roots and actual stable pin dependencies visible. A raw pointer, unsafe authorization or native return alone cannot retain backing, pin implicitly or end declared retention. |
| GCR-09 Failure boundaries | Inject allocation/root/pin and admission-preparation failures. Roll back actual unpublished acquisitions, preserve old values and publish no partial descriptor/code closure. Distinguish source OOM/event preparation, loader resource failure, unsupported runtime and terminal policy; never choose another implementation. |
| GCR-10 Typed admission | Reorder/remap finite type/method batches, use recursive typed graphs and distinct nominal/qualified subjects, and inject conflict/resource failures. Preserve canonical identity, checked bounds and atomic publication without serialized trusted keys. Provider/loader body truth is not claimed to be re-proved. |
| GCR-11 Cleanup | After actual unreachability, run the complete admitted body-then-reverse-fields recipe once before reclamation, with a valid complete receiver and retained field/code origins. No resurrection, per-copy Cleanup, support-release invocation of authored code or invented affinity. A throwing body follows worker containment without resumed field cleanup or premature reclamation. |
| GCR-12 Replacement | Admit compatible regenerated/provider code into the existing domain while old roots, frames or deferred Cleanup retain old entry/layout/allocator origins. Do not retarget immutable maps or replace the live GC; changing collector selection requires a fresh process. |
| GCR-13 Publication/lifetime | Fail map/code/unwind registration and attachment without exposing affected entries or freeing registered backing. Detach images/JIT handles while retained obligations survive; retain runtime/collector/descriptor origins to process reclamation without extending source pins or rooting signature types' objects. |
| GCR-14 Schema/profile gates | Existing exact RootPin/F/default/private selections keep their actual meaning. No DLL-string/header/tag substitution, feature-derived decoder, rootless carrier match or new F/G composition admits the common runtime. Unsupported declaration-only requirements still reject. |
| GCR-15 Scope | Rootless execution does not require the GC, normal/native allocation retains its own allocator pairing, and foreign calls gain no managed ABI, implicit marshalling, source authority or cleanup promise. |

## Implementation gaps and acceptance boundary

The [implementation summary](../implementation.md) and
[CG-027 inventory](../implementation/conformance-gaps.md#cg-027) own current
support; documentation here does not promote it:

| Existing evidence | Limit relative to this proposal |
|---|---|
| [Image-private managed runtime](../../sources/Terrum.CodeAnalysis/Native/NativeManagedNodeRuntime.cs), documented under [native support](../implementation/native.md) | Allocation uses HeapAlloc and the emitted collector marks descriptor-described handles and sweeps with HeapFree. That prototype has no relocation-update or PinCell protocol and is not one shared cross-image GC-runtime ABI. |
| [RootPin provider prototype](../../sources/Terrum.RootPin.Runtime/RootPinLoadedTypesV2.cpp) and its recorded probe evidence | Experimental exports, typed admission, support frames, cooperative polling, test-host objects and test relocation exercise parts of the old candidate. The inventory also records a synthetic JIT resolver with exact path/identity/lifetime checks. These are not integrated allocation/collection/barrier support, compiler-generated root/pin JIT/AOT execution or a common runtime implementation. |
| `RootPinRuntimeDraft` structural consumption | Exact schema dispatch separates ReferenceSupport from foreign NativeAddress. Recognition does not implement source operations, the common ABI, GC maps, barriers, construction, independent exchange or deployment. |

Approval of the architecture, exact minimal ABI/target/schema agreement,
compiler-wide fact preservation, runtime/loader integration and independent
qualification remain separate gates. Reuse existing prototype components
only after reconciling their contracts with the selected common boundary;
do not treat their DLL/table/path machinery as a prerequisite to preserve.
Keep remaining gaps in the conformance inventory as those designs are
approved and implemented.

Acceptance requires independent producer/consumer and cross-module JIT/AOT
evidence for the final selected ABI, plus supervised relocation, overlapping
pins, barrier-sensitive collection, failure rollback, cleanup and retained
origins. Emission, a nonmoving run, provider-only probes or passing structural
fixtures cannot substitute. This documentation pass claims neither those
results nor completion of GC support or the broader compiler milestone.
