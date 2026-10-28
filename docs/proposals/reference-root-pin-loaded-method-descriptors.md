# RootPin loaded-method descriptor admission

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract direction.** This candidate specifies the
`LoadedMethod*` admission and lifetime boundary. A provider-side prototype has
been implemented and qualified under this direction; that partial evidence
does not constitute normative adoption or end-to-end loader/compiler support.

## Decision and preserved boundaries

Recommend one **transactional, same-provider loader export**,
`TerrumRootPinAdmitLoadedMethodsV1`, with candidate interface identity
**`windows-x64-root-pin-loaded-methods/1`**. The domain's trusted loader derives
complete selected method identities and their generated entry-frame maps;
the provider copies, validates and interns their runtime projection.
Descriptors, the loader's complete identity subjects and required origins
remain valid for the domain's lifetime.

This supplements the previously unspecified method-admission boundary in
[RootPin frame support](reference-root-pin-runtime.md#frame-support-jit-and-aot-lowering)
and [call handoff](reference-root-pin-runtime.md#calls-results-and-raw-native-projection).
It makes those sections' opaque admitted method identity concrete without
replacing their source ownership, call phases or hold-transfer state machine.
It preserves:

* `windows-x64-root-pin/1`, revision **1**, its **104-byte/twelve-entry**
  operation table, **112-byte** `RootPinRuntimeInfoV1` and
  `TerrumRootPinGetRuntimeV1`. No thirteenth entry is added.
* The selected [loaded-type V2 candidate](reference-root-pin-array-layout-descriptors.md#exact-supplementary-abi-amendment):
  `TerrumRootPinAdmitLoadedTypesV2`, the **72-byte** batch and **96-byte**
  nodes. There is no combined batch, V1 alias, inferred node stride or
  reinterpretation of that schema.
* The existing 32-byte SupportFrame, 24-byte SupportSlot, 40-byte CallTransfer
  and 16-byte InputTransfer. New records below are cold loader data, not
  additions to these activation records.
* Exact [B + RootPin schema selection](trrm-root-pin-schema-selection.md),
  TargetProfile `(2,1)`, Feature `14/1`, F0 and ReferenceSupport Body `0D`.
  The four F selections retain NativeAddress `0D`; no F/G composition follows.

Admission invokes no method, initializer, authored Cleanup or equality
operation. It creates no source grant, runtime object, root, pin or support
hold. It does not introduce source syntax, method reflection, dispatch,
foreign-call admission, new ownership rules, a reclaimable-code policy,
general generic/witness execution or a persistence codec. Only entries
already admitted to RootPin's nonforeign frame/call protocol are eligible.
Raw NativeImport/NativeCallable entries and bodyless signature anchors do not
become such entries by obtaining a descriptor.

## Owners and the missing handoff

The [specification index](../specs/README.md) reaches these semantic owners:

| Owner | Preserved responsibility |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [calls](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms) | Selected declarations, complete ordered substitutions, receiver/formal mapping and exact qualified types; source evaluation order is not parameter-map order. |
| [Call phases](../specs/language-contracts.md#31-the-normal-sequence), [saved results](../specs/language-contracts.md#32-saved-results-and-cleanup), [lifetimes](../specs/lifetimes.md#passing-and-storage-access) | Actual ownership before preconditions, borrowed backing, normal/exceptional retirement and saved logical results, including unit. A physical map cannot strengthen access or extend a lifetime. |
| [MethodSig/Param metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Required result type, optional receiver and contiguous logical formal ordinals, including zero-data unit formals. Runtime slot numbers are not metadata row identities. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities) | Resolve original selected Module/declaration identities before complete directed contract comparison; retain each original expectation and stronger relied-on fact. |
| [Requirements/admission](../specs/representation/requirements-and-admission.md), [consumer checking](../specs/representation/reader-trust.md) | Exact supported inputs, bounded references and binding before use; producer-published body/code truth does not require consumer semantic re-proof. |
| [Native lifetime](../specs/native.md#runtime-and-code-lifetime), [JIT](../specs/jit.md#replacement-and-code-lifetime), [Windows deployment](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support) | Actual code/helper/unwind publication, original origins and no freeing while reachable obligations survive. No existing public method-descriptor factory or early-unload protocol is selected. |
| [Persistence taxonomy](../specs/representation/persistence.md) | Standard semantic interchange, private saved state and native target binding remain distinct. |

The [RootPin runtime candidate](reference-root-pin-runtime.md) requires an
opaque `LoadedMethod*` containing complete signature/binding and the
receiver/formal-to-entry-slot map. `SupportFrame.entryMethod` and
`CallTransfer.target` consume it; none of the twelve operations creates it.
The [loaded-type proposal](reference-root-pin-loaded-type-descriptors.md#schema-and-integration-consequences)
explicitly supplements only type admission, not this method contract.

In the current [provider source](../../sources/Terrum.RootPin.Runtime/RootPinLoadedTypesV2.cpp),
`RootPinLoadedMethodImpl` stores its domain, candidate loader key, result type,
entry-slot count, ordered support-input map and retained origins. The
provisional `TerrumRootPinAdmitLoadedMethodsV1` export now performs
provider-side structural/type/origin validation, exact interning, conflict
rejection and transactional publication; the former test-only method factory
has been removed. The [native probe](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/RootPinLoadedTypesProbe.c)
checks provider admission, map/profile rejection, borrowed receiver mapping,
call/frame transfer, distinct keys for equivalent maps and injected
origin-retain rollback. It does **not**
derive or authenticate the loader's complete method identity, prove binding
expectations, reconstruct keys from Standard/private artifacts or resolve
JIT/AOT provider lifetime. Those remain compiler/loader gates, not provider
claims.

## Alternatives

| Candidate | Assessment |
|---|---|
| Revise loaded-type V2 into a combined type/method batch | Coherent as an explicit replacement, not an extension guessed from V2 bytes. It could atomically publish type and method data, but these method records need only already admitted types; type graphs have no method-descriptor backedge. Adding method counts, ranges and outputs to every type submission provides no required recursion or execution benefit. |
| **Separate same-provider method export; recommended** | Leaves type V2 and every operation offset unchanged; reuses the domain's loader identity and origin-lease machinery. Type admission precedes method admission; neither transaction independently publishes executable entries. No extra mutator operation or type-schema revision is needed. |
| Promote the test factory unchanged | Omits a complete trusted identity subject, slot-count/receiver checks, exact repeat admission, transactional batches and origin retention. Renaming a test export cannot supply those contracts. |

Unpublished draft assignments are revisable; compatibility is not the reason
to choose a separate export. This is the smaller coherent interface for the
acyclic type-before-method dependency. The user-selected operation-table
extent remains fixed for this decision.

## Loader-owned method identity

One canonical loader authority for D serves all attached AOT images and JIT
code, as in [loaded-type identity admission](reference-root-pin-loaded-type-descriptors.md#identity-inputs-and-the-loader-authority).
An image supplies semantic declarations and target-binding descriptions,
not trusted keys. Only that loader assigns `methodKey` after admission and
binding. For each key it retains the full equality subject:

1. The original selected provider Module and declaration, their unambiguous
   immutable revision/origin and source-unit identity where applicable;
   callable kind, selected owner and receiver role are preserved. An Import
   stub, export alias, row number or intermediate forwarding image is not
   a new method identity.
2. The complete closed owner/method type and constant substitutions, selected
   witness/associated bindings where independently supported, every ordered
   logical formal and mode, exact receiver/result types and qualifications,
   and the complete selected declaration contract. This includes applicable
   caller-unsafety, effects, argument formation, output states, dependencies,
   transfer/cleanup phases and target/runtime obligations.
3. The actual admitted native entry-publication instance for that closed
   method under TargetProfile `(2,1)`: its code/unwind origin and generation,
   entry variant and any independently admitted adapter identity. One such
   instance has one immutable generated-frame contract. Code regeneration
   creates a new entry instance; it cannot change the old instance's map.

The last component distinguishes different generated frames for one semantic
declaration, without treating frame layout as a new source declaration.
It is a loader-owned publication identity, **not a raw code address**.
Folded code at one address can still serve distinct method subjects; JIT
and AOT copies with different native entry instances need distinct descriptors.
Two attachments of the same selected entry instance share one descriptor.
Code sharing must preserve the protocol's distinct entry tokens; otherwise
decline that sharing, rather than invent a hidden context argument.

`methodKey` is a nonzero, monotonically assigned `u64` symbol in D's method
namespace, never reused. Full subject equality, not a hash, name, layout or
consumer-provided integer, determines interning. Zero is invalid; assigning
the last available key must not wrap or reuse keys. Key exhaustion declines
admission. Method, type and origin namespaces are distinct even when numbers
coincide. The loader's full-subject map is retained with the descriptors;
the provider record plus that retained subject constitute the logical
complete LoadedMethod descriptor.

Each import/binding edge is checked against its **original** expectation,
including actual stronger provider facts used by generated code. Different
satisfied expectations can reach the same selected entry and descriptor;
they are not merged into a weakest expectation or used to mint different
nominal methods. The loader retains those obligations separately from the
shared selected-method subject. Complete comparison still follows
[the directed binding relation](../specs/representation/binding-and-replacement.md#4-complete-comparison-graphs).

Compatible replacement for a new load can reuse consumer bytes only after
the ordinary semantic and actual native ABI/fixup checks. A changed selected
revision, load generation or entry instance receives a fresh subject/key;
old descriptors are neither retargeted nor mutated. Repacking or aliasing
the same original entry preserves its identity. This selects no live patching.

After these loader checks, a provider-owned pointer is an **already-admitted
identity consistency token**. Comparing `CallTransfer.target` with
`SupportFrame.entryMethod` detects a mismatched actual entry; it does not
perform declaration resolution, prove contract compatibility or authorize a
call. A provider must first establish pointer membership in D without
dereferencing a fabricated pointer. The provider does not reparse TRRM or
prove that a loader supplied the truthful full subject; a fabricated subject
is a loader contract violation, not a supported arbitrary-integer API.

## Exact supplementary interface

The same admitted `terrum-root-pin-runtime.dll` instance that supplied D and
the operation table exports:

```text
u32 __cdecl TerrumRootPinAdmitLoadedMethodsV1(
    Domain* D, const RootPinMethodBatchV1* batch,
    LoadedMethod** vacantOutMethods, u32 outCount)
```

This is **Windows x64 machine interface notation, not Terrum source or an
authored NativeImport**. The four arguments occupy RCX, RDX, R8 and the low
32 bits of R9; status uses the low 32 bits of RAX. The platform stack,
shadow-space, preservation and unwind rules apply without hidden arguments.
The export may allocate metadata and coordinate with D, but exposes no native
unwind, source failure, authored execution or source safepoint.
It is not callable from DllMain, under the Windows loader lock or inside a
frame/call/retain-release no-safepoint commit interval.

Resolve and explicitly support `windows-x64-root-pin-loaded-methods/1`
from that **same provider instance**, not a matching DLL name or a second
runtime. Missing export or required support rejects before affected use.
Do not probe with another D, try the test factory, infer support from type V2
or widen the V1 operation table.

### Records and extents

Records are little-endian, naturally aligned to eight bytes with eight-byte
pointers. There are no flexible tails, implicit version-selected strides or
unnamed padding bytes. `RootPinMethodBatchV1` is **72 bytes/alignment 8**:

```text
u32 abiRevision=1, u32 byteSize=72
u32 targetProfileId=2, u32 targetProfileRevision=1
u32 methodCount, u32 inputCount, u32 originCount, u32 originUseCount
const MethodNodeV1* methods
const MethodInputV1* inputs
const TypeOriginV1* origins
const u32* originUses
u32 flags=0, u32 reserved=0
```

Offsets are `0..15` for revision/extent/profile, `16..31` for counts,
`32/40/48/56` for pointers and `64/68` for flags/reserved.
`MethodNodeV1` is **56 bytes/alignment 8**:

```text
u64 methodKey
LoadedType* resultType
u32 formalCount, u32 entrySlotCount
u32 inputFirst, u32 inputCount
u32 originFirst, u32 originCount
u32 declarationOrigin, u32 entryOrigin
u32 flags, u32 reserved=0
```

Its offsets are `0/8`, `16/20`, `24/28`, `32/36`, `40/44` and `48/52`.
`flags` permits only `0x00000001=HasReceiver`; absence means no receiver.
`formalCount` counts all explicit logical parameters, including scalar,
unit and non-support positions; a receiver or hidden result is not a formal.
`entrySlotCount` is the exact total slot count of this entry's generated
SupportFrame, including locals, spills and saved results, not just inputs.
Zero is allowed only when no slot is required; a support-bearing result
requires at least one saved-result-capable slot.

`MethodInputV1` is **24 bytes/alignment 8**:

```text
u32 formalOrdinal, u32 entrySlot
LoadedType* completeType
u8 sourceRole, u8 handoffKind, u16 reserved16=0
u32 reserved32=0
```

Offsets are `0/4/8/16/17/18/20`. `sourceRole` is exactly `0=explicit formal`
or `1=receiver`; a receiver requires ordinal zero and HasReceiver.
`handoffKind` is exactly `0=borrowed scan residence` or
`1=owned hold transfer`. It records the already checked passing contract,
not an authored ownership annotation or permission to change that contract.

Reuse unchanged the **40-byte/alignment-8 `TypeOriginV1`** record and its
origin-key/cookie/retain/release/kind semantics from the
[loaded-type records](reference-root-pin-loaded-type-descriptors.md#input-records).
`originUses` remains a four-byte-aligned array of zero-based `u32` origin
indexes. It is not a method/type reference namespace.

All counts/indexes are `u32`; no 24-bit TRRM row limit or register-staging
limit is imported. Empty ranges are `(0,0)`. Nonempty ranges use checked
first/count arithmetic. No field here assigns an all-ones sentinel:
ordinary indexes must be less than their own counts.

### Result and entry-map meaning

`resultType` is required, non-null and an already admitted **Value**
LoadedType in D for the exact complete declared result, even for scalar or
unit results. Ordinary no-payload return means the inhabited `void` unit
descriptor with its admitted zero-data layout, **not null, erased T or a
new no-result type**. A DoesNotReturn promise retains its declared result
type and existing behavior; it does not substitute unit.

Support-bearing classification follows the admitted type graph and this
entry's selected passing representation, not byte width or current bits.
Optional None still has a support-bearing type when Some can carry support;
an external stable descriptor with zero current support does not change type
classification. FixedArray with zero count has no element residence under
the existing traversal rules. No unsupported layout becomes rootless.

Each receiver/formal materialized in the callee as a support-bearing value
residence occurs exactly once in the node's input range:

* The receiver, if it needs such a residence, comes first. Explicit formals
  follow in strictly increasing **original logical ordinal** order.
  Scalar/unit/non-support gaps do not renumber the remaining formals.
* `completeType` is the same-domain Value descriptor for that exact selected
  input type, including every qualification. `formalOrdinal < formalCount`
  for explicit formals and `entrySlot < entrySlotCount` for every row.
  Duplicate `(sourceRole,formalOrdinal)` or duplicate entry slots reject.
  Slot order itself is not sorted or rewritten: it also governs frame drain.
* Kind 1 denotes the one staged owned hold that must transfer at entry.
  Kind 0 denotes an already borrowed input/receiver whose supporting holder
  remains responsible in the caller; its callee slot is scan-only.
  The selected declaration/passing contract, not a per-call choice, fixes
  this distinction. Optional absence changes no mapping/responsibility.
* Scalars, unit, rootless aggregates, raw pointers and independently admitted
  one-pointer call-extent reference projections have no support-map row.
  The latter retain their caller-held backing and are not stored/result
  two-word descriptors. All these positions still belong to the full closed
  method subject. A borrowed support-bearing descriptor materialized in the
  callee does require its kind-0 row; borrowed is not synonymous with rootless.
* Extra alias/local slots are generated-frame facts, not duplicate inputs;
  they cannot acquire a second release responsibility for one hold.

An input range is exclusively owned by one node and every submitted input
row has exactly one such owner. Repeated method nodes must supply separate
equivalent ranges. Each method's origin-use range is likewise owned; its
entries are unique and ordered by increasing resolved `originKey`, not
input-table position. All origin-use entries have an owner. Repeated origin
records are permitted only with identical kind/cookie/callback bindings;
even otherwise unused origin records must be structurally valid.

`declarationOrigin` names a kind-1 semantic/layout origin containing the
selected declaration/binding subject. `entryOrigin` names the kind-2 actual
code/unwind origin for the selected entry instance. Both must occur in that
method's origin-use range. That range covers the complete required closure:
original semantic/provider data, generated entry/prolog/epilog/exception paths,
unwind backing, adapters and transitive helpers/runtime/allocator origins
where used. Existing loaded-type origins remain retained independently.
A forwarding image cannot stand in for the actual leaf provider.
Conservative whole-image leases are permitted, but the canonical origin
closure for one entry instance is fixed at admission.

## Checks, interning and atomic publication

The loader first safely consumes the exact selected schema, resolves each
original import and closes the required contract graph. It verifies receiver
presence, all logical positions/types, supported passing/result classification,
frame-map projection and required origins against admitted declarations and
target-binding facts. It admits required types through **V2 first**.
Unavailable declaration/ownership/body/frame codecs or required representations
decline the affected route; test records, source names or private offsets
cannot manufacture missing facts.

The producer remains responsible for truthful body, lowering and unwind facts.
Checking the map against selected declarations and published generated-frame
data does not require the loader/provider to disassemble and semantically
re-prove machine code or producer-published body liveness. Source verification
still checks authored source, and an optional audit claims only its own scope.

The loader serializes key assignment and method admission for D, using the
same coordination as loaded-type admission. The provider performs at least:

1. Exact revision/profile support and record extent checks; closed flags,
   roles/kinds and reserved zeros; nonzero keys and method count;
   `outCount == methodCount`. Output words are initially vacant.
2. Checked Windows x64 `nuint` multiplication/addition for each entire span
   (`methodCount*56`, `inputCount*24`, `originCount*40`,
   `originUseCount*4`, `outCount*8`) and generated slot extent
   (`entrySlotCount*24`). Validate alignment, every range/owner/reference,
   formal/slot bound and canonical map/origin ordering before publication.
   Zero-count arrays have null pointers; nonempty arrays have valid storage.
3. Membership checks before dereferencing every LoadedType pointer; it must
   be an admitted Value of D, not a payload role, foreign/stale descriptor,
   same-sized substitute or forged address. Check the graph-derived support
   property for mapped inputs, result-slot capacity and HasReceiver rules.
   Check every submitted node/input/origin/use, not just a reached prefix.
4. Exact origin record validation and consistency with D's **shared**
   type/method origin registry. Non-null prepared callbacks obey the existing
   nonfailing, nonthrowing, nonsuspending, no-source-code/no-safepoint/no-reentry
   lease contract. The caller holds their full backing through the call.
5. Normalize all references to admitted complete type and origin identities,
   not caller record addresses or batch indexes. Intern by
   `(D, methodKey, TargetProfile 2/1)`. Compare result, formal/slot counts,
   flags, ordered `(sourceRole,formalOrdinal,entrySlot,completeType,handoffKind)`
   tuples, distinguished origins and full normalized origin closure.
   Identical repeats, including duplicate nodes within one batch, return the
   **same LoadedMethod pointer**. Any conflict
   for that key rejects the whole batch; no in-place update or union of maps
   or origins is allowed. Distinct subjects never merge by signature/layout.
6. Prepare immutable provider-owned descriptors/maps, index capacity and
   all new origin holds before a no-failure commit. Retain each newly needed
   canonical origin once in the shared domain lease set. Publish the entire
   method set and fill all output pointers in input-node order only after
   all fallible work succeeds. Roll back scratch/provisional origin holds
   on failure; existing methods/types/leases and source values are unchanged.

The caller supplies readable immutable input spans and a writable initially
zero output span, all properly aligned through return. Outputs are disjoint
from every input and provider-owned storage. This is not arbitrary-address
validation or a sandbox API. Invalid machine memory, a foreign/stale D,
nonvacant output, failed/unwinding callback or fabricated loader identity
violates the runtime call contract; do not overwrite live outputs or report
successful absence. Within valid caller spans, malformed records/ranges
produce the rejection below without partial publication.

The provider copies nodes, maps and origin uses; it retains no pointer into
caller batch arrays or activation storage. After return those arrays may
be released. An admission transaction is atomic for methods and its new
origin holds, **not** a rollback of earlier successful type admission.
The loader publishes no affected initializer or callable entry until its
whole type/method/provider closure and ordinary code/fixup/unwind publication
have succeeded. A later load failure can leave harmless domain-lifetime
type metadata, never a partially executable method closure.

### Status and failure behavior

| Status | Meaning |
|---:|---|
| `0` | Entire method batch committed; every output is a non-null canonical descriptor. |
| `1` | Metadata/resource capacity exhausted; nothing new published. |
| `2` | Malformed records/ranges or inconsistent method/type/map/origin binding. |
| `3` | Unsupported supplementary revision, target or required representation/capability. |

For valid initially vacant output spans, statuses 1–3 leave **every output
zero**, including outputs corresponding to already interned methods. Unknown
status rejects the provider contract. Bad extent/reserved/index/slot data and
conflicting re-admission are status 2; exceeding implementation capacity for
otherwise valid data is status 1. No truncation, skipped suffix, arbitrary
depth cutoff or success with null/partial outputs is permitted.
Semantic/binding preflight and loader-key exhaustion may decline before
calling the export. These are loader outcomes, not root/pin acquisition
statuses, source OutOfMemoryException or a new runtime-failure policy.

## Use by frames and call transfers

The existing operation signatures and state values remain unchanged.
Their production consumer checks use this admitted map:

* Lowering/loader fixups supply the descriptor for the **actual selected
  entry** to caller target slots and that entry's frame. EnterFrame checks
  membership, exact `frame.slotCount == entrySlotCount` and mapped slot
  types/states before registration/transfer. No equality of code addresses
  or signature shapes can consume a different entry's pending call.
* BeginCall's InputTransfer array is exactly the ordered kind-1 subset of
  the method map, with one distinct live/owned caller staging slot per row
  and the exact type. Missing, extra, repeated, reordered or borrowed-kind
  transfers are contract violations, even if an optional value is None.
  This completeness is a fixed map check, not an inferred per-call policy.
* At EnterFrame, kind-1 callee residences already contain the staged bits
  but are vacant/unowned. Transfer makes them live/owned and clears caller
  responsibility, without retain/release. Kind-0 residences contain their
  borrowed incoming descriptors and are live with `ownsHold=0`; they never
  drain a caller-owned hold. Supporting backing remains the checked
  caller's obligation. All existing residence bounds/alignment, state and
  no-safepoint requirements continue to apply.
* A support-bearing declared result requires the exact typed vacant private
  prospective result slot and the existing ReturnSupport handoff, including
  an optional None result. A scalar/unit/non-support result requires null
  prospectiveResult and no ReturnSupport call; ordinary machine/logical
  result behavior remains unchanged. ReturnSupport accepts only a live/owned
  saved-result slot of the exact result type in the matching callee frame.
* Preserve states `0=vacant`, `1=prepared`, `2=entered`,
  `3=result prepared`, `4=closed` and FinishCall's `0=exceptional/abandoned`,
  `1=normal`. Before entry, abandonment retires prepared caller inputs;
  after entry, callee retirement/unwind owns them. A prepared result that
  fails before normal publication is retired exactly once. No admission
  lifetime lease adds an invisible input/result hold or delays the last pin.

Reentrant calls, partial prologs, native faults, epilogs, unwind and equivalent
tail-call lowering retain the existing protocol. An unrelated callback/helper
cannot consume a pending plan. An admitted host crossing uses the same
nonforeign protocol; this proposal creates no new host or foreign boundary.
Invalid frame/call use after admission is a runtime-contract violation, not
a recoverable method-registration retry.

## Lifetime and retained origins

Use the [loaded-type domain-lifetime choice](reference-root-pin-loaded-type-descriptors.md#lifetime-and-retained-retirement-code)
also for methods. No per-method release export, weak intern entry, last-call
reclamation or image-local descriptor is introduced. Keys/descriptor addresses
are never reused in D. The loader subject map and shared retained origin set
outlive batch arrays, module attachments, JIT handles and every frame/call
that can name the descriptors.

For the selected process-wide singleton D, retain required semantic,
entry/code, transitive helpers, callback/cookie and unwind backing until
process reclamation or independently established **whole-domain quiescent
shutdown**. A per-image unload/JIT disposal request cannot invalidate them.
Closing the last call, clearing a method fixup or having no current frame is
not such proof. Leases may conservatively retain whole images but cannot
replace the actual allocator/provider or free registered unwind backing.

Orderly shutdown must have no admitted invocations, frames, pending calls,
cells/objects, deferred/in-progress Cleanup or remaining code/unwind users.
Origin release callbacks only drop leases or queue disposal and cannot unload
their own backing while running. Actual deregistration/unload follows after
callback return; failed deregistration prevents freeing that backing. Without
the existing quiescence proof, retain to process reclamation. This assigns
neither a shutdown API nor a promise to execute source Cleanup at process exit.

Metadata/code retention creates no collector root or source ownership,
retains no argument object merely because its type occurs in a signature,
and does not extend borrowed backing, guards or logical pin endpoints.

## Integration and remaining gates

On adoption, the Windows target/deployment/runtime owners would add the
supplementary export, exact records, identity authority, consumer checks and
domain-lifetime origins together. Complete-type, call, lifetime, metadata,
binding and producer-trust chapters remain their semantic owners. This
proposal replaces no accepted source rule and adds no source method grammar.

Compiler integration must preserve selected callable identities, closed
substitutions, original expectations, passing classification and complete
types through shared bound traversal/rewriting, verification and lowering.
Generated frame maps and native entry origins are target binding data derived
after selection; rewriting/cloning code rebuilds affected maps/fixups and
retains dependency-valid cleanup order. Required input/result and unwind
contracts must survive separate compilation, not a sample-name recognizer.

Standard `.trrm` continues to serialize only selected schema-owned semantic
declarations/contracts/operations. **Never serialize trusted method keys,
LoadedMethod/LoadedType pointers, domain addresses, leases or callback
addresses into TRRM.** Private semantic/native/set/graph routes likewise
reconstruct fresh trusted admission data after their independent binding
checks; a saved integer, pointer or accepted bit is not reusable authorization.
An explicitly supported native image can retain relocatable descriptions and
vacant fixup slots, not pre-admitted pointers. Missing complete source-free
contracts/codecs reject that route before execution.

There are no new Feature, TargetProfile, opcode, Body kind, private revision
or TRRM byte-schema assignments here. Supplementary runtime support is an
explicit prospective agreement, not inferred from equal draft version bytes, loaded
exports or Feature rows. Preserve the [format lifecycle](../specs/representation/format-versioning.md);
this document does not mutate an already agreed draft snapshot.

No admission/lifetime policy choice remains hidden in this recommendation.
Formal adoption remains unapproved; provider-side implementation and
qualification are partial, while trusted loader identity, consumer resolution,
source/Standard integration and end-to-end execution remain gates. The
independent allocation/construction boundary, unsupported representation/
persistence capabilities and broader runtime work remain outside this
proposal. Provisionally following this direction does not make it approved
or establish complete production support.

## Focused acceptance cases

These are the complete required acceptance cases. The provider-only subset
already exercised is described in the current conformance-gap inventory; none
of these rows is closed until its full stated outcome and integration route is
covered. Native cases use an independently admitted host/object boundary;
they do not demonstrate general source GC construction.

| Case | Required outcome |
|---|---|
| RLM-01 Identity and re-admission | Two unrelated methods with equal names/signatures/layouts stay distinct; repeated requests for the same selected native entry, including a diamond through aliases, return one pointer. Duplicate identical nodes in one batch share that pointer. Hash/code-address equality never merges subjects. |
| RLM-02 Complete signatures | Distinguish original Module/declaration, owner, closed type/constant substitutions, qualifications, receiver/modes and complete output/dependency/effect contracts. Reject bodyless anchors, unbound expectations, unavailable required codecs and fabricated source authority before use. |
| RLM-03 Input map | Mix a support-bearing borrowed receiver, transferred descriptors, a borrowed descriptor, scalar/unit gaps and a rootless projected reference. Preserve original ordinals and receiver-first order; require precisely the kind-1 call subset and unique bounded slots. |
| RLM-04 Result/unit | Admit exact scalar and unit result descriptors with no prospective slot; a unit formal still counts. Transfer Some and None support-bearing results without extra retain. Reject null/payload/wrong-qualified result types, missing saved-slot capacity, ReturnSupport for a rootless result and normal publication without the required handoff. Preserve a nonreturning declaration's actual result type. |
| RLM-05 Structural rejection | Reject bad byteSize, flags/reserved bytes, role/kind, absent receiver, duplicate/reordered logical inputs, duplicate slots, non-support map types, shared/unowned ranges, bad origin kind/reference and conflicting origin callbacks. Reject wrong ABI/profile with unsupported status, not another decoder or fallback. |
| RLM-06 Bounds and capacities | Exercise zero-formal/zero-input/zero-slot methods where valid, first/last legal ordinal and slot, last assignable key, large finite maps and checked range/span endpoints. Reject zero methods, off-by-one counts, address wrap, misalignment and sentinel-like out-of-range references. Valid capacity/key exhaustion declines without wrap, truncation or accepting an unchecked suffix. |
| RLM-07 Transactionality | Mix new and existing methods, then inject failure during preparation, after provisional origin retains and immediately before commit; callbacks themselves retain their nonfailure contract. All output words stay zero, new methods/leases roll back, prior methods/types remain intact and no authored code ran. Retry succeeds; exact conflicting re-admission rejects the entire batch. |
| RLM-08 Provider/domain | Resolve only the same-provider export alongside the unchanged 104/112-byte interface and exact loaded-type V2. Reject a missing export or unsupported capability before execution. Foreign D, forged method/type pointers and nonvacant outputs cannot become admitted identities or successful absence. |
| RLM-09 Replacement and native variants | Independently bind producer/consumer expectations, including broad and stricter views. Accept compatible replacement only after retained semantic/native obligations pass; assign fresh descriptors for changed revision/generation/entry maps. Preserve old live entries; reject in-place conflicts and stale fixed ABI facts. JIT/AOT entry variants are distinct even for one semantic method. |
| RLM-10 Handoff and unwind | Exercise prepared abandonment, complete entry, callee early parameter-pin retirement, borrowed inputs, nested calls, fault before entry commit, normal saved result and exceptional epilog settlement. Release each responsible hold once; a mismatched equal-signature entry or unrelated callback cannot consume a pending plan. |
| RLM-11 Origins and lifetime | Free batch arrays and detach intermediate images/JIT handles after success; descriptors and the actual leaf code/helper/unwind origins remain valid. Reusing an already retained origin adds no duplicate domain lease. Metadata retention does not keep source objects/pins alive. No early per-method unload; failed unwind deregistration prevents freeing backing at whole-domain shutdown. |
| RLM-12 Persistence and rewriting | Independently load selected standard/private inputs without source, reconstruct trusted identities and remap method/input/origin tables correctly. Rebuild native maps after code transformation; preserve full original expectations. Reject absent contract carriers, serialized trusted keys/pointers, wrong exact schema and unsupported F/G packing instead of guessing a schema or using the test factory. |
