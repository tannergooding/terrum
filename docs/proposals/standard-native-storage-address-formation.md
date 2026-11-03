# Standard native storage-address formation

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This is a concrete decision for subsequent
implementation, not normative adoption, an implemented capability or native
execution evidence. No accepted allocation is changed here.

## Finding and recommendation

There is a **missing Body operation**, not a missing CallSite input mode.
Recommend **`native.storage.address`**, candidate long primary **`EC`**,
control zero, selecting a **16-byte Body record `0E` StorageAddress**.
It obtains the actual address of eligible local storage, preserves its
operation-derived region/backing facts, and produces an ordinary complete
raw-pointer value. A positive requested element count explicitly checks the
selected suffix before publication; it is a request, not asserted capacity.
Generic address formation requests one element.

Use the existing TypeMap, HomeMap, PlaceSelection and value-flow carriers;
add no address pseudo-type, provenance table, trusted authorization flag or
CallSite expression recipe. Deliver this delta through one explicitly
selected **F + storage-address** schema below. The operation itself is not
NativeImport-specific: it names no callable, formal, provider or invocation.

The bounded storage is a positive-extent, initialized, stable, rootless local
object or its eligible stored subplace, including a checked element of an
independently admitted fixed inline array. This does not select an array
constructor or Core registration. Its separate Standard identity and shape
codec remain an unapproved prerequisite; see the
[fixed-array identity/shape proposal](standard-trrm-fixed-array-identity-shape.md)
and [array representation boundary](#array-representation-boundary).

The initialized-local prerequisite has an **already assigned bounded
transport**: an explicit typed Home store from the contextual scalar
constants R14/R15. Select that ordinary Body path for S's minimum executable
scope below; do not make locals initially live or make EC clear storage.
This closes the required scalar-zero path without adding ConstantMap value
registration to F+S. General frozen-value initialization remains a distinct
composition gate.

### Why the assigned carriers do not suffice

| Evidence reached through the [specification index](../specs/README.md) | Consequence |
|---|---|
| [Body maps](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations), [complete types](../specs/language/types-and-normalization.md#complete-type-terms) | TypeMap describes a complete type; HomeMap identifies typed logical storage. Neither instructs execution to produce its native address. Loading a Home obtains its value, not its storage address. |
| [PlaceSelection](../specs/il-instruction-set.md#typed-operand-records), [element places](../specs/il-instruction-set.md#fields-elements-and-ranges) | A PlaceHandle captures a target and checks, but explicitly is not a pointer, pin, addressability promise or lifetime extension. Reference formation instead produces a checked descriptor. |
| [CallSite](../specs/il-instruction-set.md#general-call-site-record), [invocation phases](../specs/il-instruction-set.md#invocation-phases) | Inputs select completed Register/Home values in formal order. Source-use `1..5` supplies no storage-address evaluation. An input Home containing T cannot substitute for a completed `ptr<T>`. |
| [Native boundaries](../specs/native.md), [Windows pointer/layout rules](../specs/targets/windows-x64-data-layout.md#1-scalar-storage-and-validity) | A raw boundary needs the actual directly interoperable address with its access, validity, layout and lifetime obligations. Equal bits/types or unsafe authorization alone do not supply them. |
| [F candidate](standard-native-import-function-pointer-contracts.md#one-invocation-and-its-outcomes) | `000E` describes NativeParameter invocation obligations. E9/record-0D forms a **function** address; DF/record-0C reserves a vacant NativeOutput cell. Neither forms an ordinary local-storage pointer. |
| [`BoundNativeStorageAddress`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.DirectNativeImport.cs), [ordinary native lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs) | The node retains local/index, carrier/element/address types, count, stride, requested byte extent and readonly authority. Direct source NativeParameter addresses into local arrays preserve dynamic indexes, evaluate each index once, and check Q<=N-i before offset multiplication; JIT tests read the selected suffix and reject negative/undersized cases. This does not establish a Standard A2 Body producer/consumer path. |
| [F producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs) | Its admitted indirect-call arguments are formal `BoundRead` values or staged `BoundCall` results. A `BoundNativeStorageAddress` is rejected rather than Standard-encoded. This is both an implementation boundary and evidence of the missing operation, not a reason to reinterpret CallSite. |
| [Private output writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Outputs.cs), [accepted registry](../specs/representation/trrm-1.registry.json) | Private operation `0x2A` uses a place handle for storage addressing. Standard short `0x2A` already means strict floating division. Importing that operation number would corrupt the selected schema. |

These are source/document observations. The existing F route does not
establish this operation. A separate provisional F+S implementation now
covers scalar HomePlace sources, contextual R14/R15 initialization,
source-free reconstruction, and NativeParameter CallSites; it does not
implement array identity/shape or indexed PlaceSelection. Current support and
qualification evidence live in the
[implementation reference](../implementation.md).
The [provider-lowering report](standard-trrm-provider-native-lowering.md)
records the earlier Debug fast build and 27 focused F/CLI tests; those results
do not establish pointer NativeParameter runtime access/extent qualification.

## Alternatives and allocation decision

| Direction | Assessment |
|---|---|
| **Separate typed operation, existing place/value flow — recommended** | One four-byte instruction plus a 16-byte reusable plan. Keeps address formation, function addresses, output reservations and invocation distinct. The requested count permits an explicit checked dynamic suffix without inventing a fat-pointer ABI or silently treating known backing as opaque. |
| Twelve-byte address-only record; require independent extent proofs/checks | Sufficient for one-object addresses and statically proved calls. A dynamic indexed address with a larger required suffix still needs a supported explicit check. The recommended extra `u32` makes that check part of a general backed-address operation, without hiding it in CallSite or adding a second operation. |
| Amend CallSite with a Home/Place address source-use | Coherent only as a redesigned invocation protocol with address production, evaluation/check timing and provenance rules. It makes raw-storage addressing call-only or requires a second standalone operation. Existing source-use numbers cannot acquire that meaning. |
| Turn every PlaceHandle/checked reference into a raw pointer | Changes established carrier, addressability, backing and access contracts; would admit logical/relocating/non-addressable places accidentally. Explicit checked-reference extraction and root/pin remain separate operations. |
| Repack storage/native families in a fresh schema | Reassigning DF, E9, EC and the candidate records could group storage operations more closely. It gives no width/capacity benefit and couples three distinct protocols. Draft numbers are mutable, but the wider redesign supplies no missing semantic capability here. |
| General pointer offset/load/store or relocatable address constants | Adds unrelated arithmetic, validity, memory-access or constant/relocation contracts. Neither a scalar constant nor bitcast obtains a local activation's actual address. |

Review the whole [mixed allocation](../specs/representation/registry.md#2-instruction-allocation),
not just one unused byte:

| Namespace | Selected candidate and preserved assignments |
|---|---|
| Primary byte / widths | Add only `EC` in the four-byte `C0..FF` partition; no prefix, escape, short or tiny alias. Preserve short `2A` strict floating divide, DF OutputReservation, E9 NativeAddress, shape-only EA and all existing controls. The accepted 39 recognized long cells become 42 with F's DF/E9 and this EC; 22 remain unassigned. |
| Body record kind `u8` | Add `0E StorageAddress` after F's `0C OutputReservation` and `0D NativeAddress`. `01..0E` then recognize 14 kinds, including the still-gated RegionExit shape; `00` remains invalid and `0F..FF` unassigned. |
| RootPin interaction | [RootPin](reference-root-pin-runtime.md#candidate-typed-body-program) independently uses F0/record-0D. [Its selected B + RootPin schema](trrm-root-pin-schema-selection.md) stays separate. Its **unselected** future reassignment alternative to `0E` cannot be combined unchanged with this candidate; a future composite must choose noncolliding assignments, for example ReferenceSupport `0F`, and an exact whole schema. Neither number is frozen or an alias. |
| Other namespaces | No new section, token/DefId kind, TypeSpec form, Contract kind, Feature ID, target ID or Body implementation kind. Body `0E` is unrelated to TypeSpec `0E` initialization references and F Contract `000E`. Preserve aggregate Feature 8/10, RootPin 14, CopyOrigin 15, F 16, general predicates 17, enum 18 and ImmutableData 19 in their own selected compositions. |

### Exact schema and capability selection

Propose exactly
**`trrm/1-draft-foreign-invocation-storage-addresses-1`**, with distinct
selector **`TrRmArtifactSchema.ForeignInvocationStorageAddressDraft`**.
Its complete meaning is the existing
`trrm/1-draft-foreign-invocation-contracts-1` composition **F**, unchanged,
plus this document's storage-address delta **S**, its explicit semantics and
the [assigned local-initialization path](#local-initialization-prerequisite)
selected for its bounded executable scope.
The [F composition owner](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities)
continues to define all inherited catalogs and prerequisites. This is one
closed selection, not a powerset/composition algorithm or future moving text.

Keep `(1,0)`, codec `01`, Body framing and map widths. Require existing
**`2/1 RootlessNativeValues`**, deriving `1/1`, for every retained EC/0E use,
including discarded/unreachable instructions and unused retained records.
In this exact schema its pointer-operation scope includes S. Do not require
Feature 4 for a storage address alone: that feature retains its independent
function-address/indirect-call obligations. Actual F calls still need their
3/4/16 requirements, and raw outputs additionally 8/10. Queries/FieldMap
paths, native unwind and other retained uses keep their conditional closure.
Compute each Module's complete requirements; another Module cannot supply
its missing feature or representation.

S does **not** amend the inherited `11/1 -> 9/1` registration-pending
boundary, register Feature 9, or select the
[scalar frozen-value proposal](scalar-frozen-value-registration.md).
An explicit store using an assigned contextual constant has no
ConstantSource/FrozenValueNode dependency and requires no Feature 11.
Conversely, an assigned LoadConstant instruction and ConstantMap shape do
not make their payload contract complete under F+S. Empty-present
ConstantMap framing is valid noncanonical input, not a value or a trigger
for Feature 11. Retained actual frozen/evaluation uses and explicit richer
requirements still need their own complete registration; they cannot be
admitted by this selection.

Select before schema-dependent decoding, writing, binding or lowering.
Existing Standard, F, C+F, F+G, C+F+G and B+RootPin identities are **not
widened**. No additional composite is selected here. The selected schema is
not inferred from Feature 2, EC/0E, record size, unsafe source, filename or
a decoder failure. There are no Development fallbacks, private-2A aliases,
old-schema retries or obligation-erasing conversions.

The [selected producer](standard-trrm-source-schema-selection.md),
[consumer](standard-trrm-selected-provider-consumption.md) and
[native-lowering](standard-trrm-provider-native-lowering.md) surfaces can add
this exact explicit selection without changing defaults. Each separately
supplied provider keeps its own explicit supported schema; a provider with
no S operations may use F. Co-location uses one schema for the entire
artifact, never one decoder per Module. Bind the actual complete contracts,
not equal selector names or bytes. Approval, implementation and qualification
of these additions remain distinct.

## Local-initialization prerequisite

### Classification and alternatives

The [HomeMap rule](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations)
makes role-3 Homes vacant on entry. The
[source state contract](../specs/language/initialization-and-flow.md#states-reads-and-initialization)
and [NativeParameter verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.DirectNativeImport.cs)
require initialized backing, including for Write. A parameterless Main
cannot obtain that state by pretending its local is an entry formal.
Neither HomeMap allocation, native stack materialization, EC nor a native
call publishes the missing source initialization.

However, the accepted [special operand slots](../specs/il-instruction-set.md#selected-special-operand-slots)
already define context-typed integer zero in R14 and integer all-bits-set
(-1 for signed integers) in R15. The
[Home stores](../specs/il-instruction-set.md#assigned-short-forms) and
[state transitions](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement)
already admit an explicit CopyRead initialization into a vacant typed Home.
The Home supplies the exact source type; the special operand is not an
undefined incoming register. Thus **the wire contract for the required
int32-zero local is determined**. The gap is the selected producer,
reconstructor and native path's admitted Body scope, not a missing scalar
constant encoding. Arbitrary ConstantMap-backed initializers are not
determined by those shapes alone.

| Initialization direction | Decision and boundary |
|---|---|
| **Assigned contextual constant plus typed Home store — selected** | Persist the actual initialization operation, using the destination's complete type. It covers the required zero initializer and every independently admitted R14/R15 case without a new instruction, record, section, Feature or schema selector. It is not a special rule for Main, int32, a local name or a foreign API. |
| Add the existing Feature `11/1` fixed-scalar registration to S | Coherent for arbitrary fixed-width scalar literals, but would deliberately change the whole F+S catalog from incomplete `11 -> 9` to the scalar proposal's closed `11 -> []`, and select its exact `ConstantMap -> 0030 ConstantSource(tag 1) -> 003F FrozenValueNode(kind 1) -> 0003 Blob` rules (unit has no Blob). Its 24-type codec does not admit addresses, aggregates, nominal enums, native-width values or arbitrary constant execution. This is a wider composition than the present prerequisite needs, not a flag to enable on the inherited F reader. Not selected here. |
| Use ScalarFrozenValuesDraft, NominalEnumsDraft or F+G as the initializer's decoder | Those are other exact whole schemas, not per-Body/Module subdecoders. None supplies EC in this selected artifact. Combining their value support with S would need an expressly defined destination composition, complete catalog and API support; equal version bytes or shared row shapes do not provide it. |
| Store an already admitted completed call result | Ordinary typed storage can initialize from such a value without Feature 11. Preserve the authored call, failure/order and dependencies. Do not insert a foreign initializer call, change Main's ABI or replace an authored literal with a provider dependency merely to fit today's F producer. |
| Add a local-initializer record, implicit zeroing or a private literal | Duplicates assigned stores or hides the transition. A vacant Home is not a default value; private Development constants and a guessed Blob payload do not complete Standard transport. Rejected. |

This decision selects no new composition/API identity and supersedes only
the earlier omission of ordinary local initialization from S's executable
scope. It leaves F and its value catalog unchanged. If arbitrary frozen
initializers become required, deliberately select the scalar proposal in an
exact whole composition then; do not infer that amendment from this path.

### Exact admitted Body and requirements

Use the existing stream Body (`implementationKind=01`,
`instructionEncoding=01`) and these assigned forms:

| Carrier/operation | Conditions |
|---|---|
| TypeMap and HomeMap | A role-3 local has its actual complete scalar type, ordinal zero and zero reserved fields. It starts vacant. Its rebindable flag and all other authority must permit subsequent writable addressing; the flag does not itself initialize it. Preserve distinct Homes even when their initial values match. |
| `D6` Home store | Payload is the full `u16` Home index. Control low nibble selects R14 or R15; high nibble `1` selects CopyRead (`1E` or `1F`). The typed destination determines the contextual source value. This operation, at its actual position, performs the permitted vacant-to-live transition. No separate initialization record or Home-entry live-state flag is added. |
| Compact store | Short `14` has destination Home index in the low nibble and source R14/R15 in the high nibble. It has exactly the same CopyRead/state contract. Canonical writers use the shortest equivalent assigned form; ordinary readers also accept the long form at small Home indexes. No tiny tuple implies these special sources. |
| Later reads and movement | Existing Home load/return and register-alias forms retain their meanings. Read the actual Home after a permitted native mutation, not its saved initializer value. An initialized local need not become an entry formal or a CallSite result Home. |

The complete destination type must be in the **already admitted contextual
constant domain** and independently supported by the selected target and
RootlessNativeValues use. No numerical-equality shortcut is permitted:
R14's positive binary zero cannot encode negative zero; its decimal zero
does not encode another cohort. R15 means the assigned representation,
not numerical -1 for every type. Do not widen these operands to character,
nominal enum/wrapper, optional, aggregate, reference, pointer/function-address
or unit values without their own admitted contract. In particular, R14 is
not an aggregate default constructor, pointer-cell initializer or null
reference factory. Independently admitted native-width contextual operands
use their complete type and selected target, not a guessed frozen-value
Blob width. Vectors/other independent representations are not activated here.

Only an already established value exactly representable by that operand
may use this encoding. Do not discard retained constructor, query,
constant-execution, target-binding or failure obligations to replace their
result with zero. Other literals/initializers must use an independently
admitted path or decline; adding this path does not admit unrelated scalar
arithmetic, conversion, query, predicate, branch or construction code in the
bounded F executable producer/loader.

For the minimum scalar/address/call Body, HomeMap and TypeMap bits 0/1
are set; MethodMap bit 2 is also set for the E9/bound target. With no other
maps its canonical `mapMask` is `07`. FieldMap bit 3 and ConstantMap bit 4
are clear. Normal Block tables, BlockEntryState records, EC/E9/CallSite
records, offsets and terminal instructions remain required. Do not insert
R14/R15 into BlockEntryState as incoming general bindings or remap them as
Home/ConstantMap indexes. An empty-present map keeps the ordinary framing
rules; an actual retained path/map entry keeps all of its requirements.

The scalar witness uses existing sections `0001 Module`, `0002 String`,
`0004 Feature`, `0011 TypeSpec`, `0016 MethodDef`, `0017 MethodSig`,
`0018 Param`, `001B Contract`, `0020 Body` and `0021 NativeImport`.
Selected provider binding additionally uses the applicable `0010 ModuleRef`,
`001C Import` and `001D Export` rows. All normal F references, exact anchors,
contracts and ranges are still checked. This path adds **no** `0003 Blob`,
`0030 ConstantSource`, `003F FrozenValueNode`, `0040 FrozenValueChild` or
`0041 ImmutableData` dependency. Supplying such sections or a Feature row
does not choose a value decoder or complete an unsupported retained use.

The native-backed use requires `2/1 RootlessNativeValues -> 1/1`,
independently triggered by retained EC; the explicit store cannot bypass it.
This does not add a Feature requirement to an unrelated ordinary scalar
store merely because F+S is selected. An E9 plus indirect NativeParameter
invocation also requires
`4/1 -> 3/1 -> {1/1,2/1}` and `16/1 -> 1/1`: the minimal witness can
explicitly list 4 and 16 and has effective set `{1,2,3,4,16}/1`.
It does not require 9, 10, 11, 12, 13, 14 or 17 solely for that store,
address and call; raw outputs, paths, unwind or other retained uses add
their own obligations. Check each using Module's exact closure, including
retained uncalled/unreachable content, and the actual complete target/
runtime support before affected use. The bounded Windows x64 witness uses
target requirement `1`, fail-fast runtime requirement `0`; scalar storage
initialization alone introduces no query, evaluator or exception helper.

### Source-free witness, ordering and public handoff

The following is an **unimplemented acceptance recipe**, not a complete
artifact, runnable source sample or runtime result:

1. Select the existing parameterless int32 Main entry. Give it a distinct
   writable int32 local Home H, with a matching complete TypeMap entry.
   The native declaration/anchor has its own pointer formal; Main has none.
2. At the authored initialization position, explicitly store R14 to H.
   For H index zero, short bytes `14 E0` and ordinary longer
   `D6 1E 00 00` encode this same typed integer-zero initialization.
   The longer fragment is noncanonical there; it is not a new codec.
3. Form the saved native function target with E9 in source order. Form H's
   writable int32 address with EC/0E, source HomePlace, requested count 1,
   into a distinct pointer result binding. Stage that completed pointer
   once with CallSite source-use 1, preserving the original NativeParameter
   ReadWrite/minimum-one/NoEscape contract and provider binding.
4. Invoke, then return a fresh read of H through the assigned Home return
   form. A provider with an increment contract must make the observed
   result 1; returning only its scalar call result would not establish
   that H itself was initialized and mutated. Repeat with signed -1 via
   R15 and with unrelated synthetic native declarations.

The store must establish the live generation before EC. Repeating an
activation repeats its own initialization; no record denotes preinitialized
process storage. If initializer evaluation fails on an independently
admitted path, H is not published live by this store, EC/native invocation
does not occur, and earlier effects propagate under the ordinary contract.
Do not hoist the store across observable calls/checks or replace an omitted
store with prologue clearing. A later EC/argument/target-preparation failure
does not roll back a completed initialization. Once native invocation starts,
its writes retain the existing outcome/validity rules.

Extend the **same exact F+S token** deliberately through
[`EmitStandard`](standard-trrm-source-schema-selection.md#exact-public-library-contract),
[`LoadStandard`/`CreateStandardReference`](standard-trrm-selected-provider-consumption.md#exact-proposed-public-api)
and the [Standard native operations](standard-trrm-provider-native-lowering.md#concrete-public-api-candidate).
No new signature, schema flag or initializer API is needed. The selected
producer emits the complete explicit Body; the loader reconstructs the
typed initialization at its ordered position, without source or an assumed
entry value; semantic re-emission retains that operation before EC. Do not
tie the native argument count to Main's formal count or recognize only this
instruction sequence. Existing F-only calls and defaults remain unchanged.

Each external provider still supplies its own explicit F or F+S selection.
A co-located provider uses the root artifact's one F+S schema but its own
Module requirements. CLI transport reuses the already proposed exact
`--trrm-schema` input/destination selection; it adds no source Core-provider
registration authority. Preflight selection and structural/support/binding
failures publish no program, artifact, image or replacement output and do
not retry another schema. No decoding operation executes the initializer.

## Exact candidate Body record

All multibyte fields are little-endian. The sole instruction is:

```text
u8 opcode=EC, u8 control=0, u16 storageAddressRecordIndex
```

It is nonterminal, four bytes, with no alignment padding. Its zero-based
Body-local typed-record index must select exactly this 16-byte record:

| Offset | Field | Domain and namespace |
|---:|---|---|
| 0 | `u8 recordKind=0E` | StorageAddress |
| 1 | `u8 flags=0` | Common header |
| 2 | `u16 reserved=0` | Common header |
| 4 | `u8 sourceKind` | `1=RegisterPlace`, `2=HomePlace`; no value read/conversion |
| 5 | `u8 resultKind` | `1=Register`, `2=Home`; None invalid |
| 6 | `u16 sourceIndex` | General R0..R13 PlaceHandle for kind 1; HomeMap local storage for kind 2 |
| 8 | `u16 resultIndex` | R0..R13 or R15 discard for kind 1; exact pointer-typed role-3 local Home for kind 2 |
| 10 | `u16 pointerTypeMapIndex` | Complete nonoptional `ptr<T>` / `write ptr<T>`, or readonly opaque `ptr<void>` as below |
| 12 | `u32 requiredElementCount` | `1..FFFF_FFFF`, a checked minimum request in actual selected referent elements, not bytes or asserted available capacity |

Register indexes have zero high bits; R14 and special register sources reject.
A source Home identifies its own storage, **not** the address stored in it.
It must have role 3; a writable result additionally requires its rebindable
flag, without overriding any other access/dependency restriction. When both
selectors are Home, their indexes must differ. The result Home has the exact
complete pointer type and is vacant before the operation; no implicit
replacement/retirement is encoded. Register sources are
read before result rebinding, so an in-place register result is permitted.
Discard preserves required checks and effects.

Both source forms resolve the same typed-place contract. Direct Home
selection is the one-object case; array-element and field selection use an
already completed PlaceHandle rooted in a role-3 local of this activation,
not an arbitrary reference/pointer referent or heap/static place.
Source `Initialize`/`Transfer` access does
not qualify as initialized readable/writable backing. Read/Write/Reference
place classes still need their actual authority; the class alone cannot
upgrade it.

TypeMap tokens retain their existing complete-type namespace, not a direct
TypeSpecRef, pointee row or MethodMap index. Resolve/substitute in the Body's
owning Module before comparing types. Local counts remain `u32` through
65,536, all `u16` indexes including `0000`/`FFFF` usable; zero is never an
absence sentinel. Global references remain their schema-owned four-byte,
one-based 24-bit rows. No private signed-int/4096-byte storage limit narrows
the wire fields or NativeParameter's full unsigned extent domain.

## Source, region and failure semantics

The following are candidate lowering rules, not runnable-source examples
or evidence that every involved array/layout profile is implemented.

1. For an admitted `&local`/inline-object address, select that actual local
   place without reading or copying T. Its region is exactly one T. For an
   admitted `&array[index]`, capture the array place and evaluate the index
   once, in source order. Existing checked PlaceSelection selects the element
   using the original complete index; EC consumes that captured selection.
   A field subplace supplies one field object, not its adjacent fields.
2. Resolve a positive-extent, directly interoperable rootless Copy referent T
   and its selected target layout. Typed results retain **exact complete T**,
   including nominal identity, nested pointer qualifications and pointer
   depth; only outer immediate write authority may be attenuated. The
   address of a pointer cell points to that cell, not the allocation named
   by its stored pointer. Readonly opaque `ptr<void>` may expose the same
   eligible object's address without typed-void payload access; geometry is
   still derived from actual T. This is not a cast to an unrelated T.
3. Require completely initialized selected backing; this bounded operation
   does not expose vacant/partial storage even to NativeParameter Write.
   The array form requires the complete counted array initialized, not just
   the selected element; its retained suffix cannot cross vacant cells.
   It neither reads T nor initializes/publishes it. Preserve all known
   validity, access, provenance, nominal/member and alignment restrictions.
   Writable results need actual write authority throughout the selected
   range; a readonly containing path cannot be upgraded by its pointer type.
   Logical initialization does not initialize padding; foreign byte
   observations still owe their exact byte-validity contract, without an
   implicit memset, normalization or marshalling buffer.
4. For a single object, available count A is one. For a checked element i of
   an admitted contiguous fixed array of count N, A is `N-i`, starting at
   that element. Retain the captured parent/index and backing generation,
   not a reread of the source name or a guessed count from byte size.
   Require `0 <= i < N`, then `requiredElementCount <= N-i`.
   No one-past-end pointer, zero-count address, arbitrary offset, sibling
   object concatenation or pointer arithmetic is admitted.
5. Derive stride S and alignment from complete T's target layout, including
   aggregate tail padding. Validate total extent `N*S`, selected offset
   `i*S`, requested extent `requiredElementCount*S` and containment in the
   actual storage with checked, nonwrapping target-`nuint` arithmetic.
   Check index/suffix inequalities before narrowing or multiplication.
   Preserve actual address alignment; packing does not justify a stronger
   typed pointer or a copy buffer. Void and zero-extent typed borrows remain
   outside the Windows profile.
6. Publish the pointer only after selection and the explicit suffix check.
   Statically impossible requests diagnose; dynamic failed index/suffix
   checks select the existing exact Core **IndexOutOfRangeException** under
   an admitted checked-failure runtime, or that runtime's admitted terminal
   policy. Selecting this category for EC's explicit suffix check is part
   of S, not an inferred hardware fault or a new exception class.
   Unrepresentable/unsupported static layout declines compilation/admission,
   not wrapped arithmetic, truncated addresses or a fabricated runtime event.
7. On failed formation publish no result; a result Home remains vacant.
   Preserve earlier argument/index effects and existing caller obligations.
   Do not evaluate later arguments on that path, invoke native code, acquire
   a pin/root, perform cleanup of T, return null/default bits or roll back
   earlier mutations. An actual source/index failure propagates unchanged.
   Source-free handlers still require their separately complete region/event
   representation; EC does not activate B6/EA or invent one.

Generic `&place` lowering requests one element. At a direct source
NativeParameter **address-adaptation** argument, request
`max(1, declaredMinimum)` in that actual T's units, at that argument's Stage
position; opaque-void formals have no minimum and request one actual element.
This explicitly defines the bounded dynamic indexed adaptation beyond the
current index-zero binder restriction. It does not evaluate the argument in
CallSite. A separately formed pointer value does not retroactively receive
a larger EC request or an earlier failure. Its call uses the
[pointer-backing decision](native-parameter-pointer-backing.md#provisional-call-site-rules):
preserve known regions and use supported refinements/explicit checks, or
decline an unresolved required derivation. EC is such an explicit check,
not a hidden runtime length probe added to every pointer-value call.

Keep the full established region facts, not just the requested lower bound;
requesting one cannot launder a known small region into external opacity.
The check establishes numeric containment only. Provider-specific bounds,
typed initialization/validity, access, alias and lifetime obligations remain.

### Array representation boundary

Indexed S needs an **independently admitted complete fixed-array identity,
fixed count, element type, target layout and bound index/place operation**,
all available without source. A nominal name, an arbitrary constant argument,
equal-sized struct, fake FieldDefs per element, byte divisibility or a
private `NativeFixedArray` pseudo-type is insufficient.

The [array descriptor proposal](reference-root-pin-loaded-type-descriptors.md#exact-supplementary-windows-x64-interface)
already identifies this independent prerequisite; its native RootPin
FixedArray node is **not** a Standard TypeSpec/Body codec or Core registration.
S therefore does not claim that assigning EC completes Standard array
production. Loading/re-emission must retain the actual admitted shape and
index binding or refuse that affected use. If the adoption scope needs an
array shape with no selected Standard carrier, its exact identity/shape
publication is a separate missing-contract decision, not permission to
derive a count, fall back to Development, or declare the array case complete.
This is the remaining dependent decision; none of S's opcode/record, result,
extent-check or local-object semantics is left to implementation policy.

## Authority, lifetime and invocation

Preserve [source address formation](../specs/grammar-spec.md#513-operators-assignments-and-expression-gaps),
[references](../specs/language/references.md) and
[lifetimes](../specs/lifetimes.md#passing-and-storage-access).
S does not revoke initialized safe `&place` formation or admit the separate
unsafe definite-initialization bypass. F's raw import/indirect invocation
still requires explicit lexical unsafe authorization, independently of
address formation. An unsafe declaration alone does not authorize its body.
Preserve any formation-specific unsafe obligation in source/bound verification;
no record flag, source span, feature or published Effects bit authorizes it.

The pointer is non-owning and non-rooting. Its actual selected local must
remain address-stable and valid through every admitted use, including later
argument evaluation, invocation and applicable exceptional exits. Do not
substitute a by-value argument/aggregate copy, hidden ABI result temporary,
descriptor slot or unrelated spill for the addressed source object.
Backing materialization may implement the same logical local, but cannot
change its identity, mutations or lifetime. No implicit stack-to-heap
promotion, owner retain, root/pin or lifetime extension is introduced.

Copies, register aliases, stores/loads, joins and loops preserve the guarded
actual region, source storage generation, authority and independent
owner/guard/cleanup dependencies under the
[operation-derived ledger](reference-provenance-carriers.md).
Rebinding a pointer/index slot does not redirect an earlier snapshot.
Ordinary permitted T mutation is not a new storage generation; retirement
and reuse do not revive an old pointer even if the bits match.
Repeated executions form addresses of their actual activation/generation,
not the record row's purported runtime instance. Lost required facts mean
unavailable, not known-empty or externally opaque.

Forming/copying an address acquires no exclusive loan. Ordinary permitted
aliases remain possible; NoAlias, guards, output reservations and reentrant
invalidation retain their own restrictions. Known conflicting access,
retirement or overlap with a reserved output cannot be authorized by EC.
Storing/returning/capturing a pointer does not extend its backing. Preserve
the accepted explicit-unsafe/suppressible-warning policy for a known dangling
address retained as **inert data**; it never validates later access or a
NativeParameter call.

At a native invocation, stage the completed pointer value once and put it
in the existing CallSite Register/Home input with source-use `1=copy/read`.
Read/Write/ReadWrite belongs to the saved **NativeParameter contract**, not
a substitution of CallSite borrow-write/initialization modes. Check exact
pointee/access, required element extent, known backing and NoEscape against
that staged value, including invalidation by later arguments, before Invoke.
NoEscape remains the provider's complete-invocation no-retention promise,
not an acquired caller lease. Write still requires initialized backing.
Missing static minimum means neither unlimited access nor absent obligations.

For a selected provider preserve the original expectation, Module/key,
actual NativeImport origin, resolution descriptor, convention and complete
`000E` protocol through E9's function-pointer type and every indirect call.
An equal anchor, pointer bits or local address cannot repair a mismatched
provider. EC has no provider resolution side effect; it does not weaken the
[F binding relation](standard-native-import-function-pointer-contracts.md#resolution-provider-binding-and-replacement).
Co-location and joint lowering preserve those same obligations.

Keep DF/raw NativeOutput, source-use `6` and proposal-only `&init` separate.
EC neither reserves vacancy nor establishes result-correlated initialization,
ownership or success. On a later argument/check failure there is no EC
reservation to cancel. Once Invoke begins, native writes are not rolled back;
normal and exceptional validity/publication follow the actual foreign
protocol, including NativeOutput only where separately present.

## Producers, consumers, transformations and lowering

Apply the existing [truth/consumption boundary](../specs/representation/reader-trust.md),
not a new trust tier:

| Responsibility | Required treatment |
|---|---|
| Source producer and bound-program verifier | Establish source authorization, actual addressability, initialized storage, correct captured selection/geometry, authority, requested check timing, definition/use, result vacancy, alias/backing/lifetime and every invocation fact. Persist ordinary local initialization explicitly; use R14/R15 only for the actual exact contextual value, not a missing value or a default object. Normalize private storage-address pseudo nodes to the exact complete pointer type, cross-checking carrier/element/count/stride/byte extent rather than merely retagging them. Preserve variable/index and all layout/provenance facts through shared traversal, substitution and rewriting. Do not recognize API/sample names. |
| Independent consumer, before affected use | Check the selected schema, Body framing/mapMask/count/offset arithmetic, instruction boundaries/zero control, exact record kind/16-byte extent, reserved fields, closed selectors, register/local/map bounds, positive request, legal source/result Home roles, a writable direct Home's rebindable flag and distinct Home indexes when both selectors are Home, complete pointer/referent type relations and supported target/shape/layout representations. For ordinary initialization check assigned store/control/source-use forms, the referenced Home/type and the contextual operand's admitted type domain; do not treat R14/R15 as incoming general registers or a ConstantMap entry. Validate referenced type/index-operation/field declarations and complete selected provider bindings, feature closures and target/runtime requirements. |
| Semantic truth versus lowering support | Source-free consumers may rely on truthful published Body facts without proving lexical source, initialization flow, arbitrary alias/lifetime theorems or foreign behavior again. Structurally well-formed lies remain producer violations and can lead to undefined behavior. A serialized count, TypeMap, PlaceHandle class or unsafe claim is not authorization; an unsupported actual place-to-address mapping still declines use. Consumers/transforms must not drop relied-on facts merely because semantic re-proof is optional. |

Canonical writers use the sole four-byte form and first-logical-use map/
record rules, reuse identical plans where semantically identical and omit
unused entries. Distinct Homes never merge. Consistent duplicate/reordered
ordinary records remain valid; an explicit canonicality audit is separate.

Re-encoding remaps the EC record index; source register allocation or source
Home; result register/Home; pointer TypeMap and all global complete-type/
Import/layout references; defining PlaceSelection and its captured operands,
bound MethodMap index target, field paths and incoming BlockEntryState.
`requiredElementCount` is a quantity, not a row reference. Preserve every
array shape, original binding, source generation and guarded region through
substitution, copying, joins, forwarding and replacement. If a changed target
layout affects extent/stride/alignment, honor fixed/coupled layout obligations
and revalidate/re-lower dependents rather than retain stale arithmetic.

Remap every ordinary initialization/read/return Home operand together with
EC's source Home and each Home's TypeMap index. Re-select short versus long
store/load/return forms after remapping; preserve R14/R15 as contextual
operand roles, not row references or manufactured ConstantMap entries.
Keep the exact typed value, permitted transition, execution order and
distinct local generation. A transformed destination type must still admit
that same contextual value; equal byte width is insufficient. Re-emission
must preserve the decoded initialization, not depend on original source or
an unchanged-byte pass-through that cannot rebuild a modified Body.

Packing selects one destination schema capable of all retained obligations.
Do not silently pack RootPin/C/G additions into this F+S identity. Rebuild
map masks/counts, record offsets, block offsets/lengths, instruction ordinals,
Body/section extents and affected SourceMap slots; EC uses existing source
step-role zero. Preserve/remap applicable source mappings or discard them
under their existing rules. Byte substitution, clearing a feature, changing
the version pair or replacing a MethodMap import with a guessed equal target
is not a meaning-preserving conversion.

JIT and AOT lower the same selected source-local storage, captured index,
explicit suffix check and nonwrapping offset to a real current-activation
address, retaining required addressability through register allocation and
calls. Never serialize process pointer bits or return an anchor/IAT/copy
address. Windows x64 passes the resulting pointer as the existing eight-byte
native address carrier with its effective argument position/convention.
Materialize its explicit initializer into that same logical local before
the pointer is used; do not assume zeroed stack memory. Native writes through
the admitted pointer must be visible to subsequent Home reads, including
after promotion/spilling; returning a cached zero or a by-value copy is wrong.
Preserve stack alignment, hidden result shifts, stack arguments, nested-call
snapshots, unwind/frame lifetime and provider code origins. Emitting an image
does not establish these runtime observations.

## Adoption owners and acceptance

| Owner to amend on adoption | Exact change; preserved boundary |
|---|---|
| [Registry](../specs/representation/trrm-1.registry.json), [instruction chapter](../specs/il-instruction-set.md), [metadata Body](../specs/il-metadata.md#body-table-and-coded-instruction-boundary) | Add only EC/0E and the record, typed source/result and explicit check semantics; keep maps/framing, 2A, E9/0D, DF/0C and CallSite input schemas. |
| [Manifest](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md), [format versioning](../specs/representation/format-versioning.md), selected-schema proposal owners | Register the exact F+S selection and retained-use detection under 2/1 without changing existing identities/catalogs or guessing a decoder. |
| [Special operand slots](../specs/il-instruction-set.md#selected-special-operand-slots), [Home stores](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement), [ConstantMap](../specs/il-instruction-set.md#explicit-typed-constantmap), [scalar registration](scalar-frozen-value-registration.md) | No new wire rule for local initialization. Include assigned explicit typed Home stores/reads in S's bounded executable scope and preserve initial vacancy. Do not select scalar registration, change 11's inherited catalog or infer ConstantMap payload support. |
| [Grammar address rule](../specs/grammar-spec.md#513-operators-assignments-and-expression-gaps), [native boundaries](../specs/native.md), [Windows layout](../specs/targets/windows-x64-data-layout.md), [checked index failures](../specs/runtime/standard-failures.md#3-checked-index-access) | Connect the initialized local address and explicit checked suffix lowering to their owners. No new source production, marshalling, target ABI, raw output or reference/pin rule. |
| [Reader trust](../specs/representation/reader-trust.md), [binding](../specs/representation/binding-and-replacement.md), [canonicality](../specs/representation/encoding-canonicality.md), conformance cases | Add schema-owned references, independent structural/support/binding cases, preservation and re-emission requirements; retain producer truth versus optional audits. |
| [F](standard-native-import-function-pointer-contracts.md), [pointer backing](native-parameter-pointer-backing.md), [source selection](standard-trrm-source-schema-selection.md), [provider consumption](standard-trrm-selected-provider-consumption.md), [native lowering](standard-trrm-provider-native-lowering.md) | Apply S only when its new exact schema is selected. EC's requested suffix is an explicit supported check; no automatic size probe on unrelated opaque pointer values, protocol deletion or Development fallback. |

Required cases below are **acceptance work, not executed tests**. Exercise
unrelated synthetic declarations/providers, independent producers/consumers
and final-patch source-free inputs, not only one motivating native API.

| Case | Required outcome |
|---|---|
| SSA-01 Local/inline | Exact scalar, aggregate, pointer-cell and nested-pointer addresses; readonly attenuation and writable initialized mutation. Observe original storage, not a by-value copy. Reject wrong nominal T/depth, readonly upgrade, vacancy/partial/void/zero-extent and unsupported layout. |
| SSA-02 Indexed geometry | Admitted fixed arrays at first/middle/last element, counts 1 and larger, signed negative, index N/too-wide, remaining count below/equal/above request, and zero-length arrays. Check original index and subtraction first; no one-past address or fake contiguous sibling fields. Include aggregate padding/stride and packed underalignment. |
| SSA-03 Order/failure | Side-effecting base/index and later nested argument calls; index-slot/pointer-slot rebinding; failed index/suffix/later argument/target preparation. Evaluate once at the stated source position, preserve snapshots/earlier effects, publish no failed address and perform no early native invocation. |
| SSA-04 Flow/lifetime | Copies, stored pointer fields, supported call-origin forwarding, guarded joins, loops and repeated storage generations. Preserve known undersized/null alternatives and distinguish genuine opacity from lost facts. Test permitted aliasing, NoAlias/output overlap, owner invalidation, reentrancy, inert dangling retention and invalid later use. |
| SSA-05 NativeParameter/provider | Read/Write/ReadWrite, minimum zero/one/multiple/full uint domain, NoEscape, typed/opaque-void and scalar/inline/array arguments through direct and E9 indirect calls. Retain exact original/provider `000E`, access/extent and convention under separate/co-located leaf providers; reject mismatch. NativeOutput remains DF/source-use-6, not EC. |
| SSA-06 Malformed/support | Bad EC controls, record kind/extent/reserved bytes, missing maps, wrong namespace, zero request, source R14/R15/value/reference/TypeIdentity, invalid result R14/high bits/None, role/type mismatch and same source/result Home. Require complete features/target/shape/index binding, including retained unused records and uncalled declarations. |
| SSA-07 Capacity/transform | Independent indexes 0/15/16/255/256/65535 and count 65536; reject 65537 and checked extent overflow. Permute/deduplicate maps/records, remap globals/field/index bindings and source slots, preserve distinct Homes and original expectations; re-emit and reload meaning, not patched bytes. |
| SSA-08 Schema isolation | Reject EC/0E under unchanged Standard/F/RootPin, private 2A as storage formation, E9/0D as data addressing, and RootPin/F0 or absent composites under F+S. Common-subset provider bytes still need an explicit selection. |
| SSA-09 Source-free JIT/AOT | Produce, load without source, transform/re-emit/reload and execute actual original-local mutations and exact checked failures through direct/imported providers. Include nested calls, register and stack arguments, hidden returns and separately admitted exceptional native exits. Verify results and untouched neighbors, not merely image emission or parse/bind success. |
| SSA-10 Explicit initialization | Parameterless Main with one or several distinct scalar Homes, explicit R14 zero and R15 signed -1 stores, EC and NativeParameter access, followed by actual Home readback. Round-trip and execute each activation's initialization/mutation; use unrelated synthetic APIs as well as an increment provider. No formal substitution, implicit stack clear, EC initialization or aliasing of equal-valued Homes. |
| SSA-11 Store/type boundaries | Short `14` versus ordinary longer `D6`, high Home indexes, correct CopyRead mode and complete contextual type. Reject reserved controls, bad Home/type indexes, special-register entry bindings and unsupported contextual domains before use. Source/bound verification rejects vacancy, partial/maybe-live backing, missing write authority, and impermissible replacement. Omission/reordering of the required initialization is a producer truth violation; arbitrary semantic re-proof is not added to mandatory consumer checks. |
| SSA-12 Exact values/unsupported constants | Preserve the assigned typed zero/all-bits-set representations for every supported contextual domain; test binary negative zero and decimal cohorts do not collapse into R14. A nonrepresentable literal or pointer/aggregate/nominal default needs another actually admitted production path or declines. Nonempty ConstantMap frozen/evaluation uses remain unsupported under F+S's inherited catalog, even if another profile's scalar helpers can decode their shapes; retained queries still need their own complete support. Feature 11 or scalar/enum/G-shaped rows do not widen F+S or select a decoder. |
| SSA-13 Initialization transforms | Force reconstruction and actual map/Body rebuilding, not only byte-identical re-emission. Permute Homes/TypeMap/records through compact/full-width boundaries, preserve multiple independent Homes, ordinary store/EC/read order, source slots and exact contextual values, and reload the resulting artifact without source. Reject erasure of a required initializer or unsupported destination composition. |
| SSA-14 Selected-route closure | Through the same public producer/loader/native surfaces, cover owned-direct, separately selected F leaf provider and co-located leaf provider, JIT and supervised AOT executable readback. Verify `{1,2,3,4,16}/1` closure with 2 derived from 4, not a required duplicate physical row, and without invented 9/11/13 requirements. Reject EC in a Module lacking effective 2, unsupported derived capabilities and provider mismatch; preserve unchanged F/default routes and output sentinels on unsupported initialization/selection. Existing F-only tests do not satisfy this gate. |

Deliberately excluded: raw NativeOutput/`&init` reservation semantics; unsafe
uninitialized address formation; checked-reference conversion/dereference;
GC/normal-heap support acquisition, root/pin and their descriptor/helper ABIs;
static/imported data addressing; arbitrary raw pointer arithmetic,
dereference/store operations; managed marshalling, allocation/adoption,
ownership transfer, escaping foreign-buffer protocols, callbacks/async and
hot replacement. Existing independently admitted operations retain their
contracts; none becomes supported through EC.

The recommendation settles S's allocation, record, explicit check, source/
result, lifetime, invocation and consumer boundaries provisionally, and
selects the already assigned explicit-store path for its minimum initialized
scalar local. No new initialization encoding or API choice is left open for
that path. The current bounded F producer/reconstructor must still be extended
to preserve those stores, local addresses and actual post-call Home reads
through selected production, loading, semantic re-emission and native
lowering; its separate scalar-frozen-value helpers and private Outputs
operation do not implement this Standard handoff. Pointer NativeParameter
access/extent qualification remains required.

The independent fixed-array identity/shape-publication gate above, a future
exact composition if arbitrary frozen initializers are required, broader
compositions, normative adoption and implementation/qualification remain
separate. R14 does not initialize an aggregate/array or complete a missing
construction/shape carrier. Completion requires the full selected adoption
scope; a permanent unsupported branch, Development route or passing
scalar-only slice does not meet that gate.
