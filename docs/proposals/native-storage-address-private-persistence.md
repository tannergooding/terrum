# Native storage-address extents in private persistence

**Status: unapproved; explicitly provisional and actionable only under the
user's authorized missing-contract workflow.** This document changes no
compiler, test, normative specification or support claim. Provisional
direction is not formal approval or format stabilization.

## Decision and boundary

The original recommendation was **private semantic-module i32 revision 99**,
with exact draft identity **`terrum/private-semantic-99-storage-address-u64-1`**.
It composes the revision-92 schema baseline described below; that is not the
current writer selection. The complete candidate schema is the revision-92
layout, including the provisional
[FieldMove tag-42 amendment](owning-field-move-private-persistence.md), with
only the three tag-35 extent operands replaced by little-endian **u64**.
All other fields, tags, type identities and admission restrictions remain
those of that base.

A distinct exact schema is required for this width change. Private entry
points select one layout by the i32 header and provide no separate selector
for two interpretations of a given revision. Changing its reader to
u64 while retaining 92 would silently reinterpret narrow artifacts;
narrowing the writer back to i32 would not preserve the widened bound
contract. Use the new private revision rather than either workaround.
This is an implementation-specific schema decision, not a claim that
[pre-release TRRM changes require a version bump](../specs/representation/format-versioning.md).

Revision 99 is deliberately **92 plus this delta**, not a numerical
composition of candidates 93–98. It adds no general dependency collection,
wide scalar codec, copy-origin summary, foreign/interface directory,
checked-reference codec or private fixed-array codec. Those retained facts
continue to require their independently selected complete representations.

## Historical source baseline and allocation check

The source observations below are retained from the proposal's earlier
revision-92 inspection; they are not current support claims or test results.
The current structured writer/reader selects revision 104 and carries u64
tag-35 extents, as tracked in the
[module inventory](../implementation/modules.md) and
[conformance handoff](../implementation/conformance-gaps.md). The revision-99
candidate remains an exact unapproved historical delta, not the current
writer schema.

| Inspected owner | Relevant fact |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundNativeStorageAddress` | `ElementCount`, `ElementStride` and `ByteExtent` are now `ulong`; the node also retains its local, index, carrier/element/address types and readonly fact. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader`, `PutExpression`, `GetExpression` | In the inspected snapshot, writer/header selection was exactly 92; tag 35 wrote three `ulong` operands but read three `ReadInt32` operands, causing 12-byte misframing. The current revision-104 codec reads/writes u64 quantities; the old mismatch is historical and not a current decoder claim. |
| [Private module reference](../implementation/modules.md) | Revision 92 carries resolved arithmetic modes, NoThrow retirement, declaration-owned native outputs/output dependencies and reservation tag 41; tag 42 is a provisional private amendment. Private persistence remains distinct from standard interchange. |
| [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), `ReadGraphLibrary` | Graph-library decoding shares the header/function codecs but has its own module reconstruction and validation path. Ordinary reads alone are not the migration boundary. |

The repository's private revision constants, proposal catalog and proposal
compositions were checked, including working/untracked proposal documents:

