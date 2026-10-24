# Windows x64 boundary-contained failure bridge

## 1. Profile and prerequisites

Logical `windows-x64-failure-bridge/1` refines
[foreign-failure-bridge/1](../runtime/foreign-failure-bridge.md).
It requires the complete selected [Windows x64 native profile](windows-x64.md),
local checked-failure support and the actual admitted domain/adapters below.
It is an optional special crossing, not a revision of the ordinary
non-unwinding call signature.

All wire calls use the existing standard Windows x64 procedure-call rules:
shared integer/floating physical ordinals, shadow space, stack alignment,
exact storage and control/nonvolatile preservation.
The new packet is a fixed rootless two-word record, not a C++ nontrivial
class, managed reference, stored `ref<T>` descriptor or native unwinder
exception object.
No new target number, custom multi-register ABI or `.trrm` allocation is
introduced.

## 2. Packet and domain header

The following names identify the physical structures for this profile.
They do not register a same-spelled Terrum source type.
All bytes are little endian, with the exact Windows scalar widths.

| FailurePacket field | Offset | Storage/alignment | Meaning |
|---|---:|---:|---|
| Domain | 0 | 8/8 | Pointer to the exact admitted FailureDomainV1 header |
| State | 8 | 8/8 | Opaque `nuint` state interpreted only by that domain |

FailurePacket size is 16, alignment is 8 and array stride is 16.
It is an affine complete value with one domain-owned responsibility.
Its machine fields are rootless, but payload/root/code obligations remain
those of the exact domain. An ABI copy transfers that responsibility only
under the admitted ownership contract; it does not create Copy semantics or
whole-record atomicity.

| FailureDomainV1 field | Offset | Storage/alignment | Required value |
|---|---:|---:|---|
| Version | 0 | 4/4 | `uint32` value 1 |
| ByteSize | 4 | 4/4 | `uint32` value 16 |
| Release | 8 | 8/8 | Non-null code pointer to the admitted domain release |

The public header size/alignment is 16/8. Its backing is readable and stable
for the full retained lifetime. Private domain state may exist elsewhere;
it is not a public header extension or an alternate ByteSize.
Other versions, smaller/larger public sizes, missing code or incompatible
declared support decline before affected use.
Version/size bytes do not prove the pointer, body or lifetime valid.

A live packet has a non-null Domain belonging to the bridge's admitted
origin-domain set. Establish that authority and the known header extent
before reading or calling through it; an arbitrary native pointer cannot
authorize probing a descriptor.
State zero is permitted if that exact domain declares it valid, for example
for an immediate/static failure. State need not be an allocation pointer.
The domain retains every validity/identity/provenance commitment for all
accepted states.

Vacant failure storage has no readable packet and no domain release duty.
It has no required all-zero image. Reading Domain to decide whether an
initialization destination is live is invalid; use the declared outcome and
checked initialization state.
These structures are machine-call data, not serialized native package,
`.trrm` metadata, a portable wire identity or persisted pointer values.

### Domain release

Release has the exact ordinary native prototype `void Release(nuint state)`.
The opaque state is one integer-carrier parameter in RCX; the result has no
payload. The selected domain header retains the exact function definition,
actual state contract, transitive cleanup, allocator/runtime origin and code
lifetime. Calling an unrelated same-signature address is not compatible.

Retirement consumes the packet responsibility once and invokes Release once
with its original State while the domain and its code remain valid.
Supporting storage for a by-value packet follows the ordinary 16-byte record
indirect-copy rules, not a second owning packet.
Retirement is nonthrowing and nonsuspending on normal and unwind paths.
An internal native destructor that can escape, pending async settlement or
an unsupported release affinity is not admitted.
Reported/supervised native release failure follows the declared domain
contract; a violated promise takes the explicit fatal breach path rather
than returning success or retrying against possibly released/reused state.

## 3. Wire entry and normal outcomes

