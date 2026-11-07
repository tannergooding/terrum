# Wide fixed-layout carriers and private persistence

**Status: unapproved; provisionally implemented in part under the user's
explicit missing-contract direction.** Formal approval, normative adoption,
implementation and qualification remain separate. [Proposal index](README.md).

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.” This document supplies a recommendation, not approval
or an instruction to implement during this proposal-only task.

## Decision and gap classification

Recommend **private semantic revision 104**, exact candidate identity
**`terrum/private-semantic-104-wide-fixed-layout-1`**: inherit the complete
inspected revision-103 codec and widen only its inline-structure size and
instance-field offset/size operands from **i32 to u64**. Carry those facts as
`ulong` in the shared semantic representation; narrow only at an explicitly
checked, capacity-limited materialization boundary.

The missing decision is this **private byte schema and its exact selection**,
not whether positive-stride layouts may exceed signed-int/u32/8192 bounds.
Choosing a host carrier alone would be implementation work. Preserving wide
aggregate facts through the required private round-trip also changes bytes;
none of the existing array/L1 proposals selects that successor.

| Inspected owner/evidence | Determination |
|---|---|
| [Windows layout §§4–5](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas), especially [253–258](../specs/targets/windows-x64-data-layout.md#L253-L258) | Checked target-`nuint` offsets, rounding, products and nested extents, with separate capacity exhaustion, are already accepted. The 8192 bound is on supplied **alignment**, not object size. |
| [Fixed-array source contract:121–134](standard-native-fixed-array-source-contract.md#L121-L134) and [extent gates](standard-native-fixed-array-source-contract.md#integration-obligations-and-acceptance) | Exact uint64 N, no int/u32/4096 source cap and compact geometry are already provisionally specified. No new source decision is needed. |
| [A1 identity/layout](standard-trrm-fixed-array-identity-shape.md#complete-identity-and-exact-layout-row), [A2 Copy evidence](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule), [L1 geometry](standard-trrm-local-aggregate-layout-execution.md#layout-physical-values-and-frames) and [L1 width gates](standard-trrm-local-aggregate-layout-execution.md#required-acceptance-cases) | Structural identity, complete element checking, full-width geometry, compact processing, large values and native preflight are already proposed. Removing semantic int/8192 narrowing is implementation integration, not a new layout rule. L1 explicitly excludes private revision/container changes. |
| [Metadata:1173–1195](../specs/il-metadata.md#L1173-L1195), [`TrRmFixedArrayLayoutTable.cs`:10–16,75–77](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutTable.cs#L10-L16) | Standard nominal offsets/size are **derived**, not serialized u32 fields; fixed-array stride/extent/alignment already use u64. No Standard wide-layout record, section or successor schema is needed. Artifact/index byte extents remain separate. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`ValueLayout.cs`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs) | The shared semantic carriers now use `ulong` for struct size, field offset/size and value size. The former 8192-byte bound remains only at bounded byte-payload analysis, not semantic layout formation. |
| [`FixedLayoutStorage.cs`](../../sources/Terrum.CodeAnalysis/Semantics/FixedLayoutStorage.cs), [`NativeFixedArrayContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs) | Target-nuint geometry now remains full-width through struct/array placement and nested layout checking. Checked narrowing is confined to bounded materialization/native-use boundaries; native displacement/frame preflight and boundary coverage remain open. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | The worktree now writes and reads exact private revision 104 under this still-unapproved candidate. Structure size and instance-field offset/size use u64; managed-class size/fields remain i32; the reader selects only 104 and rejects predecessor 103 rather than retrying. |
| [Private array proposal:250–254](native-fixed-array-private-persistence.md#L250-L254), [Repeat's historical private candidates](standard-native-fixed-array-repeat-initialization.md#historical-private-revision-102-repeat-candidate) | The u64 array table did not itself widen nominal field operands. Revision 104 is the separate exact successor; revision 100 and the historical repeat-only 102 and 102+FieldWrites candidates are not aliases or fallback schemas. |
| [Resource accounting §§1,6](../specs/representation/resource-accounting.md#6-exhaustion-host-failures-and-claims), [L1 failure boundary](standard-trrm-local-aggregate-layout-execution.md#runtime-and-failure-boundary), [A3-only failure amendment](standard-trrm-a3-layout-failure-classes.md#a3-scoped-boundary-and-a2-preservation) | Validity, unsupported mapping, deterministic capacity and physical host failure are already distinct. A3's host exception/envelope/diagnostic choices are explicitly A3-only; they cannot be borrowed for A2 or L1. No universal new failure API is implied. |

The current worktree has focused evidence for source/private wide nominal
geometry: `ModuleTests.WideFixedLayoutPrivate104RoundTripsTypeOnlyGeometry`
forms a Packing=1 aggregate with a positive-stride byte field above u32,
verifies full-width source geometry, checks exact u64 size/offset/field-size
byte vectors, and loads the private-104 producer without materializing the
payload. The same test reaches the type through a scalar static method and
does not pass the aggregate by value or execute a large allocation. Selected
revision-104 module/NativeOutput/Layout regressions pass; they do not establish
native materialization or all target boundaries. Existing test sources cover positive-stride
type-only A2/A2R1 counts and compact coverage
([16-trillion-byte geometry](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs#L713-L770)),
[nested A2 Body/provider remapping](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs#L4329),
[A2R1+Repeat max-u64 zero-stride Bodies](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs#L5322)
and [L1 zero-stride/u16-index re-emission](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmLocalAggregateLayoutConsumerTests.cs#L912-L1109).
The first wide nominal/private-104 portion is implemented and tested; the
broader matrix below remains incomplete. Positive-stride wide Body preservation,
all numeric/target-nuint boundaries and huge positive-storage execution remain
unqualified.

## Preserved authority and material alternatives

Keep the exact A1, A2, A2R1, A2R1+Repeat and L1 selections separate, including
their independently selected provider schemas and catalogs. In particular,
L1 remains `trrm/1-draft-local-aggregate-layout-execution-1`; this private
candidate is not another Standard selector or an implicit composition with
A3, E1 or RootPin. L1's all-supplied-providers L1 requirement is unchanged.

Preserve [private-persistence taxonomy](../specs/representation/persistence.md),
[exact schema agreement](../specs/representation/format-versioning.md),
[consumer checking](../specs/representation/reader-trust.md) and
[directed binding](../specs/representation/binding-and-replacement.md).

| Alternative | Assessment |
|---|---|
| **103 plus three u64 inline-geometry operand roles — recommended** | One exact successor; no high-word side table, new tag, repeated layout authority or Standard migration. Alignment controls and all inventory/reference widths remain adequate. |
| Keep 103 and reject wide private crossings | An honest current unsupported-use boundary, but does not meet the requested preservation through private persistence. It is not a reason to reject the same type at source/Standard formation. |
| Widen every private/Standard count, reference, alignment and byte extent, or introduce one new universal type/layout graph | A substantive redesign could unify more representations, but changes unrelated framing, reference spaces, containers and managed paths. None is needed for target-u64 object geometry, which Standard already represents. Arbitrary-precision persisted geometry would not make out-of-target layouts valid. |

For **104 only**, supersede the earlier private-array proposal's statement
that i32 field sizes constrain the retained inline-layout uses. Preserve its
identity, closure/projection and independently admitted operation obligations.
Do not amend or reinterpret historical 100/101/102/103 bytes. This decision
does not supersede any accepted target equation, Standard schema or source
construction/ownership contract, or the A3 failure proposal's scope.

## Shared semantic carrier

Use unsigned 64-bit completed byte quantities for `BoundStruct.Size`,
`BoundStructField.Offset` and `BoundStructField.Size`, and the semantic size
result currently supplied by `ValueLayout.Size`. All semantic field-path,
coverage, array-element, binding and verification computations consume those
full-width facts, including nested array/aggregate extents above `long.MaxValue`.
Keep logical field/type/slot indexes and inventories in their own domains.
Do not allocate one host element, byte or initialization fact per payload byte.

For this Windows x64 slice, values must fit target nuint, not the compiler
process's native-integer width. Compute field ends, aggregate minima/tail
rounding, stride and nested offsets with checked unsigned arithmetic.
Zero extent remains a real completed layout, not “unknown” or a failure
sentinel. Validate E and inline cycles even at N=0; pointer cells do not
expand pointee storage. Retain the existing unit/container floor and distinct
nominal zero-size identities.

Packing, optional declared Alignment, completed alignment and required
explicit alignment may retain their existing bounded int carriers: the
selected target already bounds their admitted values. An 8192-byte declared
alignment remains legal without imposing an 8192-byte size limit.
Backend displacement, frame, image, copy-buffer and managed-layout carriers
may remain narrower, but require checked support/capacity preflight at the
affected use. They must not be used to form or compare semantic types.

This adds no huge array initializer expansion, frozen aggregate codec, whole
array call ABI, managed/owning storage support or implicit heap allocation.
Existing independently unsupported operations remain unsupported.

## Exact private-104 delta

The predecessor is the **complete inspected 103 codec**, not revision 99,
the historical repeat-only 102 or the independently proposed 93–98
compositions. Preserve its header/profile/runtime/kind fields, source grant,
invocation buffers, entry point, structure/class/import tables, pointer and
function-pointer tables, mandatory 44-byte array table, functions/contracts,
statements and expression tags. In particular, retain actual 103 tag
**47 RepeatInitialization** and **48 PathWrite** with their existing operands
and admission; add no tag or new nested-value operation.

Keep magic `54 52 52 4D`; at offset 4 write little-endian **i32 104**
(`68 00 00 00`). The candidate identity above is documentation, not another
serialized string. There is no competing private-104 assignment in the
inspected module constants/proposal catalog; this remains a candidate,
not a Standard registry allocation.

All multibyte operands below are little-endian, without padding. Each new
operand occupies exactly the old operand's position in the variable record;
following fields move by the accumulated width delta.

| Operand in the existing 103 record | 104 representation |
|---|---|
| Structure `Size`, after name and TokenSpan role | **u64**, replacing i32 |
| Each structure field's `Offset`, after field name/type | **u64**, replacing i32 |
| That field's `Size`, immediately after Offset | **u64**, replacing i32 |
| Structure layout kind/target, Packing, optional declared Alignment, completed Alignment and RequiredAlignment | Unchanged 103 representation, including presence Boolean and i32 alignment operands |
| Structure flags, Cleanup presence/string, field count/type/index operands and field flags | Unchanged 103 representation |
| Class `Size` and class-field `Offset`/`Size` | **Unchanged i32**. Shared host field carriers require checked conversion here; no wide managed-layout admission is added. Reject negative legacy geometry under its existing validity rules. |
| Array table | Unchanged i32 count and 44-byte rows: i32 E, u64 N/S/B/A/R |
| Tag-35 storage quantities; source spans; all other records | Unchanged complete 103 shapes and domains |

A structure with m fields grows by **`4 + 8*m` bytes**. This changes encoded
record extents, not payload storage. An independent operand oracle for
`0x0000_0001_0000_0001` is `01 00 00 00 01 00 00 00`; the high word cannot
be omitted even when zero. No negative marker, optional extension, saturation,
u32 split codec or old-width fallback is selected.

Every private producer/reader using this candidate selects **exactly 104
before records**, including scalar-only programs, bodyless projections,
consumers, graph libraries and embedded semantic Contracts. Honest 103 input
rejects under 104 and vice versa; no numeric roll-forward or plausible-byte
retry. Decode complete type tables before dependent geometry checks.
Recompute admitted layout from actual fields/types and compare every retained
full-width quantity; matching low words is insufficient.

Keep TNPK/TNSP/TNGP **outer version 3** record shapes unchanged. Regenerate
embedded payload lengths, digests and enclosing extents from newly encoded
104 bytes. The outer version never selects an inner decoder. Regenerate old
artifacts from source or complete retained checked IR; no historical reader
or converter is required. Header patching is not conversion.

## Failure, binding, verification and lowering

Preserve the accepted failure distinctions without adding a size ceiling,
portable minimum, Terrum exception or machine-protocol field:

| Condition | Required disposition |
|---|---|
| Mathematical placement/rounding/product exceeds target nuint | Invalid target geometry, never wrapped/saturated storage or capacity exhaustion. Preserve the selected source/Standard operation's existing failure projection. For private 104 input use `ModuleFormatException`; for inconsistent producer/binding facts use `ModuleContractException`. |
| Wrong private schema/framing/reference, contradictory representable layout or proven inline cycle | Reject under the existing private format/contract channels before affected use. No smaller or partially verified layout is published. |
| Valid geometry needs an unsupported representation/native mapping | Decline that requested use as unsupported; private 104 uses `NotSupportedException` identifying the required mapping. Do not make type formation invalid or substitute a pointer, scalar/default value or private path. |
| Known processing, encoded-output, materialization, frame or image capacity is insufficient | For the new private-104 operation use existing host `NotSupportedException`, identifying operation/scope and exhausted dimension; do not wrap it as `ModuleFormatException`/`ModuleContractException` or manufacture target overflow. No new exception subtype or diagnostic number is selected. |
| Physical memory, I/O failure or cancellation | Retain its host failure path, distinct from deterministic exhaustion. Do not synthesize a limit receipt or normal program return. |

The new private boundary does **not** broaden A3's
`TrRmResourceLimitException`, its ledger/marker or diagnostics. A2's outward
API/CLI failure projections remain unchanged; L1/native capacity behavior
continues under its existing operation contract. Shared helpers must retain
the selected operation context rather than exposing the private-104 failure
mapping through every caller. Internal failure reasons must not be lost
merely because an existing outward projection groups invalid layouts.

* Source binding and required verification establish the same layout,
  initialization, Copy, validity, backing and access obligations as before.
  Keep per-function verification after loading/meaning-changing rewrites.
  Sparse Repeat/coverage analysis scales with actual stored state/paths,
  not N or B; reserve actual work/state before expansion.
* Module binding resolves original expectations and actual provider origins
  before comparing complete E/N, fields, Packing/Alignment, full-width
  geometry and retained observations. Equal low words or byte extents do not
  establish identity/compatibility. Invalidate affected layout/verification/
  native caches after a changed binding; never retarget live captured places.
* Re-encoding decodes and rebuilds type/pointer/array/function/import/field
  references in their namespaces and using-Module contexts. N/S/B/A/R,
  size and offsets are quantities, not indexes. Standard rewriting retains
  its complete TypeMap/Home/record/Block/SourceMap obligations; changing host
  widths changes none of those wire domains. Rebuild lengths/digests, not
  matching octets, and preserve bodyless versus full-artifact closure.
* Consumers perform mandatory schema/framing, bounds, reserved-field,
  complete-type/layout, requirements, support and actual-binding checks
  before use. They need not reconstruct source or semantically re-prove
  truthful published Body facts; no serialized “wide/verified” authorization.
* Native planning checks the complete actual allocation, alignment,
  temporaries, probing, displacement, PE and unwind ranges **before**
  payload allocation/code publication. Existing signed-int frame limits
  remain backend capacity, not type bounds. Use valid encodings or decline;
  no hidden heap, truncation or changed target equations. Preparation failure
  executes no entry and preserves any existing requested output.

## Required acceptance cases

These are complete acceptance gates, not a claim that every row has run.
Use public compiler APIs and independently assembled byte vectors; do not
allocate/execute huge payloads.

| Gate | Positive, negative and boundary requirements |
|---|---|
| Wide nominal source/Standard facts | With H=`0x1_0000_0001`, form a Packing=1 Sequential of byte, H-element byte array, byte. Retain field offsets `0,1,H+1`, field extents `1,H,1` and aggregate size/stride `H+2`; use it as an array element and nested field. Type-only source/Standard load, verification, binding and re-emission succeed compactly without a Body allocating it. Exercise A2, A2R1 and L1 only under their own explicit selections. |
| Positive-stride Body preservation | Under A2R1+Repeat and separately L1, form a compact scalar Repeat with positive stride and extent above int/u32. Load, verify, rewrite, re-emit and reload without native materialization. Do not expand N list elements; retain full-width paths/coverage and actual operation support. Reuse existing max-u64 zero-stride and max-u16 map/record regressions, without calling them positive-storage evidence. |
| Unsigned/overflow boundaries | Geometry around 8192, int.MaxValue, uint.MaxValue and long.MaxValue, including their first larger values. S=1,N=max-u64 is layout-valid; S=8,N=`0x1FFF_FFFF_FFFF_FFFF` fits, its successor overflows. Check field-end, final-round-up, nested-offset/product overflow, including N=0 with an invalid E/stride. Keep queries' independent nonnegative nint bound. |
| Private exact bytes | Check independent u64 operand vectors, zero/high-bit/max values and the `4+8*m` record delta. Round-trip wide struct/field geometry and unrelated small scalar/aggregate/array artifacts through exact 104. Reject truncation, wrong geometry, inline cycles, wrong type namespaces, negative legacy class geometry and honestly declared 103/unknown revisions without retries. Managed class bytes remain i32 and unsupported wide managed uses decline. |
| Binding/remapping/embedding | Permute nominal/pointer/array rows, imports/providers and fields/maps consistently; retain original expectations, actual origins, high-word offsets and distinct zero-extent identities. Reject changed E/N, Copy, field order/type, Pack/alignment or full-width layout even when low words match. Source-free private full/reference/consumer/set/graph re-emission rebuilds embedded lengths/digests under unchanged outer 3; an honestly inner-103 Contract still rejects. Standard L1 providers remain explicitly L1. |
| Capacity and host failures | Fixed-size type/field/seed graphs with increasingly wide B/N use bounded metadata/state, not B-byte buffers. Separately test actual processing/output limits at and just beyond the offered inclusive envelope. Valid huge native-frame requests decline before allocation/publication, distinctly from target overflow; retain sentinel outputs and perform no entry call. Test physical allocation failure separately from deterministic capacity. Do not introduce a new arbitrary layout limit to make the fixture pass. |
| Small executable preservation | Independently qualify small packed/aligned/nested arrays and aggregate calls through the selected source-free Standard/private paths and supervised headless JIT/AOT, preserving once-only effects, initialization, mutation/readback, probing/unwind and output/lifetime obligations. Successful metadata round-trips are not runtime evidence. |

## Integration boundary and remaining decision

No normative owner needs a new equation, source production, Standard field
or allocation. On approval, this proposal becomes the private successor/
wide-carrier owner alongside the existing source, A2, Repeat and L1 owners;
implementation references would describe actual support only after separate
implementation and qualification. This task changes no such inventory.

**Remaining approval decision:** adopt or replace the exact private-104
three-role u64 geometry amendment, its shared unsigned carrier and scoped
failure boundary. No additional in-scope encoding or cap is left to an
implementation fallback. Public/exported constructors, whole-array call ABI,
managed/owning storage and other schema compositions retain their independent
decisions; widening geometry neither selects nor completes them.
