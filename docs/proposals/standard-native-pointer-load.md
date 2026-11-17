# Standard typed raw-pointer loads

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This document selects a candidate for subsequent
work, not normative adoption, an allocated opcode, implemented support or
linked-JIT qualification. It changes no other proposal.

## Finding and smallest decision

Recommend one typed, nonconsuming raw-memory read, **`native.pointer.load`**:
candidate short primary **`0x1B`**, followed by destination/source register
nibbles. Its source is a complete nonoptional `ptr<T>` or `write ptr<T>`;
its result is exactly T. The source supplies the type, so no additional
TypeMap operand, Body record, authorization flag or address pseudo-type is
needed. This is a carrier for the accepted unsafe value read through `*p`,
not new source syntax or a new kind of initialization.

The missing contract is the **Standard Body operation**, not raw-pointer
source semantics or the already provisionally selected initialization
CallSite. The following evidence distinguishes it from an implementation
omission for an assigned operation:

| Checked owner or implementation | Finding |
|---|---|
| [References](../specs/language/references.md#dereference-and-member-access), [grammar](../specs/grammar-spec.md) | Raw `*p` retains explicit unsafe authorization, validity, alignment, provenance and lifetime obligations. It is not safe checked-reference dereference. |
| [Assigned short forms](../specs/il-instruction-set.md#assigned-short-forms), [machine registry](../specs/representation/trrm-1.registry.json) | `19=LoadReference` loads through an admitted **checked reference**. `1B..1F` is unassigned. A native-pointer Feature requirement does not assign a missing instruction. |
| [Place records](../specs/il-instruction-set.md#typed-operand-records), [place semantics](../specs/il-instruction-set.md#fields-elements-and-ranges) | PlaceSelection targets are Home, Field, Element and StaticField; no raw-pointer target is assigned. Internal PlaceHandles are explicitly distinct from raw pointers. `63`/`DA` therefore do not already provide this operation. |
| [Metadata TypeSpecs](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [initialization CallSite proposal](standard-trrm-initialization-destination-callsite.md) | TypeSpec `0E` describes `ref<init T>`. With T=`ptr<U>`, completion initializes the pointer **cell**, not U. Neither that type nor CallSite source-use 5 describes a raw load. |
| [NativeAddress proposal](standard-native-address-formation.md), [storage-address proposal](standard-native-storage-address-formation.md) | E9/record-0D forms a function-pointer value; EC/record-0E forms a storage address under a separate composition. Neither reads a value through an existing data pointer. |
| [Private module codec](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | `BoundNativePointerRead` has private expression tag **39**, with unsafe-scope checks and recursive address serialization/reconstruction. This is not a Standard instruction assignment. |
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [shared verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), [native lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs) | Source binding constructs `BoundNativePointerRead`; verification checks authorization, exact pointee/read type, nonconsuming eligibility and known backing. `EmitNativePointerRead` lowers the address and typed read. These mechanisms do not supply a Standard carrier. |
| [Current source emitter](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [indexed fallback](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Indexed.cs), [Standard reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs), [init-Body reconstruction](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs) | The provisional implementation now emits and consumes `0x1B` only in kind-`01` register Bodies under the exact successors above. Old identities and compact-indexed kind `08` remain unsupported; adding a private indexed case is not a Standard encoding. This implementation does not approve the proposal or establish its full conformance matrix. |

The original compact-indexed failure is not a fallback for this proposal:
kind `08` remains outside the selected schema. Current implementation
coverage and remaining gates are maintained in
[`implementation.md`](../implementation.md) and the
[conformance-gap index](../implementation/conformance-gaps.md); the broader
provider, provenance, remapping and execution matrix below remains the
acceptance target.

## Allocation alternatives

The [mixed codec](../specs/representation/registry.md#2-instruction-allocation)
has fixed first-byte lengths: `00..0F` tiny, `10..BF` two-byte short,
`C0..FF` four-byte long, with no prefixes or escapes.

| Alternative | Assessment |
|---|---|
| Widen assigned `19` into a type-directed checked/raw load | A coherent amendment, not forbidden by draft compatibility. It has the same two-byte size, but replaces the checked-only meaning with two authority regimes. Every producer, reader and verifier must distinguish those regimes anyway. Not selected. |
| Add a raw-pointer PlaceSelection target and reuse `63`/`DA` | Also requires a new contract: captured pointer target, access/state domain and permitted operations on the resulting PlaceHandle. Useful for a complete raw load/store/place family, but unnecessarily opens place formation and subsequent operations for this read-only gap. |
| **Assign short `1B` with the existing register-value carrier** | Recommended. The complete source type already determines T; a distinct operation preserves checked/raw separation at the same width as `19`. No record or new map is needed. |
| Extend the compact-indexed implementation-kind `08` operation table | Not selected. The normative Body chapter selects kind `01` instruction streams and kinds `02..05` compact descriptors; kind `08` is not an assigned Standard Body form. Do not add a parallel operation table or fallback there. |
| New long memory family or typed load record | Can carry independently selected types, alignment modes or wider protocols, but none is needed for an exact-T read. Do not invent reinterpretation, unaligned, volatile, atomic, store or take modes here. |
| Repack existing draft storage/reference assignments | Permissible without aliases or retained old readers. No width or required-capacity benefit justifies the wider redesign for this gap; current assignments are not protected merely because occupied. |

Relative to the accepted registry, this consumes one of 85 unassigned short
primary cells, leaving 84. The unassigned `1B..1F` range becomes `1C..1F`;
other short gaps and all tiny/long assignments remain unchanged.
All Body record kinds, family controls, section/token/DefId kinds, TypeSpec
forms, clause kinds and Feature IDs remain unchanged. Candidate E9, EC, DF,
RootPin and construction allocations in other proposals are not composed
or reassigned. Predicate-node kind `1B` belongs to a different namespace.

## Exact operation contract

```text
byte 0: 0x1B
byte 1: x | (y << 4)
x: destination R0..R13
y: source      R0..R13
```

This is a two-byte, nonterminal instruction at an ordinary instruction
boundary. R14/R15 are invalid in either position: no contextual pointer
constant or discard form is added. An unused result can occupy a general
binding; that does not erase the read or its obligations.
`x == y` is valid: read the old pointer binding before publishing T.
For example, `1B 21` reads through R2 into R1; this is not a complete Body.

The source must be an available value binding with the exact resolved,
normalized/substituted nonoptional raw-pointer type. It is not a Home index,
PlaceHandle, checked reference, function pointer, integer address or
TypeIdentity. A pointer held in a Home is first loaded as its typed value
through existing eligible transport; loading that Home alone does not read T.
An optional pointer must first be established present and explicitly
unwrapped through an independently admitted operation.

T must admit the ordinary nonconsuming value read under its complete
Copy/access contract and have an independently admitted, positive-extent
target storage representation. This operation neither copies an affine owner
nor invokes an authored copying/construction protocol. `ptr<void>` is not
loadable. A pointer to a unique/owning T does not gain a destructive take.
The operation does not assign missing stored-reference, root, view, owner,
aggregate or optional representations. Implementations decline unsupported
required types rather than silently treating equal-sized storage as scalar.
This is not a new source-level ban on otherwise admitted pointer uses.

1. **Order and observation.** Source lowering evaluates the pointer expression
   once, captures its value, then performs the read at this ordered operation.
   The reader reconstructs an ordered read, not a deferred expression that
   repeats a call or observes a later rebound pointer. Existing permitted
   optimizations remain subject to memory/effect observations.
2. **Target representation.** Read T's complete payload under the selected
   target layout, not the pointer width or a host-default integer width.
   On Windows x64, loading int32 reads four bytes with alignment four;
   loading a pointer cell reads eight bytes with alignment eight.
   Preserve signedness, native-sized identity, floating signed zero and
   NaN bits under ordinary typed-load rules. Invalid Boolean/rune/nominal
   representations are not normalized into valid values.
3. **Validity and access.** Require the actual readable extent, permitted
   access, alignment, provenance and live stable backing for this use.
   Hardware tolerance for unaligned reads does not relax a typed-pointer
   contract. There is no unaligned flag, inferred array bound, implicit
   marshalling, pin, root, allocation, owner acquisition or lifetime extension.
   Existing concurrency/non-tearing rules apply; no stronger atomic,
   volatile, synchronization or device-memory contract is introduced.
4. **Unsafe authorization.** Authored reads require explicit lexical unsafe
   authorization. A method's `unsafe` declaration or Effects `04`
   caller-unsafety does not authorize its body. Producers and shared bound
   verification preserve that fact through traversal and rewriting.
   The Standard operation is inherently an unsafe raw read; loading it does
   not make it safe or authorize neighboring operations. There is no wire
   “authorized” bit. Source-free consumers follow
   [reader trust](../specs/representation/reader-trust.md): validate supported
   structures and caller obligations without claiming to reconstruct lexical
   source or prove arbitrary unsafe promises.
5. **Backing and nested values.** Preserve known storage generations,
   guarded alternatives, access and provider/owner dependencies.
   Loading `ptr<U>` from a `ptr<ptr<U>>` reads the pointer **stored in the
   cell**; its pointee backing is not the cell's address or automatically
   the cell's backing. Preserve any independently established stored-value
   dependencies. Unknown backing cannot become an invented known local
   region, global lifetime or unload lease. Complete inner qualifications
   survive; readonly outer access does not erase write authority carried
   inside an independently valid stored pointer.
6. **Initialization, failure and cleanup.** A load changes no destination
   vacancy/completion fact. `ref<init ptr<T>>` completion makes that pointer
   value available under the existing CallSite contract, never T itself.
   Preserve the grammar's explicit-unsafe definite-initialization exception
   without publishing checked initialization facts. Neither an initialized
   descriptor nor a successful presence check supplies pointee validity.
   Output failure/partial-state, reservation, loan and settlement obligations
   remain unchanged. Failed operand evaluation publishes no result and
   preserves its actual effects/cleanup. An invalid raw access gains no new
   catchable failure, zero/null result, rollback or cleanup guarantee.
   A successful load retires neither the source cell nor its referent.

## Exact schema selection and requirements

Call the operation delta above **P**. Recommend these two closed, explicitly
selected successor agreements, not component flags or a composition algorithm:

| Exact out-of-band candidate identity | Complete selected schema/catalog |
|---|---|
| `trrm/1-draft-init-reference-bodies-pointer-loads-1` | Existing `trrm/1-draft-init-reference-bodies-1` (B+I and its selected companions), unchanged except P |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-1` | Existing [B+I+G selection](standard-trrm-initialization-predicate-composition.md), unchanged except P, including its required accompanying failure-identity/construction context where applicable |

Keep `(1,0)`, codec `01`, Body framing, `mapMask`, map/record widths and
Module governing edition `1`. Selection is exact and ordinal before
schema-dependent indexing, writing, binding or lowering.
No on-wire discriminator, version bump or new Feature is selected.
Under [draft versioning](../specs/representation/format-versioning.md),
equal version bytes, Feature lists, names, plausible operands or decoder
failure cannot select this amended schema. Released-schema stability remains
the format owner's separate rule.
Revision 1 denotes the complete stated agreement, not future moving contents
of linked proposals; subsequent changes require renewed exact agreement.
Within these successors, P is valid only in implementation-kind `01`
register Bodies. It does not make the implementation-kind `08` compact-indexed
writer or reader a Standard fallback.

The old identities are not widened or aliases. Explicit conversion from
them decodes and rebuilds the destination representation; changing an external
label or version bytes is not a conversion. No old/private decoder retry or
opcode-octet substitution is permitted.
For the bounded provider qualification, root and every supplied provider use
the **same selected successor row**, retaining the current init-profile
uniform-selection boundary. A provider with no P instruction can be explicitly
re-emitted under that successor without inventing a read.
This proposal adds no mixed-schema provider-loading policy.

Local-field-layout, C1/C1M1, F/storage-address, RootPin, generic-call and
exception-region successors are **not** selected. A provider retaining their
additional obligations cannot be packed into one of these successors by
dropping those obligations. Likewise P does not give a parameterless Main
an address producer: any required local/storage/function-address operation
must have its own already selected carrier and composition, or qualification
remains blocked there.

Each Module retaining a P operation requires **`2/1 RootlessNativeValues`**
and its prerequisite **`1/1 NativeBoundaryContracts`**, including retained
uncalled or unreachable Bodies. Feature 2's pointer-operation scope supplies
this operation only under the exact amended schema; its presence does not
choose that schema or authorize unsafe source. Preserve each parent's
complete catalog and conditional closure, including bounded `10/1` and G's
`17/1` where used. No Feature 3/4 is required merely for a data read;
actual native calls/address formation retain their independent requirements.
Requirements remain per Module, not inherited from a provider's manifest.

## Owning integration and cross-layer obligations

These are proposed amendments only; the owners are reached through the
[specification index](../specs/README.md).

| Owner | Required integration; preserved boundary |
|---|---|
| [Registry chapter](../specs/representation/registry.md) and [machine registry](../specs/representation/trrm-1.registry.json) | Amend `instruction_encoding.short_forms`/`unassigned_ranges`, operand/type constraints and retained-use detection for `1B`. Keep `19` checked-only and all other namespaces unchanged. |
| [Instruction set](../specs/il-instruction-set.md#assigned-short-forms), storage/family/allocation tables and [IL conformance cases](../specs/conformance/il-registry.md) | Add the exact operation, type domain, ordering and unsafe raw-read semantics above. Existing `01` BlockEntryState value types carry pointer bindings; they do not assert pointee validity or manufacture backing. |
| [Metadata](../specs/il-metadata.md#body-table-and-coded-instruction-boundary), [references](../specs/language/references.md), [grammar](../specs/grammar-spec.md) | Cross-reference the raw-read mapping; change no TypeSpec, qualifier, Param, CallSite or source production. Keep TypeSpec `08`/eligible inline ptr forms distinct from `0D` function pointers and `0E` init references. |
| [Manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md), [reader trust](../specs/representation/reader-trust.md) | Add P's retained-use/support checks within 2/1 and the exact selections above. Source producers own semantic truth; consumers perform mandatory structural, support and binding checks, not arbitrary source/body re-proof. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [canonicality](../specs/representation/encoding-canonicality.md) | Preserve complete expected contracts, exact origins and decoded remapping. One two-byte form is canonical; no tiny/long/private alias is selected. |
| [Native boundaries](../specs/native.md), [JIT](../specs/jit.md), [data layout](../specs/targets/windows-x64-data-layout.md) | Apply existing target width/layout, validity, unsafe and code/backing-lifetime rules equally to JIT/AOT. No new ABI, boundary admission or hardware-fault translation. |

The [initialization-destination proposal](standard-trrm-initialization-destination-callsite.md)
remains the sole owner of its shared source-use-5, DD, preparation,
publication and result/destination association direction. P neither replaces
nor expands it. NativeAddress remains function-pointer formation; it is not
a data read, even when both result carriers are pointer-sized.

* **Producer/shared bound pipeline:** select the proposed schema explicitly,
  preserve generic complete types, authorization and backing through shared
  traversal/rewrites/substitution, and emit the read at its actual position
  through the selected kind `01` register-Body path, not the compact-indexed
  kind `08` fallback.
  Do not admit only an int32 example, a named provider or one Main shape.
* **Reader/reconstruction:** update instruction length/roles, typed register
  flow and selected Body reconstruction together. Reconstruct the raw read
  and its relied-on facts, not `BoundReferenceRead`, a bitcast, trusted flag
  or private indexed fallback. Refuse an unsupported representation/merge
  before affected use rather than erasing its dependencies.
* **Provider/replacement:** retain original consumer expectations and actual
  provider origins separately. Apply the complete directed binding relation,
  including nested pointee identities/access, layout, effects, output states
  and dependencies. A pointer returned through an initialized cell cannot
  escape provider-local backing merely because the cell was completed.
* **Re-emission/remapping:** rebuild TypeSpecs, nested pointer identities,
  nominal/provider Imports, signatures, contracts, Home/Type/Method maps,
  typed records and live register/CFG relationships. P has no embedded row
  index, but its source type and dependencies still need remapping. Rebuild
  code/Block/record offsets, extents and affected SourceMap instruction/step
  references after transport or instruction-width changes; do not substitute
  matching bytes. Distinct storage Homes remain distinct.
* **JIT/AOT:** route the admitted reconstructed operation through typed native
  lowering, maintaining once-only address evaluation and actual target
  read width. Keep provider code, helpers and backing live through required
  use. Successful emission is not execution evidence.

### Malformed input and truth boundaries

Before affected use, reject unsupported exact schemas/requirements, `1B`
under an unamended schema, truncated instructions, invalid instruction/Block
extents, R14/R15 operands, unsupported operand-carrier/type combinations,
wrong-namespace/out-of-range referenced maps/types and unrepresentable
required layouts. A checked reference, optional pointer, code pointer,
integer or PlaceHandle is not a P source. T=`void`, a required consuming read,
or an unsupported complete type cannot be silently reinterpreted.

Producer/source verification rejects missing lexical unsafe authorization
and known invalid access/backing/lifetime facts; an optional semantic audit
can inspect published Body truth more deeply. Do not turn those obligations
into a claim that a mandatory reader proves every address, initialization
history, external byte representation or foreign promise. Structurally
consumable false semantic metadata remains a producer violation and can
lead to undefined behavior under the existing trust contract.

## Focused acceptance cases

These cases define the full acceptance target; the bounded implementation
does not yet satisfy every row.

| Family | Positive, negative and boundary observations |
|---|---|
| Typed reads | Read unrelated signed/unsigned widths, nint/nuint, float32/64 and typed pointer cells from independently admitted backing. Preserve exact floating bits and nested pointer qualifications. Check int32's four-byte read at an aligned readable-region end with the following region inaccessible, not an eight-byte read followed by truncation. |
| Encoding/types | R0/R13 and `x==y`; pointer values arriving from formals, Homes and call results. Reject R14/R15 in either role, absent bindings, truncated `1B`, `19` used as a raw load, nonpointer/optional/checked/PlaceHandle sources, void/owning reads and unsupported representations. Preserve ordinary checked-reference behavior. |
| Unsafe/validity | Explicit unsafe body/expression versus only an unsafe method declaration; removed/forged source-bound authorization. Known dangling/retired backing, insufficient extent or alignment and forbidden access remain invalid despite unsafe. Invalid external representations are unsafe-contract violations, not automatically mandatory-reader detections or catchable failures. |
| Pointer-cell outputs | Publish `ref<init ptr<T>>`, return/pass its completed pointer and read independently valid T. Separately verify cell completion does not initialize a vacant pointee or establish its validity. Failed/partial output paths grant no new state or cleanup exemption; raw reads publish no checked state. |
| Order/failure | A pointer-producing call executes once; a later rebinding cannot redirect the captured read. Read before/after an intervening write observes the required order. Operand-call failure performs no read/result publication; preserve earlier effects and required cleanup. |
| Nested backing | Two cells contain pointers into different live backing, distinct from both cell addresses. Copy/forward/join/re-emit without conflating those dependencies. Reject known local-backing escape and invalidation; unknown external backing never becomes invented local provenance. |
| Provider/re-emission | For each exact successor, separately emit provider initialization/forwarding and consumer read Bodies; load without source, reorder nested pointer/TypeSpec and method/import maps, re-emit **both**, reload and bind replacement providers. Accept compatible contracts; reject changed pointee identity/depth/access, output relation, dependency or schema. Include multiple independent cells/providers and noncanonical valid map ordering. |
| Execution/selection | Inspect actual P bytes, independently reconstruct the raw read, and confirm the participating Bodies use kind `01`; then exercise linked Standard JIT and separately emitted native-library/AOT reads under each claimed successor. Supply backing through an independently admitted route; do not smuggle address formation into CallSite or P. Old B+I/B+I+G, kind `08`, and unselected composites reject P; missing 2/1 or 1/1 rejects affected use. |

Use the existing public compiler APIs, active tests and headless supervision.
Keep exact compiler/configuration, schema, provider/input and final-patch
identity with run evidence under the existing artifacts locations.
Source/private JIT, structural loading, pointer-return equality and host
readback do not substitute for the proposed linked Standard read. Adoption of
P and these exact selections remains unapproved; the bounded implementation
and qualification do not discharge the remaining matrix above.