Each special entry has an exact ordinary C-style prototype. The descriptive
shape is:

```text
bool Entry(P0 p0, ..., Result* result, FailurePacket* failure)
```

The ordinary arguments retain their matched order and source semantics.
The designated result pointer, when present, follows those arguments; the
failure pointer is last. Native pointer parameters implement the specifically
declared initialization destinations, not general stored-reference transport.
An instance receiver is an explicitly matched ordinary argument.
The return is the one-byte Windows `bool`, not Win32 BOOL/int32.
Only its required low-byte carrier bits matter; unused upper bits are
unspecified.

For logical `void` result, omit the result-pointer payload entirely:

```text
bool Entry(P0 p0, ..., FailurePacket* failure)
```

Retain the complete logical unit result for binding and effects.
Do not manufacture a positive-extent `void` result place.
Other designated result destinations must have positive-extent fixed
rootless storage with the actual alignment/validity/ownership contract.
An extended scalar without a formal foreign primitive equivalent uses an
explicit matched bit record/buffer, not an invented direct native primitive.

Both destinations begin reserved/vacant and are stable, actually aligned,
exactly sized and disjoint from each other and incompatible live sources.
The admitted declaration establishes those requirements before invocation;
the implementation need not insert redundant internal checks where they
are already proved. Read/write aliases among ordinary arguments retain their
declared rules; this is not an automatic NoAlias guarantee for every input.

| Normal status | Primary result | Failure packet |
|---|---|---|
| true | Complete and live when non-unit | Vacant |
| false | Vacant | Complete and live |

For unit success, the primary result is the logical initialized unit without
payload storage. For unit failure, it is not a successful result.
There is no third normal status, default result on false, live failure on
true or vacant failure on false.
Status/output correlation is part of the bridge ABI contract.
Arbitrary caller destinations are not initialized by examining their bytes.

The entry contains its own supported escaping failures and returns normally
with these correlations. It has no hidden language-exception argument,
language unwinder crossing or automatic host-exception equivalence.
Its raw native contract returns through the packet protocol; the source-facing
receiving proxy retains its actual language-failure behavior.

The Boolean result has no hidden result pointer. Explicit result/failure
pointers occupy ordinary integer-carrier positions after the original
arguments, sharing ordinals with XMM floating arguments.
For example, `Entry(int32 a, float64 b, Result* result, FailurePacket* failure)`
uses RCX, XMM1, R8 and R9 respectively.
With four original arguments, result and failure are fifth/sixth parameters,
at callee-entry RSP+40 and RSP+48. Caller home area and alignment obligations
are unchanged.

## 4. Complete capture and publication

The origin adapter stages the designated primary result privately through
the source operation's complete normal cleanup/postcondition phase.
On contained failure it retires private partial/saved result responsibility
under the actual type/allocator origin, establishes a complete owned packet,
and publishes only the false/failure outcome.
The failure state does not borrow that staging or an exited frame.
No former moved input or released allocation becomes live again.

Other writable arguments and user-declared outputs preserve their actual
status/availability contract, including permitted live failure outputs.
The bridge cannot claim rollback of mutations or a consumed input merely
because the designated primary result is vacant.

Capture includes every actual failure-payload, provenance, cause, root/handle,
allocator and helper lifetime. It can use declared private domain storage
or immediate state; no allocation is imposed on the success path.
Failure-path allocation is not required either, but an allocating provider
must publish its resource/fatal capture contract.
The caller cannot assume that every native capture is allocation-free or
that an internal `exception_ptr` preserves object-address identity.

If capture/replacement cannot establish a valid packet, the selected explicit
fatal action does not return and does not unwind through this boundary.
Do not publish false with a vacant packet, replace the original cause with
an unreported allocation failure or convert an arbitrary hardware/SEH fault
to an ordinary result. The profile assigns no fatal process-status value.

## 5. Receiving, forwarding and native re-raise

