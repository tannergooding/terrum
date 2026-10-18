# Standard TRRM native boundary metadata

**Status: UNAPPROVED extension proposal for consolidated review.** Raw
caller-unsafety, direct target-compatible representation, authored wrappers,
and removal of compiler-owned API/COM policy are settled. Explicit
native-callable entry support is an approved direction. The logical carriers,
physical shapes, capability decomposition and numeric candidates below are
recommendations, not canonical assignments or implementation acceptance.

This proposal covers imports, native-callable entries, native function
pointers and generic vtable calls together. It supersedes this document's
earlier direct-import-only scope and its deferral of the latter surfaces.
Withdrawn COM owner/acquisition, IID, reference-count and automatic cleanup
records remain withdrawn. See [unsafe FFI cleanup](unsafe-ffi-vtable-cleanup.md)
and [joint output/failure/ownership composition](trrm-output-failure-ownership.md#proposed-native-boundary-composition).

## Recommendation and reason to extend TRRM now

Add required semantic records to standard `.trrm`, selected through ordinary
Terrum dependency binding. Reuse complete types, declarations, signatures,
effects and layouts; add the native binding, callable role, explicit layout
commitment and pointer relationships that those records cannot currently
express. Do not make private modules, a compiler catalog or a native package
the only source of a dependency's callable contract.
This does not redesign private semantic modules, `.trmn`, `.trng` or native
deployment sets. Those routes may carry derived code and their own private
representation, but cannot establish standard TRRM support by implication.

The [format-version contract](../specs/representation/format-versioning.md)
explicitly permits draft changes while retaining `(1, 0)`, without unpublished
artifact compatibility. The current
[registry](../specs/representation/trrm-1.registry.json) is
`trrm/1-draft-unified-3`; it registers no Feature IDs and no released schema.
Its authority over current assignments does **not** make metadata frozen,
complete or closed to extensions. Missing records are design work to specify,
not a reason to reject the needed language/compiler route indefinitely.

Recommend one integrated next draft, with the candidate agreement label
`trrm/1-draft-native-boundaries-1` and the existing version pair `(1, 0)`.
The label is out-of-band draft agreement, not a new header field. Consumers
must select that exact schema and its governing semantics; equal version
bytes or a Feature number cannot make an old decoder understand new records.
After user consolidation, update the canonical registry, clause applicability,
manifest catalog and conformance gates together. This proposal performs none
of those normative updates.

An existing consumer must still reject an unsupported required record rather
than drop its meaning. That is a temporary support boundary, not a permanent
decision against extending TRRM. Implementation can proceed against an
explicitly selected experimental draft; ordinary standard interchange is
claimed only after review, registration and complete consumer support.

### Settled semantics, recommended choices and remaining choices

| Category | Disposition |
|---|---|
| Settled | Raw imports and native-callable declarations require unsafe callers, including scalar-only signatures. An unsafe declaration does not authorize its own body. |
| Settled | No automatic marshalling, pinning, rooting, ownership/refcount operation, COM acquisition or foreign-provider proof. Preserve known backing, loans, access, output state and cleanup. |
| Settled | API names, library symbols, IIDs, SDK layouts, slot choices and application algorithms are authored library/source data, never compiler recognizers. |
| Recommended here | Required native declaration records, an explicit native callable-signature carrier, numeric vtable binding with an explicit receiver, generic pointer/output/dependency clauses and exact capabilities. |
| Recommended here | First executable boundary is synchronous, closed, rootless ABI data. Use target classification without a 16-byte aggregate cap or a sample-specific admission gate. |
| Still for review | Exact source annotation/type-use spelling; product loading defaults; whether to consolidate the proposed scoped dependency/value facts into broader shared codecs before registration. Recommended defaults appear below. |

## Logical contract and ownership of facts

| Fact | Producer and consumer obligation |
|---|---|
| Terrum identity | Ordinary Module/Import/Export selects a declaration and its complete expected contract, not a DLL inferred from its source name. |
| Foreign binding | An import declares library identity, exact export and loading policy. A native-callable entry explicitly opts into the foreign ABI; public Terrum visibility alone is not an OS export. |
| ABI and types | Retain exact target convention, complete scalar/pointer/function-pointer types, logical parameter order and fixed-layout commitment. Derive registers, hidden result storage and stack positions in lowering. |
| Effects | Retain existing Effects, including caller-unsafe. NoThrow is independent of native faults, blocking, purity, allocation and foreign retention. |
| Pointer use | Authored access, extent, alignment and retention promises are separate from pointer representation and the actual argument's backing/loan facts. |
| Output | Preserve the exact invocation result, region/destination version and conditional validity. A native pointer cell becoming readable is not ownership or initialization of everything reachable through it. |
| Dependencies | Preserve result/capture sources and carried backing, pin/root, holder, code/load and cleanup obligations through copies, fields, forwarding, binding and replacement. |
| Vtable binding | Retain receiver formal, vptr offset, slot and native signature. No IID, interface acquisition or reference-count semantics follow. |
| Verification | Readers revalidate supported shapes and binding; mandatory verification reconstructs local authority and flow. A serialized authorization or provider assertion is not a verifier bypass. |

The accepted [pointer/reference rules](../specs/language/references.md),
[dependency rules](../specs/language/dependencies-and-views.md),
[caller-unsafety](../specs/grammar-spec.md#53-caller-unsafe-declarations-and-explicit-unsafe-scopes),
[layout contract](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations)
and [Windows x64 call interface](../specs/targets/windows-x64-call-interface.md)
remain the semantic owners.

`ptr<T>` is a raw ABI address, not `ref<T>`, `pin ref<T>` or `init ref<T>`.
Copies and admitted erasures preserve known dependencies; none acquire a pin
or extend a lifetime. A pointer into movable storage still needs its explicit
live pin or independently stable backing. Per-indirection qualifications
remain distinct; this proposal does not settle every C/C++ const mapping or
introduce `ptr<init T>`.

Optional foreign annotations are authored evidence, not provider verification
and not a prerequisite catalog of approved APIs. Their absence supplies no
no-retention, initialized-output or fresh-backing theorem. Raw addresses with
unknown provenance can remain unsafe inputs; converting a known stack/pin/owner
loan into an unknown address is not a way to discharge its checked obligations.
Known authority, backing or retention contradictions still reject under the
ordinary rules. Lack of proof about foreign behavior alone is not a reason to
reject an otherwise admitted raw call.

## Proposed physical records

All new numbers and changes to reserved shapes in this section are
**unapproved candidates**. Existing
Module, ModuleRef, TypeDef, FieldDef, Layout, MethodDef, MethodSig, Param,
TypeSpec, Import, Export, Contract and Body framing remain unchanged.
New semantic sections use required policy `0x0000`, not the optional
nonsemantic extension range. Typed references remain little-endian `u32`
row indexes with a zero high byte and the existing 24-bit bounds; only fields
marked `?` admit zero. No semantic references are hidden in Blob bytes.

### Candidate allocation and integration choice

| Namespace | Candidate allocation | Existing assignments checked |
|---|---|---|
| Required section kind (`u16`) | `0x0021` NativeImport, `0x0022` NativeCallable, `0x0023` NativeVTable, `0x0024` NativeFunctionPointerSignature | Continue the required-table sequence after `0x0020`; SourceMap remains a separate namespace role at `0x7F00`. |
| Contract kind (`u16`) | `0x0006` NativeAbi, `0x0007` NativeParameter, `0x0008` NativeOutput, `0x0009` NativeRepresentation; complete shared Dependency `0x0004` | Extend the assigned clause sequence and give the reserved general dependency owner reviewed variants instead of a permanent native-only duplicate. |
| TypeSpec form (`u8`) | `0x0D` native function-pointer signature-table reference | Canonical forms are `0x01..0x0C`. This is not a new atomic/family type code. |
| Body primary opcode (`u8`) | `0x2A` AddressOfPlace, `0x2B` ReinterpretPointer, `0x60` ReadPointer, `0x61` WritePointer, `0x31..0x32` native address/indirect calls | Keep place/address operations after `0x20..0x29`, raw memory in the `0x60..0x6F` band, and invocation after Call `0x30`. Vtable calls reuse Call. No unsafe-tail opcode is proposed. |
| Feature identity (`u32`) | `1..5`, exact revision `1` | The current catalog registers none; use one coordinated sequence with the generic/construction candidates `6..8`, not holes reserved for experiments. |

These are separate namespaces: a section kind does not collide with an equal
opcode byte. No new DefId discriminator, qualifier bit, MethodDef flag, target
requirement, runtime-policy value or type-family code is proposed.
ABI/role/access/condition domains below belong to these new records only.

The smaller additive alternative keeps native-only InteropDependency and
places every new operation in an unused nearby slot. Recommend the substantive
integration above: one dependency vocabulary, semantic operation bands and
one candidate table/capability sequence with
[generic/construction metadata](generic-nominal-construction-metadata.md).
The shared Dependency variants require an explicit amendment to the current
reserved shape and applicability; they are not current-schema meanings.

Fixed-width metadata IDs do not become smaller on disk merely because their
numbers are lower. These choices improve ownership and allocation structure,
not a measured size result. Experimental assignments, including Feature 1
ScalarInitOutput and the former native clause numbers, may be retired or
reassigned. They reserve nothing in this draft.

Before registration, check the then-current registry and all candidate
namespaces, replace every affected table/operand/manifest reference and
independent vector, and revalidate decoded artifacts. Do not substitute raw
bytes or alias the historical experiments to these same-number candidates.

### Native binding tables

| Section | Exact ordered row payload | Stride |
|---|---|---:|
| `0x0021` NativeImport | `u32 methodDef, str libraryName, str entryName, u32 resolutionPolicy` | 16 |
| `0x0022` NativeCallable | `u32 methodDef, str entryName?` | 8 |
| `0x0023` NativeVTable | `u32 methodDef, u32 expectedMethodDef?, u32 receiverOrdinal, u32 vptrByteOffset, u32 slot` | 20 |

Rows inherit their module through MethodDef; no new Module partition cursor
is needed. Each provider method has exactly one binding row of its declared
role, except a signature-only contract, which has none. Duplicate/conflicting
rows reject. A raw import or vtable declaration has no Body. A local
native-callable entry has its own ordinary verified Body. Unresolved method
formals, an implicit instance receiver and variadics are outside revision 1:
all receiver and ordinary ABI parameters are explicit Param rows in mode
`ordinary`, with no implicit-reference adaptation.
Boundary parameters/results are formally admitted target scalars, raw data
or native-function pointers, and the marked Copy structs below. Checked
references, init modes, managed/owning descriptors and unrepresented optional
layouts do not become raw ABI parameters through this record.
NativeVTable.receiverOrdinal must select an ordinary data-pointer parameter;
it is not MethodSig.receiverTypeSpec or an extra physical argument.
An authored local binding has `expectedMethodDef=0`. A materialized consumer
dispatch declaration uses that field to retain its separately imported
expected role-4 declaration; the latter has no local binding row. Require
matching complete callable contracts and a genuine ordinary Import on the
expected declaration. Reject self-links, chains and cycles. The local binding
retains the exact slot/offset facts used by the consumer and checks them
against the selected provider before affected execution or replacement.

An unresolved imported expected MethodDef has no local implementation or
native binding row: its ordinary Import selects the provider. Compare its
declared ABI role and complete callable contract, then obtain binding data from
that selected provider. Do not require a consumer to duplicate a DLL binding
or invent a local code address for its bodyless proxy. For a role-4 vtable
declaration, this provider lookup is distinct from a consumer-local dispatch
stub materialized after source binding. Signature-only anchors are
artifact-local structural carriers, not independently exported or imported
declarations; a selected callable's complete contract supplies the signature
when reconstructing a local anchor.

Library/entry names are nonempty, NUL-free decoded text. Library identities
are names, not filesystem paths; named native exports use exact target
symbol spelling, without a guessed suffix or inferred source name.
Revision 1 uses ASCII library basenames and export names so that PE/loader
byte interpretation does not depend on an ambient code page. A broader
symbol-name encoding needs an explicit target mapping, not silent transcoding.
`NativeCallable.entryName=0` means addressable native entry only, not an
automatically named PE export. A nonzero entry name explicitly requests an
OS-visible symbol, independently of ordinary Terrum Export keys.
Duplicate native export names in one output image reject.

Recommend these resolution-policy candidates:

| Code | Meaning |
|---:|---|
| `0` HostResolved | Use the selected deployment's ordinary native-library resolution. It supplies no trusted-system-image guarantee. |
| `1` SystemDirectory | Require the selected Windows deployment/loader to enforce system-directory resolution, or reject that lowering. |

The default recommendation is explicit authored `HostResolved`, not an
immutable compiler API catalog. No source-supplied path, plugin discovery,
automatic DLL staging or implicit ownership is added. A static PE import's
DLL basename alone cannot satisfy SystemDirectory; a backend must establish
the actual loader/deployment basis or decline. Renaming a policy value does not enforce it. Current loading restrictions
belong in the [native support inventory](../implementation/native.md), not
in this candidate policy's meaning. Ordinal imports and
platform-specific decorated/member conventions need later explicit shapes,
not overloads of `entryName`.

### Native ABI and callable-signature identity

Contract `0x0006` NativeAbi is a MethodDef-owned singleton. Its payload is
`u32 abiId, u8 role, u8 exceptionMode, u16 reserved=0`: 16 bytes including the
existing eight-byte Contract header. Candidate ABI ID `1` denotes the exact
selected Windows x64 system call interface and requires Module target `1`.
Roles are `1` raw import, `2` native entry, `3` signature-only contract and
`4` vtable call. Exception mode `0` is direct non-unwinding; other values
reject in revision 1. NoThrow remains an ordinary Effects promise, not an
implicit consequence of this byte.

Every role requires the existing caller-unsafe Effects bit. Native entries
also require checked NoThrow and the accepted fault-containment-or-termination
boundary; an arbitrary body is not made contained by setting the bit.
An unsafe declaration still needs explicit unsafe regions for operations
inside that body. A contained foreign-failure adapter is a separate selected
[bridge](../specs/runtime/foreign-failure-bridge.md), not an exception-mode
fallback or a universal interpretation of integer status.

Candidate TypeSpec form `0x0D` is exactly
`u8 form=0x0D, u8 qualifiers=0, u32 nativeFunctionPointerSignatureRow`
(six bytes). Required section `0x0024` has fixed eight-byte rows:
`u32 signatureContractMethodDef, u32 reserved=0`. Each row targets a bodyless,
non-exported role-3 MethodDef carrying the complete MethodSig, Effects,
NativeAbi and applicable pointer/output/dependency clauses. A TypeSpec refers
to this table row, not to an implementation or an entry-point name. The
signature row order is the artifact-local type identity used by Body values;
duplicate normalized contracts reject rather than creating alternate type
identities. The referenced MethodDef is a contract anchor, not a callable
implementation; ordinary Call and native address formation cannot invoke or
take its address.

This reuses MethodDef-owned contracts instead of inventing a second Effects
or Param system. A native function-pointer type's normalized identity is the
resolved complete callable contract, not the anchor's row number or name.
Separate anchors can denote equivalent signatures; different nominal pointee
identities, conventions, modes, access, effects or retained relationships
cannot be merged because their machine words match. Reject unanchored
structural signature cycles; pointer recursion through valid nominal types
does not expand their pointee storage.

Existing `ptr` TypeSpecs can point to a function-pointer cell, and FieldDef
can carry the complete function-pointer TypeSpec. The new form specifically
selects a foreign convention; it does not change the accepted default Terrum
convention of an unqualified `fnptr`, choose a new source spelling, create an
environment or extend code lifetime.

### Explicit fixed representation

Contract `0x0009` NativeRepresentation is a TypeDef-owned singleton:
`u32 targetRequirement, u32 explicitAlignment, u32 valueKind`, for 20 bytes
including the header. Target is `1`. Metadata `explicitAlignment=0` means
omitted; otherwise it records the source's actual power-of-two requirement
in `1..8192`, not merely the derived aggregate alignment. This metadata
sentinel does not permit source `Alignment: 0`.

The clause expressly carries the per-target Blittable commitment. Candidate
value kind `1` additionally records a rootless, cleanup-free Copy contract;
it is a separate declared fact, **not** an inference that Blittable implies
Copy. Revision 1 admits ordinary closed structs with that fact and
Sequential, Union or Transparent layout under the existing target rules.
Unique/owning values, enums or other declaration kinds need their complete
general value/validity/ownership codecs before this scoped carrier admits
them; a marker must not retag one as a Copy struct.

Retain the existing Layout row and every complete FieldDef type. For
Sequential/Union, Layout.pack is the normalized effective packing
`1/2/4/8/16` (omitted source packing is 16). Transparent has no custom
packing/alignment controls and uses pack `0` here. Recompute field offsets,
size, object alignment, transitive required explicit alignment and stride;
reject mismatches, by-value storage cycles, Auto layout or unknown fields.
Raw Union keeps the target's unsafe alternative-access and Copy-field
restrictions; it does not gain an active-member tracker.

Scalar, covered data-pointer, native-function-pointer and recursively marked
struct fields retain their exact types. Pointer fields do not recursively
require a layout for an opaque pointee. A typed operation exposing T's data
instead requires T's admitted commitment and validity, including when a
selected import publishes typed access to that region. A declaration cannot
publish an uncommitted T layout by calling it a pointer. `ptr<void>` can carry
an opaque address without asserting a foreign object's layout.
Layout numbers, public visibility or Sequential alone never supply the
commitment. Fixed inline-array, enum and owned-value codecs remain separate
representation work, not a reason to reject the minimal scalar/pointer route.

### Pointer access and retention

Contract `0x0007` NativeParameter is MethodDef-owned and keyed by
`(parameterOrdinal, pointerDepth)`. Its exact payload is:

```text
u32 parameterOrdinal, u32 pointerDepth,
u8 access, u8 retention, u8 extentKind, u8 reserved=0,
u32 extentValue, u32 requiredAlignment
```

The complete row is 28 bytes. Depth `1` selects the first pointee, depth `2`
the next pointer's pointee, and so on; every selected level must exist in
the complete data-pointer type. It is not a field path or receiver ordinal.
Access codes are `0` unspecified, `1` read, `2` write and `3` read/write.
Write facts must agree with the access at the corresponding indirection;
they cannot upgrade known readonly backing.

Retention codes are `0` unspecified, `1` call-extent/no-retention on every
outcome, and `2` declared capture into a surviving destination. Code `2`
requires the matching shared Dependency capture relationship; it does not
allocate or prolong that destination. Code `0` is not no-retention and does
not grant a safe escaping loan. These are provider promises at an unsafe
boundary, not a demand for provider-specific proof.

Extent kinds are `0` unknown (`extentValue=0`), `1` fixed elements,
`2` element count in an integer formal, `3` fixed bytes and `4` byte count in
an integer formal. In count forms extentValue is a zero-based formal ordinal,
not a table reference. Checked region use must establish a nonnegative,
representable bound under the ordinary flow/runtime rules, without size/stride
overflow; the annotation itself does not prove it or silently add a marshalling
stub. A fixed extent zero genuinely covers no pointee bytes; it is not the
unknown sentinel or a string-termination proof.
Alignment zero means no additional published bound; a nonzero value must be
a supported power of two. Neither value improves actual backing alignment.

For a raw import's ordinary by-value pointer formal, `NativeParameter` is
optional. The pointer TypeSpec already carries the ABI pointer shape and any
represented pointee identity/qualifier; without this clause the artifact makes
no provider promise about access, retention, extent or alignment, and grants no
checked pointee observation or escape. The declaration remains caller-unsafe.
Use `NativeParameter` only when the author opts into those stronger facts.
Native-function-pointer values use their `0x0D` TypeSpec and complete signature
contract directly as by-value arguments or results; they do not require a
data-pointer `NativeParameter` clause.

These clauses describe only their selected regions. Whole-value copying,
embedded pointer fields and adapters still preserve their actual dependency
closure; the absence of an explicit field-path clause cannot erase it.
Complete C/C++ const/direction import syntax remains separate work.

### Result-correlated native outputs

Contract `0x0008` NativeOutput is MethodDef-owned, with the same
`(parameterOrdinal, pointerDepth)` key:

```text
u32 parameterOrdinal, u32 pointerDepth,
u8 condition, u8 onMatch, u8 onOther, u8 reserved=0,
u64 comparisonBits
```

The row is 28 bytes. The destination is a writable data-pointer region in
the exact signature with a complete, positive-extent admitted pointee.
`ptr<void>` alone does not identify an output cell; a pointer-valued cell
instead uses its full pointer-to-pointer type. Zero-data logical output
initialization is not a raw memory write. Conditions are `1` always, `2` integer nonzero,
`3` integer zero, `4` signed integer nonnegative, `5` integer equal and
`6` integer not equal, plus `7` Boolean true. Codes 2–6 use the exact
immediate integer result, at most 64 bits; code `4` needs a signed result.
Code `7` needs the exact atomic bool, not Win32 BOOL or an integer alias.
Comparison bits are zero for codes 1–4/7 and a correctly width-bounded bit
pattern for codes 5–6. Floating, later async and arbitrary predicate conditions
are not inferred.

States are `0` unspecified/unavailable for checked observation,
`1` unchanged valid entry value and `2` complete valid pointee value.
Unchanged requires a valid entry value. An always condition requires
`onOther=0`. Reject conflicting/overlapping output-region declarations.
For `ptr<ptr<void>>` at depth 1, state 2 publishes the pointer cell's value
only: not a live pointee, fixed interface, nonnull value, owner or release
obligation. Initialized-null is a value, not vacancy.

No source default such as “integer nonzero means success” is inferred from
the return type. An author can select nonnegative status without assigning
HRESULT or another API a compiler-known meaning. Failure state 0 never
authorizes reading, adopting or releasing a possibly unwritten pointer.
Explicit live failure outputs remain describable; their ownership, if any,
belongs to the wrapper's complete ordinary contract.

NativeOutput is not the checked `init ref<T>` mode or a replacement for
OutputState. It supplies no new address-of-vacant-place operation and no
automatic safe output publication. A wrapper may use initialized private
native staging, check the result and then publish its own declared output.
The [joint proposal](trrm-output-failure-ownership.md#proposed-native-boundary-composition)
defines the required publication/cleanup composition.

### Whole-source relationships

Recommend completing general Dependency clause `0x0004` with shared
DependsOn/Captures/ForwardsBacking variants rather than introducing a native
owner model or permanent InteropDependency codec. Its proposed
MethodDef-owned payload is:

```text
u8 destinationKind, u8 sourceKind, u8 relationship, u8 phase,
u32 destinationOrdinal, u32 sourceOrdinal
```

The complete row is 20 bytes, keyed by destination kind/ordinal, phase and
source kind/ordinal. Destination kinds are `1` result, `2` checked init-output,
`3` native-output pointee value, `4` retained formal holder and `5` retained
receiver. Result/receiver ordinals are zero; formal destinations use their
actual zero-based ordinal. Native-output dependencies in this revision select
depth 1; deeper or partial-region retained relationships need a separately
complete carrier, not a dropped dependency.

Source kinds are `1` whole formal and `2` receiver (ordinal zero for the
latter). The receiver is never formal zero. Relationships are `1` eligible
source-storage borrow, `2` existing-backing-only forwarding and `3` their
broad union, matching the meanings discussed in the
[reference-call proposal](reference-call-metadata.md#1-which-dependency-relations-belong-in-the-first-callable-profile).
Every listed source constrains the caller, and a by-value parameter copy is
never surviving caller storage. Code `0` source with ordinal/relationship
zero explicitly declares no borrowed sources for that destination; it
cannot coexist with source rows for the same destination/phase.

Phase `1` is normal publication and is used by destination kinds 1–3.
Phase `2` is capture into an existing surviving holder on every applicable
exit and is used by kinds 4–5. A normal-failure status is still an exit that
may retain state; no implicit rollback or exceptional release is assumed.
Output destinations must actually be published in the declared outcome before
dependent values can use them. An init formal is not a live default source.
Existing-holder destinations must independently denote actual surviving
storage/state, not a callee's by-value parameter slot. An opaque native address
alone proves neither a holder lifetime nor authority to capture into it.

Writers normalize inherited/default source relationships into explicit rows.
No missing row proves a dependency-bearing result fresh. Explicit empty
borrowed-source sets still retain definition-owned backing/code/load facts.
The verifier derives actual instance versions and nested closure from values
and calls; annotations do not serialize fresh lifetime authority.
Function-pointer copies preserve code dependencies as well as data backing.
This concrete interop provider/consumer path supplies the trigger for
revisiting the previously deferred physical dependency work. The same rows
serve ordinary reference/view and native declarations. Review their owner,
keys, variants, defaults and phases with the reference-call proposal and
replace the current reserved interpretation explicitly; this does not activate
every wider dependency/async/field-path variant or silently reinterpret an
older artifact.

Sharing a clause does not make ordinary reference/view use depend on the
Windows native ABI capability. NativeBoundaryContracts admits its native
declaration uses; an ordinary reference/output capability must admit its own
complete types, phases and execution under the same selected clause semantics.
Recognizing the shared metadata does not enable all those Bodies.

## Executable Body carriers

Declaration metadata alone cannot make these operations executable. Recommend
the following candidate shapes under the capabilities below. `r`, `t`, `m`
and argument lists use the existing Body value/type/method namespaces and
width rules; the method operand keeps its typed artifact-wide escape.

| Opcode | Candidate shape | Required meaning |
|---:|---|---|
| `0x2A` AddressOfPlace | `rNew, tPointer, rPlace` | Form an exact typed data address from positive-extent initialized, addressable backing; retain access, extent, actual alignment and every backing/pin dependency. |
| `0x2B` ReinterpretPointer | `rNew, tPointer, rSource` | Explicit unsafe data-pointer reinterpretation/erasure; retain actual backing and alignment, with no authority upgrade or fresh-backing inference. |
| `0x60` ReadPointer | `rNew, rPointer` | Read the complete admitted Copy pointee under its exact access, validity, extent/alignment and dependency contract. |
| `0x61` WritePointer | `rPointer, rValue` | Write the exact complete pointee type under ordinary authority and state obligations; neither initialize an unrelated checked output nor discard dependencies stored in a pointer field. |
| `0x31` NativeAddress | `rNew, tNativeFnPtr, m` | Resolve a native import or explicit native entry, check its callable contract, and retain the selected symbol/provider/code origin. Never use the consumer proxy's absent body offset. |
| `0x32` CallIndirect | `rNew?, rNativeFnPtr, arguments` | Invoke the complete native function-pointer contract, preserving staged arguments, output correlations, effects, backing and code lifetime. |

Reuse the accepted bounded indexed Unsafe region `0x06` and its complete
region/edge validation. Its number is not a new allocation. Generalize its
admitted operations for the reviewed native contracts, extending other Body
forms only under a reviewed equivalent structural model. Do not standardize experimental
UnsafeTail `0x2E`, infer authorization from a declaration's effect, or maintain
two permanent scope systems. Its current scalar experiment is described in
the [output inventory](../implementation/output-profiles.md#provisional-trrm-output-bodies).

Imported vtable binding has two phases. An unresolved expected declaration
selects a provider through ordinary Import. Materialization creates a distinct
local dispatch declaration whose NativeVTable row retains both that expected
MethodDef and the exact receiver/offset/slot/signature. Re-emission preserves
the typed link and derived obligations; it does not turn dispatch into a call
of the provider's code export. The explicit link is preferable to copying an
unidentified row or losing the dependency because only the local stub is
called. Retaining a declaration dependency does not itself require loading a
provider code image for the vtable call.

Current imported-address, local callable, role-4 and scalar-output prototypes
remain bounded experiments. Their copied binding rows, private revisions and
supported Body shapes are inventoried in
[modules](../implementation/modules.md#persisted-caller-unsafe-operations) and
[native support](../implementation/native.md), not approved by this design.

The new pointer/address operations and vtable-targeted Calls require
structurally reconstructed unsafe authorization, including scalar-only
indirect/vtable calls. Zero result IDs retain the existing unit-result rule.
No declaration's unsafe bit authorizes nested operations in a caller or in
its own body.

Ordinary Call gains raw-import and resolved dependency targets under the
direct-call capability, retaining full selected declarations and contracts.
A role-4 MethodDef is an indirect vtable target under the vtable capability:
its explicit receiver ordinal, vptr offset, slot and complete signature
determine dispatch. Signature-only contracts cannot be ordinary targets, and
taking the address of an unbound vtable declaration rejects. NativeAddress
cannot silently expose an ordinary Terrum method as a foreign entry.

At a vtable invocation, evaluate/stage source arguments once in their normal
order. Use the staged receiver formal; load its vptr at the declared byte
offset and its code address at `slot * targetPointerSize`, with checked
address arithmetic and all known access/backing obligations. The receiver is
an ordinary explicit pointer parameter in the native argument list, not an
extra hidden `this`; aggregate result storage still follows the selected ABI.
These loads occur at invocation after argument staging. Do not fuse explicit
source loads with different observation timing into this operation.
An author asserts external table validity; metadata does not prove its extent
or a COM identity. Nontrivial native member conventions, implicit this
adjustment and C++ inheritance are not implied.

Native value support must also admit and verify the applicable existing
Copy/Select, Aggregate/Extract, place, field, initialization and branch shapes;
their current inspectable numbers alone do not provide that support.
The existing equality/inequality operation shapes can compare same-type native
addresses, including null, under this capability; no address arithmetic or
data-to-code conversion follows from reusing their type matrix.
ConstBits can represent a null raw address with zero bits, not arbitrary
integer-to-code conversion. Complete type identity survives field transport.
Reconstruct pointer dependencies from address formation, values, copies,
joins, loads/stores and call contracts rather than trusting serialized root
sets. A nested indirect call has exactly the same backing obligations as a
direct one.

Call-site output facts are derived from the actual status value and
destination storage/value generation for that invocation. Reassigning a
status or reusing its destination cannot attach the old correlation to a new
invocation. An unchanged/copied status may still witness its original
invocation while that destination state remains valid; unrelated calls do not
erase it. No “output initialized” or “unsafe authorized” Boolean in an artifact
substitutes for those relationships.

The target owns scratch/result storage sizing. Allocate each staged value
using its full checked extent and required alignment, preserve distinct live
ranges across nested calls, and account separately for shadow space, stack
arguments and indirect results. Neither the metadata nor the Windows x64
ABI supplies a 16-byte maximum or a fixed scratch stride.

## Exact capabilities and implementation sequence

These candidate Feature IDs all use exact revision `1`, not a minimum-version
comparison. Their interpretation is under governing edition `1`, target
requirement `1`, with the direct non-unwinding boundary and the existing
runtime-policy obligations. The canonical catalog currently registers none.

| Candidate Feature | Coherent scope | Prerequisites |
|---|---|---|
| `1` NativeBoundaryContracts | Declaration tables/clauses, shared dependency variants, native callable-signature TypeSpec, fixed representation, complete binding/rewrite rules and malformed rejection. No new Body execution is implied. | Edition/target rules above; applicable base metadata/effect contracts |
| `2` RootlessNativeValues | Exact admitted native scalar/address/function-pointer and marked Copy-aggregate values, fields/places, operations `0x2A`, `0x2B`, `0x60`, `0x61`, initialization and backing-flow verification. | `1/1` |
| `3` DirectNativeCalls | Standard-TRRM direct imports and selected dependency calls, target ABI staging/results and source-independent consumers. | `1/1`, `2/1` |
| `4` NativeCallableEntries | Explicit native entry Bodies/exports, NativeAddress and CallIndirect, pointer transport and retained code origins. | `3/1` |
| `5` NativeVTableCalls | Generic explicit-receiver/offset/slot calls through role-4 MethodDef targets of ordinary Call, including supported pointer/function-pointer/output arguments. | `1/1`, `2/1` |

`NativeVTableCalls` does not depend on native address formation or indirect
function-pointer invocation: it reuses ordinary `Call` with a role-4 target.
The feature is required when a module carries a vtable binding or a Body calls
one, while `RootlessNativeValues` supplies the pointer-valued Body contract.

These are coherent optional extensions of the bounded interchange baseline,
not optional source semantics or substitutes for accepted GA output/failure
requirements. Rootless native scalar admission follows the target's formal
foreign mapping, not equal storage width; it does not activate arbitrary
arithmetic for newly carried scalar identities.

Start with declaration-library consumption and the direct scalar/pointer
route, then complete native entries/function-pointer transport and vtable
execution using these same records. This is an implementation sequence, not
deferral of their design. A declaration-only library can require `1/1`
without executable pointer Bodies. A provider retaining native entry Bodies
must declare their actual execution requirements too. Rich checked outputs,
owners, async retention, witnesses or failure bridges additionally require
their complete shared capabilities; none can be erased to fit this slice.

A source reference to a public role-4 declaration should materialize a local
vtable-dispatch stub in the consumer program, preserving the receiver, offset,
slot and full signature without treating the declaration as a provider code
import. Re-emitting that consumer preserves its call body and vtable contract;
it does not establish general binary module linking or provider compatibility.

Register every applicable declaration, value, program, operation, phase,
dependency and runtime carrier, with explicitly inapplicable layers. A
consumer can report inspectable unsupported declarations, but cannot claim
bindable or executable support merely by recognizing Feature rows. Admit the
complete retained Module/dependency closure, including address-only and other
non-call obligations.

### Contract-to-consumer acceptance boundary

| Surface | Carrier and invariant | Required independent evidence |
|---|---|---|
| Source/bound program | Resolved declarations, complete types, structured unsafe scopes and generic pointer/value facts; no API recognizers | Unrelated imports, native entries and vtables with nested expressions and negative authority cases |
| TRRM producer/reader | Tables, clauses, types and exact manifest closure; validate lengths, roles, references, owner/key uniqueness and supported meaning | Source-free bytes, separately produced fixtures, malformed records and unsupported capabilities |
| Dependency binding | Ordinary expected declarations plus selected native roles/contracts; preserve complete nominal/layout and source sets | Reordered type tables, multiple consumers, incompatible replacement and retained stronger provider facts |
| Traversal/verifier | Complete children, remapping, actual argument backing and per-invocation output versions | Nested indirect calls, field/copy forwarding, stripped unsafe scope, expired pin/loan and status/storage reuse |
| JIT/AOT/native consumers | Explicit target ABI and retained symbols/code/load dependencies; no consumer-local proxy offsets | Imported address-only use, mixed register/stack signatures, large/over-aligned aggregates and independent C ABI probes |
| Publication/cleanup | Call preparation, exact output/result publication and unchanged destination on rejected artifact production | Normal refusal, live/unspecified failure outputs, staged-result failure and destination preservation |

These are required cases, **not** results established by this documentation
change. Ordinary tests stay headless; SDK probes, emission and supervised
execution remain separate evidence. Private round trips do not establish
standard `.trrm` support.

## Binding, replacement and lifetime

1. Resolve the ordinary producer/key first; compare complete normalized
   types, ABI role/convention, modes, effects, layouts, output states and
   dependency/capture sets under the existing
   [directed binding relation](../specs/representation/binding-and-replacement.md).
   No DLL name, equal layout or matching machine signature identifies a
   Terrum declaration.
2. Preserve every expected guarantee and do not add caller requirements.
   Unknown required clauses or unsupported relationships reject; compare
   retention/source bounds by their specified meaning, not numeric ordering.
   Known retaining behavior cannot satisfy a no-retention expectation.
3. Use the selected provider's native symbol/entry/vtable binding. If an
   expectation or optimization fixes a stronger binding fact, retain that
   obligation for replacement; do not silently publish every private binding
   detail as the consumer's expected contract.
4. Retain provider code, native-library load and embedded-data dependencies
   for every address use, including storing, returning or indirectly invoking
   it without a direct Call. Function pointers do not themselves acquire
   ownership of an image. Deployment must keep the actual image/library live.
5. Packing/trimming remaps every typed reference and context-sensitive
   signature anchor; clone shared rows when imported meanings differ.
   Preserve required layout, capture, code/load and cleanup facts even when
   a direct call disappears. Never infer dependency use only from Call nodes.
6. A new provider revision revalidates all retained facts and any derived
   native artifacts. A previously accepted bit, unchanged module name or
   matching format pair is not sufficient. Incompatible machine code must
   be rebuilt/relinked before use, not patched by changing metadata alone.

The authored `GetClientRect` boundary, for example, needs an integer result,
an opaque native pointer and a pointer to the authored fixed RECT layout.
The compiler needs those generic records, not built-in knowledge of that API,
its spelling or SDK layout, and no approved-API catalog. Its success/output
promise is authored separately. This is an ABI schematic, not new source
annotation syntax or a claim that the current TRRM consumer implements the
proposal.

## Implementation gaps versus design work

Scratch sizing, selected address origin, preserved known backing, transitive
layout validation, nested unsafe traversal, address-only dependency accounting
and output cleanup already have general correctness obligations. A violation
is an implementation defect, not a reason to add provider-specific policy,
another permission bit or an artificial ABI size cap.

Missing standard carriers are separate design/implementation work. The
[conformance inventory](../implementation/conformance-gaps.md) records both
categories and their evidence boundaries. Historical findings are not current
patch verdicts; design approval cannot certify their fix, and a local fix
cannot approve this schema or establish native qualification.

## Malformed, incompatible and unsupported input

Reject, at the applicable access/admission boundary:

- Incorrect section policy/stride/extent, overflowing counts/ranges, reserved
  fields, unknown enum values, invalid typed references or hidden references.
- Duplicate binding/contract keys, orphaned or wrong-owner rows, conflicting
  method roles, a local import/vtable Body, or an unbound native entry.
- Safe raw declarations, a native entry without its required checked effect,
  a signature-only invocation, implicit convention conversion or incompatible
  function-pointer anchors.
- Missing explicit fixed representation, false transitive layout facts,
  incompatible packing/alignment, unsupported owning/managed representation
  or a typed access that upgrades actual pointer authority/alignment.
- Invalid formal/depth/count selectors, contradictory pointer/output facts,
  invalid result predicates, unsupported partial/async phases or a claimed
  retention destination that cannot survive.
- A missing normalized dependency relation, unsafe-region contradiction,
  forged output correlation or discarded known backing/code origin.
- Unknown exact capability/revision, incomplete prerequisite closure,
  unsupported target/runtime/loading policy or incompatible replacement.

Use the existing [reader-trust partition](../specs/representation/reader-trust.md):
directory checks, naturally accessed row/contract checks and retained
requirement admission are mandatory. A trusted selective reader need not
pre-scan every unrelated Body; a whole-artifact validator checks its declared
scope, including uncalled bodies. Producer truth and foreign-provider truth
are distinct. No unsupported fact becomes a default value, optional section
or success-shaped fallback.

Validate and complete production before publishing/replacing an output
artifact; rejected production must not truncate an existing destination or
leave a partial executable that looks successful. Cleanup only the
producer-owned staging/output artifacts.

## Consolidation decisions

Recommend reviewing the integrated boundary model and explicit candidate
shapes together, then assigning the exact draft/catalog and migrating every
producer/consumer. Keep source spelling a separate review: prefer ordinary
extern declarations plus explicit ABI/native-entry/type-use annotations,
and explicit receiver/slot declarations rather than a mandatory native
interface or COM type system. No settled unsafe or library-policy decision
needs reopening to choose that spelling.

The material remaining choices are:

- Confirm HostResolved as the raw default, or require an explicitly supported
  stronger deployment policy. Recommendation: permit the former under unsafe;
  never claim SystemDirectory from a tag or PE basename alone.
- Confirm MethodDef contract anchors and the rootless-Copy value scope, or
  use broader shared callable/value records with identical facts. Recommendation:
  reuse declaration/Contract owners, consolidate dependency variants now,
  and reconcile nominal Copy/Unique facts with the generic metadata proposal
  before registration rather than maintaining contradictory category owners.
- Select source annotation ergonomics and the exact reviewed draft/IDs.
  Recommendation: retain ABI-shaped source and the coordinated allocations above;
  do not couple implementation to library/API names or wait for a frozen GA
  schema.

The [encoding inventory](../implementation/trrm-encoding-history.md) and
current support chapters distinguish historical experiments. They are not
alternate meanings of this draft's IDs, even when a number is reused.
Recreate artifacts from source, or explicitly decode a selected old schema,
reconstruct complete contracts, write the new schema and revalidate; decline
incomplete inputs. No old decoder, alias or conversion is required.
This proposal changes no compiler, test or normative allocation.
