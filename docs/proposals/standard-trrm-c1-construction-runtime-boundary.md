# Standard TRRM normal-class construction boundary

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This is a construction-specific delta, not
normative adoption, implementation support or a public managed-machine ABI.

## Inherited base and decision

Inherit the **proposed contract**, not the historical implementation observations,
of [ordinary references with normal-backing support](reference-normal-backing-support.md)
(**N**): its [host selection](reference-normal-backing-support.md#exact-host-selection-and-propagation),
[carriers/control](reference-normal-backing-support.md#exact-carrier-and-allocation-identity),
[holds/retirement](reference-normal-backing-support.md#hold-acquisition-copy-publication-and-retirement),
[call handoff](reference-normal-backing-support.md#sources-staging-and-direct-calls),
[provider/JIT/AOT](reference-normal-backing-support.md#provider-identity-jitaot-and-retained-origins)
and [persistence/consumers](reference-normal-backing-support.md#persistence-consumers-and-remapping).
Retain N's single-threaded, jointly regenerated Windows x64 closure,
one normal runtime instance and runtime-only-retirement restriction.

For ordinary nongeneric, nonunique normal C, add only:

1. Explicit fresh-backing/Publish/Abort operations in real construction Bodies.
2. Admission of fully decoded, bound **construction-2 Standard programs** to
   N's native preparation, including by-value handle inputs/results.
3. The private payload/allocator choices below and **terminal exhaustion,
   `runtimePolicy=0`**. No handlers, recoverable allocation or unwind codec
   is added.

Inherit [construction-1's I/P roles, signatures, keys and complete closure](standard-trrm-c1-constructor-declarations.md).
P remains a checked complete-result program, never the Boolean/out TryCreate
adapter or an intrinsic Body. This execution restriction does not narrow
C1 declaration publication or permit dropping unsupported obligations.

No GC service is added. Preserve
[one integrated GC/runtime interface with multiple implementations](integrated-gc-runtime-boundary.md);
neither a RootPin side runtime, GC allocation plus permanent pin, nor stable
GC-pointer assumptions substitute for normal backing. The
[common ABI's normal-support/interop gate](integrated-gc-runtime-abi.md#values-cell-identity-and-support)
remains separate.

## Exact semantic successor and candidate assignments

Recommend this distinct, explicitly **unapproved** whole-schema identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2`**

Call it **construction-2** below. It is exactly the governing proposal's
**construction-1** successor plus the changes in this section and their
semantics below. Keep `(1,0)`, edition 1, Body kind/encoding `01`, existing
Construction clauses and inherited catalog edges. C1M1 and construction-1
remain unchanged; no conversion, relabeling or compatibility alias is defined.

Select construction-2 ordinally and case-sensitively for the root and **every
supplied provider, including unused inputs**, before schema-dependent
decoding. API defaults do not select it. A mismatched selector, plausible
record or Feature list cannot trigger a retry under another schema.

All assignments below are **unapproved candidates in construction-2 only**.
Primary/record tags are hexadecimal; Feature, role and action numbers are
decimal:

| Namespace | Candidate |
|---|---|
| Feature | Decimal **`25/1 = ClassConstructionBodies`**, edition 1, no unconditional Feature prerequisites. Requires the complete normal construction lifecycle, typed class/place use, retained ownership/dependency facts and an admitted execution agreement when executed. |
| HomeMap role | **`4 = ConstructionContext`**. Preserve the eight-byte row; flags, formalOrdinal and reserved are zero. TypeMap identifies the exact eventual nonoptional, normal C, with its checked access qualifications and nongeneric class owner. |
| Long primary | **`ED = ObjectConstruction`**, exactly `ED, control:u8=0, recordIndex:u16`. The little-endian index selects Body record `11`. This is a four-byte instruction, not a prefix or escape. |
| Body record kind | **`11 = ObjectConstruction`**, exactly twelve bytes, as below. |

Require 25/1 for retained bodyful I/P, role-4 Homes or record-11/ED uses, even
in an uncalled Body. Bodyless I/P declaration roles alone still need no new
Feature. Retain independent 13/1 field-map/import/query, graph, scalar-value,
generic and native requirements; 25 does not select their codecs or imply a
GC/root/pin capability. No numeric TargetProfile, new runtime policy, Contract
kind, section or DefId is assigned.

Record `11` has the existing four-byte header
`u8 kind=0x11, u8 flags=0, u16 reserved=0`, followed by:

```text
u8 action, u8 destinationKind, u16 contextHomeIndex
u16 destinationIndex, u16 reserved=0
```

| Action | Exact operands and meaning |
|---|---|
| `1 BeginNormal` | Destination kind/index are both zero. Establish fresh unpublished normal backing in the inactive context Home. |
| `2 Publish` | Destination kind `1=Register` or `2=Home`; publish the complete C into that exact vacant result residence, consuming the context. |
| `3 Abort` | Destination kind/index are both zero. Release an unpublished context only after its actual acquired obligations and dependents have ended. |

Context indexes are zero-based HomeMap indexes selecting role 4 in this Body.
A Publish register is R0..R13, never a constant/discard. A Publish Home is
an ordinary role-3 local of the exact result type, not another context,
receiver or formal. A register result establishes a new binding with its
own vacant responsible staging; rebinding cannot abandon an earlier hold.
Begin/Abort have no result, not a default C.
All `u16` Home/record indexes, including FFFF, remain usable; there is no
absent-index sentinel. Unknown actions/kinds, nonzero reserved fields,
wrong roles/types, bad extents and trailing bytes refuse affected use.

### Allocation comparison and canonical writing

The [accepted registry](../specs/representation/trrm-1.registry.json) has 39
recognized long primaries, including shape-only EA, and 25 unassigned cells:
`CD..CF`, `DF`, `E9`, `EC..FF`. F0/F1 is **not an assigned runtime band**.
All candidates below have the same four-byte `C0..FF` width and full `u16`
record payload. EB is not a terminator/prefix boundary: E0..EB mixes
continuing operations and control transfers; terminality is operation-specific.

| Candidate | Comparison and disposition |
|---|---|
| `E9` | The [NativeAddress candidate](standard-native-address-formation.md#allocation-review-and-alternatives) uses it. Reassignment is permissible, not a normative collision, but creates a composition choice without construction size/capacity benefit. |
| `EC` | The [StorageAddress candidate](standard-native-storage-address-formation.md#alternatives-and-allocation-decision) uses it. Displacing that draft supplies no additional construction capability. |
| **`ED`; selected** | Fresh backing-lifecycle family near storage formation, with no competing assignment found. An organizational choice, not protection of unpublished E9/EC meanings or creation of a reserved runtime band. |
| Other `EC..FF`, including `F1` | Equally representable; F1's adjacency to unapproved F0 supplies no benefit. Leaving earlier holes buys no capacity. |
| Reclaim `EA`/`EB` | Would relocate recognized RegionExit/ReturnHome shapes without encoding benefit. Preserve them, including EA's region gate. |
| Reallocate RootPin; unselected | Moving F0 to EE only changes grouping. A denser **future** common family could use `ED/00 → 11` for construction and `ED/01..07 → 12` for ReferenceSupport, preserving its 16-byte record/seven actions while saving one primary and retiring its conflicting `0D`. This requires a new integrated runtime/target/catalog/schema agreement and revised [RootPin mapping](reference-root-pin-runtime.md#candidate-typed-body-program)/[selection](trrm-root-pin-schema-selection.md), without F0/0D aliases. It is not composition with N. |

Construction-2 admits **only ED/00 to record 11**; other controls, prefixes,
escapes, short/tiny forms and intrinsic construction Bodies remain invalid.
All three actions normally continue; even allocation's terminal failure path
does not supply the normally reached Block terminator. Actions consume no
index bits. Counts remain `u32` bounded by 65,536; global rows/code offsets
retain their own widths. The successor has **40 recognized long cells, 24
unassigned**, and **13 record kinds** including inherited G1 `10` and new
`11`; uncomposed `0C..0F` stay invalid. TypeSpec/Contract/Feature/TypeMap
numbers remain independent namespaces.

[Canonical writers](../specs/representation/encoding-canonicality.md) always
emit ED/00, even for indexes 0/15/255. The lower-primary tie-break concerns
assigned equivalent forms, not unselected E9/EC/F1 alternatives. Records
and Type/Method/Field/Constant maps retain first-use order/unused omission;
HomeMap order is not canonicalized or deduplicated. Ordinary valid
permutations require complete reference remapping, never opcode-octet patches.

### Current catalog and implementation cross-check

The registry assigns **1..7, 9..13**; other proposals use **8, 14..24**.
The current [Feature reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs)
has no 25 admission. Inherit [G1's record 10/Feature 23](standard-trrm-generic-method-call-body-encoding.md#allocation-review-and-alternatives),
not RootPin 14 or CG1 24. The [Body reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs)
rejects ED/F1 instructions, and the [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyWriter.cs)
validates through it: length, control, record, Feature and nonterminal checks
must change together. F0/F1 TypeMap-token handling is not opcode support.

## Checked construction program

The affine context owns a fresh generation, provisional hold and actual
field/acquisition state; it is not C, a pointer or `ref<init C>` storage.
Begin requires an inactive context and leaves required fields vacant, never
default-initialized. I permits only Uncalled→Entered→Completed; set Entered
before its declaration-initializer prefix. Completion applies I's minimum
guarantees, not whole publication.
Publish requires I completed **exactly once**, all required storage/validity
and surviving dependencies; it transfers the provisional hold without a retain.
Publish/Abort consume the context; reuse creates a new generation.

I retains its live nominal receiver cell, Home role 1/carrier 0, with unpublished
object authority. Its general CallSite receiver input zero selects the context
with `sourceUse=5`;
only that receiver position admits it, and P's target is exactly its role-2
selected I after binding. Field PlaceSelection (`targetKind=2`) additionally
admits `baseKind=2, baseIndex=contextHomeIndex`. Preserve aliases/CFG state;
no context Copy, ordinary Home read/take/return, whole escape, post-publication
initialization, access upgrade or unsafe permission follows.

Normalize P before row assignment:

1. Use the once-staged inputs and original formal correspondence.
2. BeginNormal for C.
3. Execute real I once: declaration initializers, preconditions, authored body,
   phase cleanup and postconditions.
4. Perform the actual final completion/validity checks; do not replay I's
   clauses or discover a `Validate` method. Unknown registration is not absence.
5. Publish, then return through N's checked result handoff.

P has no caller entries; incomplete I or unsupported final checks prevent
P publication, not I retention. Caller-entry constructions keep their actual
call-site program. DoesNotReturn has no normal result. Derive P's actual
allocation/failure effects, preserve unsafe/capture/failure obligations and
I's origins through inlining. Body `0B` still selects real P→I with its unchanged
zero-supplied/default/empty-params binding, not a hidden allocation recipe.

## Extension to N's native preparation

Admit the decoded construction-2 declaration/Body graph only with N's existing
explicit host selection. Recompute complete native class layouts/call plans
from it; neither metadata inspection, a serialized key nor stamping an old
`BoundClass` selects or implements this extension. N's target-profile and
persisted-layout-observation gates remain unchanged; no new host key, CLI
grammar or profile is assigned.

Apply N's value handoff to normal-handle inputs/results; P returns its integer
carrier in RAX. I borrows the active context's `NormalBacking*`; an ordinary
C receiver borrows the staged complete handle's control, not a payload address
or handle-slot pointer. No hidden argument is added. P→I forwarding transfers
the applicable original parameter responsibilities by supported transfer/elision,
without an extra wrapper retain; preserve actual borrows/later uses.

For default Auto payloads, place fields in declaration order, rounding each
offset to its complete field alignment and tail-rounding to maximum alignment
(minimum 1), with checked target-width geometry. Unit fields remain logical
but nonaddressable; an empty class still has allocation identity. Preserve
other declared layout commitments or refuse; impose no serialized prototype
32-field/128-byte limit.

Select **GetProcessHeap/HeapAlloc/HeapFree**, releasing the original base
through the same heap. Reserve checked extent for alignment-adjusted payload,
control and all later nonfailing bookkeeping before success; retain the
original base. No public header/displacement or independently compiled helper
ABI is assigned. These Win32 calls implement the runtime, not a source
NativeImport API or a tracer/GC fallback.

Private generated helpers use N's Windows convention; this is **machine
notation**, not Terrum declarations or exports:

```text
u32 BeginNormalConstruction(const NormalClassDescriptor*, NormalConstruction* vacantOut)
void PublishNormalConstruction(NormalConstruction* consume, NormalBacking** vacantOut)
void AbortNormalConstruction(NormalConstruction* consume)
```

Begin returns exactly `0=success` or `1=exhaustion`; failure reverses internal
acquisitions and leaves the context vacant. Publish/valid Abort allocate
nothing, invoke no authored code and cannot fail.

## Construction-specific failure boundary

On Begin exhaustion, **failfast** without I invocation, C result, retry or
source Boolean/None/default. Other constructor/validation failures preserve
their actual operation and unpublished state; policy zero promises no unwind
cleanup. It does not bypass selected ordinary `require`/phase-2 fresh-class
construction/raising: missing class/witness/event support refuses before use.

Abort only releases **empty unpublished backing** after all actual remaining
acquisitions and dependent loans/holds have ended. Any independently admitted
abandonment/rollback program must retire them in reverse acquisition order,
distinguishing complete children from partial storage; never invoke the
incomplete parent's Cleanup, even after all fields were written before validation failed.
No exceptional rollback carrier is added here. After Publish, failure follows
N's complete-value retirement, not failed-construction rollback.

## Integration and remaining gates

On adoption, amend these owners together; none is edited or approved here:

| Owner | Intended amendment |
|---|---|
| [Constructor roles](standard-trrm-c1-constructor-declarations.md), [source publication](standard-trrm-c1-class-source-publication.md) | Route normal P execution through construction-2; preserve complete declaration/member closure and construction-1/C1M1 meanings. |
| [Registry](../specs/representation/trrm-1.registry.json), [Body](../specs/il-instruction-set.md), [metadata](../specs/il-metadata.md), [requirements](../specs/representation/requirements-and-admission.md) | Add the four assignments, exact operands/state/Feature rules and nonterminal classification. |
| [N](reference-normal-backing-support.md) | Add only construction-2 preparation and the construction/call projections above; its ordinary support, provider, consumer and native-lifetime rules remain inherited. |
| [Source construction](../specs/grammar-spec.md#510-properties-and-accessor-local-effects), [flow](../specs/language/initialization-and-flow.md), [ownership](../specs/language/ownership-and-cleanup.md), [allocation failures](../specs/runtime/standard-failures.md#4-recoverable-allocation-exhaustion) | Preserve logical phases, complete/partial state, failure and retirement semantics. |
| [Private ABI](../specs/data-layout-and-abi.md#private-implementation-versus-independently-replaceable-boundaries), [Windows calls](../specs/targets/windows-x64-call-interface.md), [JIT](../specs/jit.md) | Link the bounded execution delta without admitting independent managed native providers. |

Carry context/generation, I state, fields/acquisitions and validation dependencies
through shared bound traversal, producer verification and source-free lowering.
Remap context Homes, exact C/I/P and clause origins, destinations, CallSites,
records/maps and instruction/source locations with C1's declaration graph;
recompute native plans. Inherit N/C1's original provider expectations, consumer
truth/checking split and exclusion of serialized runtime state.

Approval, implementation and qualification remain open. Normal/GC or precompiled
interop, authored Cleanup, recoverable failures/E1, generic/unique classes and
missing validation/witness codecs remain separate gates. Property
initialized-state has the separate unapproved
[PI1 candidate](standard-trrm-c1-property-initialized-state.md). Until its
metadata, source and runtime gates are implemented, refuse uses requiring
property completion. Do not omit closure or claim full C1/Core-exception
completion.

## Focused acceptance cases

Required future outcomes, not test results:

| Case | Required outcome |
|---|---|
| Real program | Unrelated immutable/mutable classes, zero/unit/nonzero parameters: real I/P Bodies, once-staged inputs, one allocation/I, ordered final validation and complete C result. Refuse P needing caller entries or missing checks; preserve I and exact `0B` binding. |
| Lifecycle/failure | Reject live-C/init-cell receivers, repeated I, context Copy/escape, early Publish, stale generations and Abort with acquisitions/dependents. Branches/loops preserve state; validation failure publishes nothing and never invokes incomplete-parent Cleanup. |
| Wire/canonical | Check indexes 0/15/255/FFFF, overflow/capacity, flags/extents/namespaces/destinations and Feature 25. Only ED/00→11 is valid and always four bytes; reject alternative primaries, nonzero controls, prefixes and ED as Block terminator. |
| Allocator | Empty/unit fields, mixed/over-alignment and high extents; inject each internal failure. Reverse internal acquisitions before terminal exhaustion, with no I/result, weaker alignment or allocator fallback. |
| New call path | Headless JIT/AOT exercise by-value handles/P results, nested/recursive calls, later argument rebinding and fifth physical positions. No wrapper retain delays the actual parameter/result endpoint. |
| Providers/remapping | Non-first/multi-Module providers, aliases, bodyless expectations and private constructor closure survive source-free permutation/packing/re-emission. Reject retargeted P→I, changed contracts or lost expectations. |
| Admission | Reject mixed exact schemas, including unused providers, missing N selection, independent native instances and unsupported GC/Cleanup/ownership/failure/codec requirements. |

Use public APIs and final compiler/configuration/input identities with matched
active tests. Existing N factory or constructor-clause tests are not P execution
evidence.