The receiving adapter checks the normal wire correlation and the exact
admitted domain before consuming the packet into its selected local failure
mapping. Header validation alone is not a dynamic type check.
Mapping and local dispatch follow the
[abstract bridge](../runtime/foreign-failure-bridge.md#5-typed-mapping-propagation-and-causal-replacement).
The [local exception capability](../language/local-failures.md) and chosen
catch input mode remain required local contracts, not pointer-header facts.
They do not add a hidden wire parameter, Copy promise or universal catch value.
Missing mapping/local failure support declines before affected execution,
not after guessing a universal catchable object.

Forwarding moves the same domain/state responsibility and preserves its
origin, provenance and actual dependencies. It need not recapture the
foreign exception, allocate another wrapper or change domain identity.
If a new source failure replaces it, the earlier owner remains retained by
the causal holder until that holder's retirement.

A selected originating-runtime re-raise helper has its own exact signature,
failure effect, domain/state authority, ownership/borrow and retained support.
It is not the Release member and is not added to the common header.
The wire call carrying the packet has already returned before re-raise.
Any borrow ends before the packet retires; throwing while that borrowed
state remains in use cannot implicitly consume its separate owner.
Raising in C++/another native runtime does not raise through active Terrum
frames.

## 6. Synchronous callbacks and nested entries

A matched callback uses the same Boolean result/failure relation and ordinary
native call convention. It may have an explicitly matched opaque context
argument before its ordinary inputs; this assigns no closure representation.
The context has the actual stable backing/generation and source capture/code
lifetime for the whole synchronous call.

The callback contains Terrum propagation before returning normally.
Only after that return may the native adapter forward the packet, stop under
its declared failure result or raise within its own admitted native runtime.
The native caller's propagation/stop contract must prevent accidental further
callbacks or default-value continuation after failure.

Nested/reentrant entries have distinct reserved outputs and responsible
packet holders. No ambient TLS error slot, callback-success default or packet
overwrite supplies their state.
Native interfaces lacking this exact failure channel need another complete,
explicit callback adapter contract; this profile supplies no guessed channel.
Deferred/async callbacks, general closures, COM and Task transport remain
outside this profile.

## 7. Preservation, lifetime, admission and replacement

Every wire entry and Release preserves the standard register/control state.
Native internal unwind obeys its selected native runtime; Terrum internal
dispatch preserves incremental post-inner-cleanup source selection.
Accurate Windows unwind/probing/registration data remains necessary for
supported local unwinding, but does not enable a shared exception-object ABI.
No foreign unwind/longjmp or hardware-fault handler skips Terrum frames.

Retain each actual domain instance/generation, header, backing, code,
allocator, root/handle and mapping/re-raise helper through all packet, cause,
borrow, callback and active release uses. Release completion can discharge
only its actual obligations. An image cannot unload while its descriptor or
code can still be observed or called.
An address reused by a later domain generation does not make an old packet
compatible; the old generation cannot retire with that obligation live.

Binding compares the complete selected source/wire signatures, profile and
domain versions, outcome states, effects, transfers, mapping/capture/
provenance/release contracts and retained dependencies.
Adding an ordinary hidden failure parameter, changing true/false correlation,
flattening a cause, introducing capture/cleanup or weakening a relied-on
guarantee is not compatible replacement because export names still match.

Before independent use, the consumer needs complete physical records for
every retained source declaration/value/operation and local failure feature.
This profile fixes native machine-call structures only. It allocates no
Feature/target ID, Attr, TypeSpec extension, exception-body region, packet
codec or callback metadata in `trrm/1-draft-register-il-1`.
Out-of-band governing rule agreement does not repair missing physical records.
Private serializers remain private.

[Required cases](../conformance/target-windows-x64-failure-bridge.md)
include independently compiled native producers/consumers and explicit
document-only Terrum/runtime obligations. A C++ fixture demonstrates its
own selected runtime and native wire, not arbitrary native exception
domains or Terrum catch/capture execution.