| Private revision | Existing owner |
|---|---|
| 92 | Historical base layout and its FieldMove amendment for this revision-99 candidate; not the current writer selection |
| 93 | [General declaration dependencies](reference-provenance-carriers.md#private-exchange-candidate-revision-93) |
| 94 | [Wide scalar value carriers](scalar-wide-value-carriers.md#exact-private-schema-and-composition) |
| 95 | [Field-value copy-origin summaries](reference-field-value-provenance.md#necessary-private-transport-candidate-95) |
| 96 | [Foreign-interface identity](native-output-foreign-interface-private-persistence.md#exact-revision-composition) |
| 97 | [Native convention composition](native-calling-convention-private-persistence.md#exact-composition-and-selection), with the linked native, enum and [checked-reference amendments](checked-reference-private-persistence.md) |
| 98 | [Source interfaces and witnesses](source-interface-witness-private-persistence.md#exact-revision-composition) |
| **99** | No competing private assignment found; proposed here only |

An unknown `native-99` profile used in negative tests and unrelated numeric
99 operands are not private header allocations. Reassigning an unpublished
candidate is permissible, but doing so here would change another proposal's
composition without solving an additional extent problem. A broader
private-to-standard migration is also unnecessary for this bounded repair.

## Exact record layout

Keep the four-byte `TRRM` magic and write little-endian **i32 99**
(`63 00 00 00`) at header offset 4. This is not the standard u16
major/minor pair, a Feature ID, expression tag or native-profile number.

The complete tag-35 expression is:

| Offset from expression start | Field | Width and meaning |
|---:|---|---|
| 0 | `kind = 35 (23)` | u8, existing private StorageAddress tag |
| 1 | `type` | i32, existing private address-type identity |
| 5 | `spanStart` | i32, existing source coordinate |
| 9 | `spanLength` | i32, existing source extent |
| 13 | `variable` | i32, containing function's local-slot identity |
| 17 | `carrierType` | i32, existing private type identity |
| 21 | `elementType` | i32, existing private type identity |
| 25 | `elementCount` | **u64**, existing element-count quantity |
| 33 | `elementStride` | **u64**, existing byte-stride quantity |
| 41 | `byteExtent` | **u64**, existing formation's byte-extent quantity |
| 49 | `isReadonly` | u8, exactly 0 or 1 |
| 50 | `index` | One complete recursively encoded private expression |

The fixed prefix is **50 bytes**, formerly 38. An existing int32 constant
index has a 21-byte encoding, making that expression exactly **71 bytes**.
`AddressType` is reconstructed from the common `type` field, not serialized
again. There is no alignment padding, extent discriminator, optional trailer
or compact/legacy alternative. Producers write `ulong`; consumers read
`ReadUInt64` in the same order. A set high bit is not a negative value.
The nested expression retains its original type, span and scalar encoding.

This changes quantity widths, not the meanings of available elements,
stride, requested region, whole-storage versus indexed formation or
NativeParameter obligations. In particular, do not universally replace
`byteExtent` with `elementCount * elementStride`; an admitted requested
region may be smaller than complete backing.

## Producer and consumer selection

* Every structured writer selects **exactly 99**, including producers,
  consumers and graph libraries with no tag-35 expressions. Final selection
  must not fall back to 92 because values fit i32 or a table is empty.
* Every corresponding reader selects this complete schema before records:
  ordinary reference/link reads, graph-library reads, sets, graph edges,
  embedded semantic Contracts and contract snapshots. Reject honestly
  declared 92, candidates 93–98, older and unknown revisions at the header.
  An old exact-92 reader likewise rejects 99. No numeric roll-forward,
  `>=92` compatibility claim, per-expression dispatch or decode retry.
* Audit exact-membership gates for structure fields, invocation buffers,
  retained profiles and producer validation as well as the shared header
  and expression codec. Adding 99 to a list must select its specified
  fields, not make it an alias for 92. Cache/check reuse includes the exact
  schema and unchanged bytes, binding, target and runtime context.
* Keep current TNPK/TNSP/TNGP **outer version 3** layouts and native-profile
  labels. Each embedded semantic payload independently selects 99; the
  selected outer-3/inner-99 agreement rejects old or inconsistent inner
  selections. This width delta supplies no new outer field, ABI or profile.

Under [private persistence](../specs/representation/persistence.md), there
is no obligation to retain a historical private reader. Recommend
**regeneration**, not conversion support: recreate prior artifacts from
their actual source or independently retained complete checked IR, and
rebuild affected enclosing payloads, snapshots, lengths and checksums.
Existing machine images may be retained only under their separately
established unchanged semantic/target/binding obligations, not because a
header was edited.

The transient widened-writer/92 bytes do not define a second legitimate
92 layout. Never infer narrow versus wide from length, operand plausibility,
profile, filename or a failed decode. A future converter, if separately
authorized, must explicitly select the authentic narrow source schema,
reject negative old operands, decode and widen their values, and fully
re-emit the destination. Header substitution alone is not conversion.
Nor can a reader promise to identify every dishonestly relabelled old stream
that happens to be structurally valid under the new schema.

## Bounds, preservation and use

Producers must emit schema-valid bytes and truthful retained facts. Keep
current source/bound verification and caller-unsafe obligations; the width
change adds no serialized authorization or capability.

Before affected consumption, require:

* Complete prefix/child framing, canonical Boolean, valid spans, assigned
  expression/type forms and local/type-table reference bounds. Unsupported
  carrier families remain unsupported. Wrap truncation/overflow failures
  through the existing module-format diagnostic boundary.
* Checked unsigned geometry for the selected use, including `N*S` and any
  `i*S+B` containment calculation; reject overflow or an extent exceeding
  actual complete backing or the selected suffix. Preserve existing legal
  index/whole-storage and zero-extent/native-addressability restrictions.
  Do not truncate, clamp, wrap or impose i32 on wire quantities. A quantity
  representable on wire can still be unsupported by a concrete target or
  backing implementation; reject that use explicitly.
* Existing artifact/count/work limits: 4 MiB, 1,024 functions, 256 slots,
  100,000 nodes, depth 128 and 1,024-byte strings. Extents are quantities,
  not row counts or allocation requests; decode no element-per-count graph
  and allocate no backing merely to inspect the tuple.
* Existing binding, access, retention, initialization and backing/provenance
  checks needed by the admitted use. Mandatory structural safe consumption
  is distinct from re-proving arbitrary producer-published Body facts, as in
  [reader trust](../specs/representation/reader-trust.md).

Shared traversal, resolution and ordinary/set/graph remapping must retain
all three u64 values exactly. Map `Type`, `AddressType`, `CarrierType` and
`ElementType` in their proper private namespaces, preserve/remap the local
in its function context, and recursively resolve/rewrite `Index`, including
symbolic calls and its source span. Reordered structure, pointer and
function-pointer tables must not change geometry for unchanged backing.
Preserve original import expectations independently of the actual provider;
incompatible backing/layout or native bindings reject rather than rewriting
an extent to make replacement appear compatible.

Re-emission uses decoded typed IR and a newly selected writer. Rebuild
affected expression/container lengths and native-container checksums; do not
patch matching bytes or just the version. Preserve existing private
slot-flexible vtable replacement versus stricter native-package binding.

## Focused regression gates

Use public compiler APIs and independent byte fixtures, not new friend
assembly access. These are proposed checks, not passing evidence:

| Case | Required result |
|---|---|
| Exact selection | Fresh producer/consumer/graph-library artifacts, including scalar-only/no-address artifacts, declare 99. Their public readers reject 92, 93–98 and unknown headers before record decoding. Migrate existing exact-92 fixture assertions deliberately. |
| Independent width/framing | A valid scalar tuple `(1,4,4)` has extents at 25/33/41, Boolean at 49 and child at 50. Independently inspect all 24 extent bytes and the 71-byte constant-index form; decode adjacent expressions/statements without drift. A writer/reader round-trip alone cannot establish the width. |
| Unsigned boundaries | Independently constructed vectors exercise `int.MaxValue+1`, `uint.MaxValue+1`, bit 63 and `ulong.MaxValue` in each operand. Preserve high words wherever the selected backing supports them; otherwise reject for geometry/support, not signedness, truncation or a misdecoded following tag. Do not manufacture enormous runtime backing to claim coverage. |
| Malformed input | Truncate each extent and the nested expression; Boolean 2; trailing bytes; invalid slots/type references; unsupported backing; zero/non-addressable formation; multiplication/offset overflow and region overrun. Reject without retry or success-shaped fallback. |
| Public dependency paths | Restore `DirectNativeImportTests.DirectNativePointerInputCanBeCombinedWithOneNativeProducerDependency` and `NativeVTableCallTests.NativeVTableMethodSurvivesPrivateAndNativeModuleDependenciesAsync`; retain their pointer access, provider identity, incompatible replacement and set/graph checks. Extend a small synthetic scalar address fixture across both existing runtime policies. The vtable execution test is Windows x64-gated. |
| Projection/re-emission | Reorder struct/pointer/fnptr tables, include a traversable index child and adjacent output reservation/FieldMove, then read/remap/re-emit and compare exact geometry and original expectations. Exercise ordinary, set, graph-library and embedded-Contract paths. |

[`NativeOutputModulePersistenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs)
provides the active exact-revision, forged-record and type-reordering
patterns; the [direct-import](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs)
and [vtable](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeVTableCallTests.cs)
tests contain the affected dependency paths. They are not excluded by the
current [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj).
No tests or native execution were run for this documentation-only decision.

## Standard isolation and remaining choices

The separately provisional **F+S+A2** selection,
[`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`](standard-trrm-rootless-copy-type-evidence.md),
remains on the standard `(1,0)` framing with its own explicit schema
selector, TypeSpec `11`, FixedArrayLayout `0012` and Copy/Feature rules.
Its u64 array geometry does not select this private decoder, and its
StorageAddress record `0E` is not private expression tag 35. Private 99
adds no standard allocation or A2/private-array transport. Include negative
cross-format/schema cases; shared `TRRM` magic never authorizes fallback.

**Formal choices still awaiting review:** approve the candidate 99
assignment, its exact 92-based u64 layout, and exact-only admission with
regeneration. The proposal supplies those decisions provisionally; no width,
field order or historical-reader policy is left to the implementer.
An eventual composition with candidates 93–98 needs a separate explicit
whole-schema integration decision; their greater numbers or prerequisite
tables do not silently inherit this delta. Compiler migration and the
regression gates remain unimplemented by this document.
