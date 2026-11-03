# Ordinary references with normal-backing support

**Status: unapproved; provisional implementation direction under the user's
authorized missing-contract workflow.** This candidate closes one physical
gap, not source admission, normative adoption, a public managed ABI or
implementation support. The authorization permits work against this direction
pending later review; it does not approve adjacent runtime or library choices.

## Decision and scope

Recommend a **uniform 16-byte, eight-aligned ordinary-reference value**
containing the selected address and an optional **normal-backing hold**.
The hold shares the allocation's existing normal retention control; reference
formation and Copy allocate neither a descriptor object nor another owner.
Normal class/box handles and references into their payload use that same
control, with distinct source authority and lifetime obligations.

Select this agreement only for **Windows x64, one jointly
lowered/regenerated native closure and one compiler-owned normal runtime
instance**. Separately compiled semantic inputs may participate after complete
binding and relowering. The first executable subset is synchronous,
single-threaded and has runtime-only-retirable normal payloads, defined below.
It admits neither independently supplied precompiled managed providers nor
cross-instance value exchange.

This is a distinct, compiler-private lowering agreement. It is not today's
accepted TargetProfile `(1,1)`, the address-only extension proposed for that
profile, or RootPin's candidate `(2,1)`. It assigns **no new numeric
TargetProfile, Feature, section, Contract, opcode, Body record or private
revision**. A host must explicitly select the agreement for the whole native
closure using the [out-of-band host key](#exact-host-selection-and-propagation)
below; a source type name, equal descriptor size or recognized TypeSpec does
not select it.

### Why another decision is necessary

| Evidence | Established contract; missing physical support |
|---|---|
| [References: formation](../specs/language/references.md#formation), [lifetimes](../specs/lifetimes.md#definitions-and-notation), [SGC-43](../specs/conformance/source-storage-and-gc.md#separate-compilation-and-representation-boundary) | An ordinary reference to a normal class/box subplace may outlive the source handle local. It retains bounded support for that allocation, not the handle slot, an explicit owner or a guard. This source behavior is already accepted. |
| [Normal retirement](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement) | Normal backing is stable and shared-retaining; its one object-level Cleanup follows final strong-hold/dependency retirement. Strong cycles are not collected. GC reachability and GC cleanup are different policies. |
| [Windows layout](../specs/targets/windows-x64-data-layout.md#1-scalar-storage-and-validity), [call interface §5](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | Neither pointer width nor the one-address call-extent projection assigns a stored backing-hold carrier or a reference-result ABI. |
| [Stored-reference candidate](reference-stored-descriptor-layout.md#remaining-decisions-and-support-gates) | Its 8/8 address value requires externally bounded support. It explicitly leaves references surviving the last normal handle gated on a support-hold/Copy/release/call decision. |
| [Provenance candidate](reference-provenance-carriers.md#decision-and-boundary) | Its operation-derived ledger distinguishes storage, values and capabilities, but deliberately does not implement normal shared-backing support. Compiler facts cannot keep freed storage alive. |
| [RootPin carrier](reference-root-pin-runtime.md#value-layouts-and-identity), [type admission](reference-root-pin-loaded-type-descriptors.md#decision-and-boundary), [FixedArray/V2 admission](reference-root-pin-loaded-type-descriptors.md#exact-supplementary-windows-x64-interface), [method admission](reference-root-pin-loaded-method-descriptors.md#decision-and-preserved-boundaries), [deployment](reference-root-pin-provider-deployment.md#decision-and-scope) | The two-word envelope and support-handoff model are useful precedents. Normal shared support and construction remain excluded. Loaded descriptors and an admitted RootPin provider do not create a normal backing hold. |
| [`ValueLayout`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs), [`NativeEmitter.NativeStorage`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs) | Current ordinary-reference layout is pointer-sized. Formation/reborrow forwards an address, and class-field reference formation is explicitly refused. Complete reference types now exist; the older proposal's historical missing-type observation is not the current blocker. |
| [`NativeManagedClassRuntime`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedClassRuntime.cs), [`NativeManagedNodeRuntime`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedNodeRuntime.cs) | Class factories allocate through the image-private managed-node machinery. Frame flags root whole object pointers; emitted offset lists drive marking/sweeping and HeapFree. There is no normal strong-count/control, interior-reference hold, normal final-dependent retirement or class Cleanup driver. Nonmovement of these allocations does not establish the accepted normal policy. |
| [`JitTests.ManagedClasses`](../../tests/Terrum.CodeAnalysis.Tests/Native/Jit/JitTests.ManagedClasses.cs) | Existing source tests include whole-handle staging across a later collection and explicit reclamation after handle retirement. They are evidence about the tracing prototype's intended tests, not qualification of normal-backed field references. |

These are source/specification observations, not newly executed tests.
Accepted semantics are sufficient to specify **what must stay valid**, but
neither an accepted target representation nor a current provider supplies
the required physical hold. This is not merely missing provenance analysis.

## Alternatives and selection

| Candidate | Assessment |
|---|---|
| **Selected: reuse compiler-owned allocation, typed value traversal, staging and code-origin machinery; add a distinct normal retention control and a second reference word** | One control per actual normal allocation, no per-reference allocation, nonfailing Copy, and a private direct-call agreement. Requires genuine normal allocation/retirement support; the tracing collector cannot be relabeled as that provider. |
| Keep eight-byte references and retain the original local/receiver in an ad hoc sidecar | Works only where an explicitly bounded holder already covers every use. An opaque field, Copy wrapper, result or substitution otherwise loses the sidecar. Retaining a unique owner or old local invisibly would also change its endpoint. This remains the address-only proposal's separate subset, not the solution here. |
| Extend the RootPin provider with a normal support kind | A coherent larger integration could reuse its envelope, frame/call protocol, loader identities and origins. It must explicitly add normal allocation, count/retirement policy, loaded-type support and admission; an ObjectCell, RootCell or permanent PinCell is not that kind. This would require a coordinated amendment of the RootPin candidates, not an alias or automatic Feature-14 dependency. Not needed for this bounded normal-only closure. |
| Unify normal, GC, pin, arbitrary view and erased support behind a public universal anchor/provider | Could eventually enable independent mixed-provider exchange. It also needs a common representation, support-kind dispatch, tracing of mixed edges, provider/bootstrap identity, full cleanup and erasure contracts. That is substantive redesign, not a smaller completion of ordinary normal-field references. |

The selection is based on the bounded runtime obligation, not compatibility
with unpublished layouts. It intentionally replaces the address-only
candidate **inside this separately selected closure**, uniformly, rather than
choosing eight or sixteen bytes per value origin. It changes neither that
candidate's externally bounded agreement nor RootPin's envelope/helper ABI.
There is no implicit adapter between them.

## Exact host selection and propagation

**The existing profile mechanisms cannot select this agreement without
conflating distinct contracts.** Their current roles are:

* [`NativeTargetProfile`](../../sources/Terrum.CodeAnalysis/Semantics/NativeTargetProfile.cs)
  is a static Windows AMD64 machine/pointer-size definition, not an instance
  or selectable agreement. Its pointer size remains eight; a sixteen-byte
  reference descriptor does not widen native pointers.
* [`Compilation.GetNativeProfileVersion`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs)
  derives private `native-*` profiles from retained types and operations;
  [`ModuleCompiler.ReadNativeProfile`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  reads/checks those private semantic profiles. The tracing-class
  `native-19`/`native-20` identities do not select normal retention.
  Reusing or inferring one would not distinguish the two implementations.
* [`CompilationOptions`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs)
  and [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  retain checked-failure policy but no native-agreement selection.
  [`SemanticVerifier`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs),
  [`NativeEmitter.EmitCode`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
  and [`NativeJit.Execute`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs)
  have no such selector; forwarding only runtime policy cannot supply it.

Recommend the exact, case-sensitive ASCII host key
**`terrum.experimental.windows-x64.normal-backing`**. The proposed nullable
string option is **`CompilationOptions.NativeLoweringAgreement`**; proposed
selector-bearing verification/emission/JIT overloads take the same explicit
**`nativeLoweringAgreement`** selection. These are candidate API additions,
not existing support. Null/omission retains today's noncandidate default;
empty, unknown or differently cased values reject, without trimming, aliases
or discovery. A recognized but unimplemented agreement rejects as unsupported,
never falls back to address-only references or tracing handles.

Resolve the key into one immutable, per-closure lowering context. Do not
mutate a process-global `NativeTargetProfile` mode. Runtime-failure policy,
source arithmetic defaults, Core registration, invocation profiles and exact
input-schema selection remain independent. Selecting this key establishes
neither their support nor a normal runtime instance by itself.

The required propagation path is:

1. **Binding and layout.** `Compilation.ValidateOptions` resolves the host
   selection before target-sensitive binding, layout queries or constant
   evaluation. Every admitted `Create*` path passes the context through
   `Binder` and `ValueLayout`, including nested fields, wrappers and closed
   substitutions. Record its identity in memory with the resulting
   `BoundProgram` as proposed `NativeLoweringAgreement` state. Layout, ABI and
   verification caches must distinguish agreements, not just complete type
   spelling or machine architecture.
2. **Mandatory verification and closure binding.** `SemanticVerifier.Verify`,
   `VerifyLibrary` and the applicable reference/import verification paths
   receive the explicit host selection and check it against that recorded
   state before target-dependent validation. Rewriters/adapters preserve the
   context. Separately supplied target-independent semantic inputs still need
   their independently selected supported schemas, complete binding and
   recomputed lowering under the one closure context. Changing a program's
   identity stamp cannot convert old eight-byte field/call layouts; a
   serialized assertion cannot opt the host in.
3. **AOT and JIT.** Selector-bearing `NativeEmitter.Emit`/`EmitCode`, admitted
   native-import variants and `NativeJit.Execute`/`ExecuteWithData` forward
   the same selection through re-verification, `PeCompiler`, layout/call
   classification and normal helper/state construction.
   `NativeJit.EmitForExecution` must forward it on both the ordinary and
   direct-native-import branches. Preserve the checked agreement identity
   with in-memory `NativeCode` and require it before JIT attachment. Existing
   overloads without selection mean the noncandidate default and reject a
   candidate-bound program/code; they must not infer the host's choice from
   its stamp. An unsupported overload/worker rejects before affected use.
   AOT fixes the agreement into the regenerated executable and its private
   state/helpers; launch does not reselect it. JIT fixes it before publishing
   entries. Equal keys do not make separately created runtime instances or
   cached native closures interchangeable.
4. **CLI.** Recommend the exact option
   **`--native-lowering-agreement terrum.experimental.windows-x64.normal-backing`**
   on source `analyze`, `build`, `build-native-import` and `jit` only.
   [`TryReadCompilationOptions` and `CompileTrees`](../../sources/terrumc/Program.Compilation.cs)
   parse it once, set the compilation option and pass the same selection to
   the chosen verifier/emitter/JIT route. Omission retains existing behavior;
   duplicate/missing/unknown values and unsupported command/mode combinations
   are usage errors. `analyze` establishes only its admitted semantic checks,
   not execution support. Native imports retain their existing unsafe,
   signature and support restrictions. Library/export, Core-library,
   source-grant/component/frontend and persisted module/TRRM/native-package/
   graph commands are outside this first CLI surface and reject the option;
   a forwarding worker cannot silently discard it. Existing `--runtime`,
   `--arithmetic-profile` and diagnostic protocols are unchanged.

This key is a host lowering choice, **not an encoding-schema selector,
standard TargetProfile/Feature identity, private revision or serialized
authorization**. It is not written into semantic metadata or used to make an
old native package compatible. It selects neither the address-only extension
nor RootPin `(2,1)`, and admits no independently precompiled managed provider.
All existing source, runtime-support and qualification gates below still
apply after explicit selection.

## Exact carrier and allocation identity

The following are proposed nonforeign machine layouts, not Terrum declarations
or serialized records. Words are little-endian.

| Complete value | Size/alignment | Payload |
|---|---|---|
| Ordinary `ref<T>` or `write ref<T>` | 16/8 | `u64 address` at offset 0; `NormalBacking* support` at offset 8 |
| Optional ordinary reference | 16/8 | Absence is both words zero; present values have nonzero address |
| Normal class handle or normal `box<T>` handle | 8/8 | Non-null `NormalBacking*`; the provider resolves its payload |
| Optional normal handle | 8/8 | Zero means absence |

Inner qualifications, descriptor rebinding and Copy eligibility change no
width. They remain distinct complete types and permissions. In particular,
a unique class handle still has one responsible owner and cannot be copied
merely because its physical carrier matches an aliasable handle.
An ordinary descriptor can be Copy without T being Copy.

A nonzero support is the **existing control for the normal allocation
containing the selected place**, not a pointer to a handle variable or to a
new reference-specific object. Its payload base never moves while live.
Every admitted normal class/box subplace formation carries that control.
Zero support is permitted only for an externally bounded stable place whose
complete obligations cover its extent; it is not a request to recover an owner
from an address. Zero address with nonzero support is malformed, not absence.
Presence never proves initialization, write permission or backing validity.

One provider-created control belongs to one allocation generation. It keeps:

* the actual runtime-instance identity and nonreused live allocation identity;
* the stable payload base, checked size/alignment and complete admitted layout;
* the strong backing-hold count, publication/retirement state and nonallocating
  retirement bookkeeping;
* the actual allocator/release pairing and retained type/helper/code origins.

Control headers and layout/traversal descriptions are **compiler-private
within this regenerated closure**. They have no independently replaceable
binary consumer, so this decision deliberately selects no public header,
vtable, export, registration API or guessed header subtraction. The
compiler must emit all users against the same implementation. The pointer
stored in a descriptor is stable and supplied by the actual typed handle;
raw pointer bits, a descriptor-slot address, a process-wide address registry
or the prototype's `payload + 32` convention cannot manufacture it.

Create the control as part of the already admitted normal class/box allocation,
with its payload and retirement capacity. Unpublished construction owns one
provisional backing hold. Success transfers that hold into the initial handle
only after complete construction/validation, without another retain.
Failure retires initialized child acquisitions in reverse actual acquisition
order, then releases the private payload/control with their allocator/code
origins still valid; it never invokes incomplete-parent Cleanup or publishes
a handle. This does not insert a hidden
`own<T>`, separately owned request allocation, boxing operation or owner
capture. Initial allocation can fail under its established construction
contract; reference formation, copying and retirement cannot allocate a
replacement control to repair missing support.

References designate one initialized positive-extent, correctly aligned
place. Checked offset/extent arithmetic must remain within that actual
allocation and the selected field/payload. A zero-data `void` place remains
nonaddressable; zero-extent nominal referents remain outside this candidate.
Keeping the allocation alive does not grant sibling access or prevent
permitted writes to the selected live place. A permitted Copy write changes
contents, not automatically the place's storage generation.

### Descriptor slots and nested backing

An outer reference to a descriptor field retains the allocation containing
that **slot**. Copying the stored descriptor instead retains the support named
by its current **value**. A reference to a local class-handle slot remains
bounded by the local slot even though copying that class handle retains the
object. Following a stored child handle selects the child's actual allocation;
it does not retain the parent merely because evaluation passed through it.
Box payload storage and any class handle stored in that payload are likewise
different selections.

Later slot rebinding never redirects an earlier copied descriptor. Reading
through a reference to the slot obtains its current value and performs the
corresponding typed hold acquisition. Capture the address/support pair without
an intervening user operation; confinement prevents observation of a partial
pair. This supplies no 128-bit atomic access, concurrent sharing, guard or
handoff protocol. Packed fields require supported exact-extent unaligned
copies; borrowing a descriptor slot still requires its own actual alignment.

Non-Blittable Sequential/Transparent holders use the selected field widths
and ordinary placement rules. Transparent wrappers inherit their carrier
classification. Raw Union/foreign/constant admission does not follow from a
known size. Closed substitutions use the same complete descriptor layout;
an unsupported generic/witness/erased representation remains unsupported.

## Hold acquisition, Copy, publication and retirement

The following are exact **internal provider operations**, not selected public
entry points or new semantic IL operations. All generated code uses one
provider/runtime instance.

1. **Acquire a normal field reference.** Evaluate/capture the receiver and
   selectors once at the source formation point. Keep the actual handle live
   while checking the initialized exact place, bounds, alignment, access and
   independent obligations. Prepare the destination, increment that
   control's backing count, then install the complete address/support pair
   and live-residence state in a no-user-code commit interval. No root or pin
   is acquired. Earlier validation failure publishes no descriptor.
2. **Copy.** Each new logical descriptor/handle residence retains its same
   control once. Recursive Copy applies typed optional/field traversal, not
   scanning pointer-shaped bytes. Register aliases and relocation of the
   same logical value add no hold. A transfer moves its one hold and vacates
   the old residence. Neither duplicates T or an explicit owner.
   Reborrow/access attenuation preserves the selected address/control and
   acquires only a shared hold for any new residence, never a root or pin.
3. **Replace/store.** Stage the complete new value and its holds before
   retiring the old destination. Commit under ordinary initialization,
   dependency and confinement rules. Self-assignment retains the incoming
   hold before releasing the old one. Failure before commit leaves the old
   value in its actual current state; unrelated prior writes are not rolled
   back. An optional absence has no support hold.
4. **End.** Clear that residence's responsibility exactly once, then release
   its hold. Other descriptors and handle copies retain theirs. Retirement
   of the selected T generation, replacement that invalidates a surviving
   dependent, or release of an explicit owner/guard still requires the
   existing semantic checks; a positive count cannot authorize it.
5. **Last hold.** The provider completes the admitted normal retirement
   recipe before freeing payload/control storage. Normal strong cycles,
   including cycles through stored supported references, keep their holds
   and are not traced away. Equal addresses after reuse never revive an old
   allocation/place identity.

For admitted live inputs, retain/drop are nonallocating, nonsuspending and
nonfailing. Use checked `u64` counts, never wrapping or saturating counts.
Each counted residence has prepared storage; aliases do not count twice.
The selected address space bounds actual simultaneously representable holds.
A smaller implementation capacity must be established before construction/
admission, not discovered as a new failure of an otherwise valid Copy.
Traversal/reclamation uses preallocated or control-owned work state, not an
allocation or unbounded native recursion at final release.

The first executable subset requires **runtime-only retirement**: recursively
admitted scalar/native-address fields, normal handles, ordinary reference
descriptors and supported inline Copy storage, with no authored Cleanup,
explicit owning/resource/task/guard fields or GC/pin support in the payload
graph. Its derived typed field releases preserve the accepted reverse
declaration order and allocator pairing and invoke no source code.
This permits support release on checked exceptional exits without inventing
authored Cleanup on unwind.

This restriction is a before-use runtime-support gate, **not a new source
ban on Cleanup-bearing classes/boxes**. Their accepted one object-level
Cleanup, complete receiver, throwing-retirement, field order and exceptional
cleanup rules remain authoritative. They require a fully admitted normal
retirement driver and every-exit schedule before this carrier can be used
for them. Do not silently skip Cleanup, run it once per reference, run it on
an arbitrary GC cleanup thread, or introduce deferred-finalizer/resurrection
policy to widen this first subset. Storage free and release of runtime backing
holds are not user-resource cleanup.

No last-use optimization may advance an observable Cleanup/retirement
boundary. No failure produces a null nonoptional reference, absent optional,
default T, lost support word or success-shaped fallback. Bad domain/control
state is a provider-contract violation, not recoverable allocation exhaustion.

## Sources, staging and direct calls

Keep the [operation-derived provenance ledger](reference-provenance-carriers.md#common-compiler-carrier).
The support word implements **one actual normal-allocation hold**, not the
whole dependency set. All source versions, whole-source permissions,
independent owners/guards/publication/affinity and code origins remain checked.
Unknown required facts remain unavailable.

In particular, a result permitted to use several sources does not narrow its
published bound to the one actual support word. Every possible source still
needs its declared retained extent. Where additional obligations have no
admitted carrier or established external holder, refuse the use; this
candidate introduces neither a hidden support-bundle allocation nor a
pointer-to-owner registry. Captures attach to the actual retained holder and
survive failure where its ordinary mutation does.

For direct nonforeign calls inside this closure:

* A 16-byte ordinary reference or equivalent wrapper uses an **indirect
  by-value input**, with an independent caller-prepared copy at least
  16-aligned, and **hidden result storage** for a reference result. Result
  extent/alignment is 16/8; return its hidden pointer in RAX. Do not split
  the two words across RAX/RDX or treat the input as a borrow of the caller's
  descriptor slot. A normal handle remains one integer-position carrier.
* Use the ordinary Windows physical sequence: hidden result pointer when
  required, admitted explicit receiver, then payload-bearing formals.
  Preserve logical unit positions, shared integer/floating ordinals,
  RCX/RDX/R8/R9 and later stack positions, shadow space, alignment and
  preservation. No new hidden source parameter/runtime-context argument is
  introduced; generated helpers address the one closure's runtime state.
* Evaluate receiver/arguments once in source order. Each completed staged
  value has its own live hold until invocation handoff. A later source-slot
  rebind cannot redirect it; permitted referent writes can still change
  what the callee reads. Recheck actual owner/guard/access validity before
  invocation rather than freezing referent contents.

Use the **handoff state machine**, not the RootPin ABI, from the existing
[call-transfer design](reference-root-pin-runtime.md#calls-results-and-raw-native-projection).
For this compiler-private provider, preallocate one plan with exact generated
entry identity, staged input residences, parameter mapping, prospective result
residence and state. Its concrete record layout is private to the regenerated
closure. The required transitions are:

| Phase | Responsible holds and failure behavior |
|---|---|
| Prepared at Invoke | Push the plan on the runtime instance's reentrant per-thread call stack. Caller staging still owns inputs. No source operation intervenes before entry. |
| Entry commit | Match the exact generated entry; install all parameter residences and transfer responsibility from staging in one no-user-code commit, without extra retains. Mark entered before any source precondition, callback or fallible body operation. A fault before commit leaves prepared holds caller-responsible; entry/unwind handling cannot release them as callee holds too. |
| Entered | Callee parameters transfer or retire their own holds under actual scope state. Do not retain an invisible caller copy until return, delaying a last-dependent endpoint. Borrowed receivers retain their separate call-extent holder. |
| Result prepared | After callee checks and required cleanup, transfer the saved result hold to private prospective caller storage. It remains unpublished through the machine epilog. |
| Finished | On normal completion, commit the prospective result for ordinary publication. On failure, release prepared inputs and any unpublished result once; entered inputs follow callee scope/unwind state. Close/pop the plan on both paths before any outer residence drain; reuse only after reset. |

Nested calls use distinct plans. Unrelated helper/native/callback entries
cannot consume a pending plan. Prolog/stack-probe and epilog failures need
the selected transfer-aware supervision/unwind support; if unavailable,
decline that failure/call route before use, not a guessed caller/callee
ownership transition. Source-free exception regions retain their separate
codec gate. This reuses the established algorithm without passing NormalBacking
values to RootPin helpers or adding normal support to their table.

A callable's publication/dependency/output phases remain unchanged. Output
bytes alone publish nothing; an initialization destination for a stored
reference initializes its 16-byte **descriptor slot**, not T. Uncompleted
`ref<init T>` is still non-Copy and gains no stored layout or escaping use.

The accepted one-address **rootless call-extent projection** remains a
different boundary, with the caller holding complete support through the
invocation and its cleanup. It does not become this value ABI. Raw pointer
extraction remains separately explicit/unsafe and acquires no hold.
Foreign imports/exports, NativeCallable and the bounded Terrum function-pointer
ABI gain no normal handle or support-bearing reference signature.

### Provisional cancellation is still borrowed

The [cancellation candidate](cancellation-and-terminal-settlement.md#proposed-core-sourceapi-surface)
selects an affine source and Copy borrowed tokens, not independently owned
request state. If an authored token descriptor carries an ordinary reference
to the source's request field, this carrier can preserve its physical normal
backing; it selects no token field name, extra allocation or public layout.

A permitted source-handle move transfers the same unique source responsibility
and leaves allocation/request identity unchanged. Tokens and token-containing
Copy aggregates share physical backing support, not request authority.
Ending/replacing the unique source while tokens or captured uses still
require it remains invalid, even if a backing count would preserve the bytes.
Aliasable-class field references surviving a last local handle are therefore
not a precedent for tokens outliving the source's explicit owning endpoint.
Cancel changes the live state read by the token; no captured Boolean snapshot
is substituted. A request/status observation settles no task, reservation,
guard, loan or token dependency.

This physical choice adds no Task/awaiter, async-frame, cancellation API,
threading or terminal-settlement admission.

## Provider identity, JIT/AOT and retained origins

The provider is generated **inside the admitted native closure**, not a DLL
selected by source or another process-wide RootPin role. Reuse existing
native allocation/alignment primitives and code/data/unwind publication
machinery only where they meet this contract. Normal allocations are kept
out of the prototype's tracing allocation list; `ManagedHeap.Collect` cannot
reclaim them or supply their backing holds.

Each execution instance has one exact runtime identity and complete bound
type/layout/entry map. AOT emits that instance's state/helpers with the
closure; JIT prepares the corresponding state/helpers before publishing its
entries. Both use identical descriptor, strong-cycle, staging and retirement
semantics. No CLR-managed execution or alternate target is implied.

This first agreement does **not** share objects between independently created
JIT and AOT instances, even within one process or for identical source/code
bytes. Compare the actual selected closure/runtime instance, original
Module/definition/substitution and code-load generation, not addresses,
type names, DLL basenames or table shape. Cross-instance calls/values reject.
A future common external provider needs its own complete identity/bootstrap/
ABI agreement; neither RootPin's absolute provider path nor its singleton D
supplies one for normal backing here.

Retain the actual leaf layout, helper, allocator, generated retirement and
unwind origins through every live hold, call plan and pending retirement.
An intermediate Module/forwarder is not the allocation's release domain.
The conservative initial implementation retains the native closure's origins
for its runtime-instance lifetime; disposal of an attachment is not early
unload authority. If remaining cycles/obligations prevent quiescence, retain
the required backing/origins rather than free reachable code. Process exit
adds no source Cleanup promise. Prompt value-hold release is still required;
conservative code retention must not retain source objects by itself.

## Persistence, consumers and remapping

This is target lowering of typed formation, Copy, load/store, transfer,
call/result and retirement operations, not a new semantic instruction.
Reuse complete reference/optional/handle type identities and the shared
Dependency/copy-origin contracts where their exact codecs are supported.
Neither declaration source rows nor serialized authorization bits create a
live NormalBacking control.

Semantic interchange never writes runtime pointers, payload addresses, hold
counts, runtime-instance identities, call plans or verifier caches.
Reconstruct dependencies and execute support transitions after complete
loading/binding and selection of the native agreement. The
[private checked-reference candidate](checked-reference-private-persistence.md)
can carry only its actually supported complete types/operations; it does not
add normal backing, class construction or this native layout automatically.
Keep old prototype/private/Development schemas rejected where they cannot
preserve the distinction. Equal version bytes do not select a new decoder.

Because no standard target/capability identity is allocated here, do not
emit target-dependent standard layout observations or requirement rows that
pretend this agreement is `(1,1)` or `(2,1)`. Standard independent execution
and native-package reuse requiring an unrepresented support/profile
obligation remain gated. Target-independent semantic input may be inspected
or jointly relowered only under its complete supported schema/binding rules.
Old eight-byte native field/call layouts must be regenerated as a whole,
not reinterpreted because their semantic `ref<T>` spelling matches.

Consumers validate exact selected schema/rules, ranges/references, supported
complete types/layouts, original provider expectations, source/capture/output
phases, Copy/transfer modes, runtime/entry agreement and actual code lifetime
before affected use. Producers publish truthful initialization, backing and
body facts; consumers are not required to semantically re-prove those bodies.
A false claimed normal handle or retirement recipe is a provider violation,
not a reason to guess support from pointer bits.

Binding/replacement preserves original nominal, payload/field, allocator and
cleanup identity, not merely offsets or equal byte counts. Rewriting visits
nested complete types/substitutions, FieldDef/Import/FieldPath references,
source/formal mappings, copy-origin summaries, Homes, Body maps and call/store
operands. Rebuild derived layout, indexes, record/code offsets and source
data, reconstruct the ledger, and regenerate private type/entry maps and
support transitions. Do not persist or remap a live control as a metadata row.

## Intended integration and acceptance gates

On approval, the [target layout/call/deployment owners](../specs/targets/README.md)
would own this bounded normal representation, private-call agreement and
runtime-instance/origin rule. The [layout boundary](../specs/data-layout-and-abi.md)
would link that admitted case without making references Blittable.
The [logical-record owner](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending)
would distinguish this physically supported subset from the still-unassigned
general normal/GC/erased/runtime carriers.

No grammar, reference formation, whole-source permission, unique-owner
endpoint, normal Cleanup rule, GC/pin contract or cancellation API is replaced.
The provenance proposal remains the ledger owner. The address-only and
RootPin proposals retain their distinct agreements; this proposal owns only
the normal-only support-bearing alternative and its explicit gates.
No normative owner or implementation inventory is changed by this patch.

The following are **required future tests**, not passing evidence:

| Family | Required positive and negative outcomes |
|---|---|
| Host selection | Omission preserves the noncandidate default. Exercise the exact key through binding, verifier re-entry, AOT and both JIT emission branches; reject malformed/unknown/duplicate CLI selection, unsupported commands/modes and host/program/code agreement mismatches. Concurrent compilations and caches cannot exchange agreement-dependent facts. Equal keys do not admit cross-instance native reuse; semantic relowering never becomes native retagging. |
| Actual backing | With unrelated normal classes and boxes, retain a field reference after ending/rebinding the last aliasable handle local; reads remain valid and final reference retirement frees the actual allocation once. Copying/moving handles preserves the allocation generation. |
| Slot versus value | A reference to a local handle/descriptor slot cannot escape it. A reference to a descriptor field keeps that field storage; copying its current descriptor instead keeps the other referent. Rebinding affects only subsequent slot reads. Follow child handles without retaining the wrong parent allocation. |
| Copy aggregates | Copy/store/rebind nested reference holders, optionals, closed substitutions and supported array elements. Observe 16/8 reference layout and recomputed enclosing placement, correct support sharing, self-assignment and exactly-once drops. Register aliases add no count. |
| Source bounds | Preserve broad multiple-source and backing-only contracts despite an actual single support pointer or precise copy-origin summary. Missing extra support/external bounds reject, never become empty or trigger hidden bundle/owner allocation. |
| Unique/guard endpoints | A reference may survive a valid allocation-preserving unique-handle move, not the unique owner's retirement or a guard/lease release. Loans into old inline owner/descriptor storage do not follow a move. Raw pointers cannot retain backing. |
| Cancellation composition | Copied tokens and token-containing holders observe the same live request field before/after a permitted source move and Cancel. Source end while dependent uses survive rejects. Query/request does not settle retained operation/output/cleanup obligations. No token owns an independent request allocation. |
| Direct ABI/staging | Exercise indirect reference inputs/results, normal handle carriers, hidden-result order, all register/stack boundaries, mixed floating/unit positions and named arguments. Later arguments rebind a source or fail; staged address/support remains exact or is released once. |
| Handoff/failure | Inject failure before entry commit, in preconditions/body, result checks and epilog settlement; test nested calls, early parameter end and plan reuse. No leaked/double hold, delayed endpoint from an invisible caller copy, restored moved source or unpublished result escape. Required unsupported unwind/region routes decline. |
| Construction/capacity | Inject payload/control/traversal-capacity failure before publication: no source/reference/default result escapes; actual partial acquisitions roll back. Copy/formation never allocate or fail from counter exhaustion. Test last valid checked extents and overflow, invalid controls and stale generations. |
| Retirement/policy | Derived runtime-only field releases use exact types, declaration order and allocator origins. Normal strong cycles remain unreclaimed. Authored/owning cleanup, mixed GC/pin graphs and unsupported thread/guard protocols stay gated rather than silently using tracing/finalization or skipping Cleanup. |
| Provider/target isolation | Qualify JIT and AOT separately using the same semantic inputs. Reject cross-instance exchange, old tracing handles, RootCell/PinCell support, equal-shaped RootPin values and 8/16-byte profile mixing. Detaching an intermediate image must not free retained leaf origins. |
| Persistence/rewriting | Independently encode supported target-independent semantics, bind bodyless original expectations, reorder types/fields/maps/providers and relower. Preserve sources and identity; reject unsupported normal backing/target requirements instead of persisting live pointers or forging profile numbers. |

Implementation remains required in explicit host-selector propagation and
agreement checks, normal allocation/control construction, typed traversal and
runtime-only retirement, reference/handle layout and Copy/drop lowering,
mandatory provenance/owner checks, call handoff, source-independent binding
and final-patch JIT/AOT qualification. Existing tracing tests or RootPin
provider probes establish none of those results.

There is **no further physical carrier or host-selector choice inside the
selected bounded agreement**. Its immediate blockers are implementing the
explicit selection path and admitting the real normal provider with all
corresponding value/call transitions. Source normal class/box construction and
the provisional cancellation family retain their own admission gates.
Cleanup-bearing backing, mixed normal/GC graphs, suspension/erasure,
independently supplied native providers and standard profile/capability
publication require their complete separate contracts; this decision does
not claim their completion.
