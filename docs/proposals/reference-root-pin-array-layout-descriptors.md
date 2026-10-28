# RootPin fixed-array layout descriptors

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract direction.** This is a focused amendment to
[RootPin loaded-type descriptor admission](reference-root-pin-loaded-type-descriptors.md),
not normative adoption, implementation support or runtime evidence.
It supplies only the missing fixed-inline-array layout/traversal form.

## Decision and preserved boundaries

Recommend one closed **FixedArray** Value shape: one exact element-type edge,
a fixed element count and a checked byte stride. Use it for independently
admitted, homogeneous inline storage, including arrays nested in aggregates
or other arrays. Elements are indexed subplaces, **not FieldDefs**.

The amendment replaces only the prospective supplementary descriptor ABI
`windows-x64-root-pin-loaded-types/1` with
**`windows-x64-root-pin-loaded-types/2`**, using the same provider/domain and
the revised native records below. All unamended identity, admission,
origin-retention, retirement and lifetime rules of the loaded-type candidate
continue to apply. This is one replacement candidate, not an instruction to
support both descriptor ABIs or reinterpret an already agreed V1 snapshot.

It does **not** change:

* [RootPin operations](reference-root-pin-runtime.md), their value envelopes,
  statuses, twelve-entry **104-byte** helper table, **112-byte**
  `RootPinRuntimeInfoV1`, or `TerrumRootPinGetRuntimeV1`.
* The existing 40-byte PlaceRequest, 24-byte SupportSlot, opaque cell
  contract, allocation/construction boundary or complete GC retirement thunk.
* [B + RootPin schema selection](trrm-root-pin-schema-selection.md):
  `trrm/1-draft-root-pin-runtime-1` / `RootPinRuntimeDraft`,
  Feature `14/1`, TargetProfile `(2,1)`, F0 and ReferenceSupport Body `0D`.
  The four F selections retain NativeAddress `0D`; no F/G/RootPin composition
  is selected.
* Any normative owner, source constructor, Core array registration, source
  indexer, TypeSpec form, wire record, Feature, opcode or private revision.
  The native loader records here are not serialized semantic interchange.

Dynamic/unknown-size arrays, capacity-bearing collections, slices, arbitrary
strided views, variable-length payload tails and external managed storage
remain outside this decision. No array construction, allocation, initialization,
ownership or ordinary shared-backing support follows from a descriptor.

## Existing owners and the missing physical form

The following owners are reached through the [specification index](../specs/README.md).

| Owner | Preserved contract and remaining boundary |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [constant generics](../specs/language/constant-generics.md#canonical-value-identity) | Preserve the selected constructor/nominal origin, complete element type, ordered arguments, exact constant domain/value and qualifications. Equal sizes or integer carrier bits do not supply array identity. |
| [Grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments) | `InlineArray<T, N>` illustrates ordinary type/constant argument syntax, not a selected storage intrinsic or new array constructor. This proposal introduces no source spelling or name-based recognition. |
| [Lifetimes](../specs/lifetimes.md#erased-interface-storage-is-explicit), [reference formation](../specs/language/references.md#formation) | Inline element references depend on receiver storage and matched access; managed interiors retain their containing GC root/relocation relationship. Capture the selected place once, without rooting a vacant value or upgrading access. |
| [Element places](../specs/il-instruction-set.md#fields-elements-and-ranges), [checked index access](../specs/runtime/standard-failures.md#3-checked-index-access) | Evaluate base/index once and check the original index against the captured extent before narrowing or address calculation. PlaceSelection's bound index operation is not a FieldDef identity. |
| [Registered queries](../specs/language/registered-queries.md#selected-query-forms) | A fixed cardinality must be an explicit complete-shape fact, not inferred from byte size. Count/layout query results retain their nonnegative `nint` range and fixed/coupled dependency rules. |
| [Windows layout §5](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride), [§6 access](../specs/targets/windows-x64-data-layout.md#6-access-padding-and-observations) | Completed element layout, alignment, padding and checked `nuint` count-times-stride govern storage. Packing does not relayout nested elements or erase explicit alignment. Actual access alignment can be weaker than natural alignment. These rules select no runtime array descriptor or managed-array header. |
| [Windows target cases](../specs/conformance/target-windows-x64.md#3-aggregate-layout-and-access) | WXAG-12 requires three 8-byte elements to occupy 24 bytes at stride 8; WXAG-13..16 preserve checked arithmetic, truthful layout, storage-cycle rejection and exact packed access. They are required outcomes, not implementation evidence. |
| [Binding](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities), [consumer checking](../specs/representation/reader-trust.md) | Bind original subjects before comparing contracts, check structural consumption and required support, and preserve producer truth obligations without requiring semantic re-proof of authored bodies. |
| [RootPin relocation](reference-root-pin-runtime.md#relocation-and-exact-place-access), [loaded shapes](reference-root-pin-loaded-type-descriptors.md#closed-traversal-shapes) | RootPin already requires exact captured array-element selections through relocation. The eight descriptor shapes have no repetition/count/stride form; TypeFieldV1 keys denote real immediate owner/FieldDef subjects. |

Consequently, neither a fake field per element nor an opaque Leaf for a
rootless array completes the typed lookup contract. A Leaf loses even scalar
element subplaces. An independently admitted array representation and its
complete identity/layout/retirement facts remain prerequisites; this proposal
does not manufacture them from the name `InlineArray`, a matching struct,
an indexer, a constant argument or a known byte size.

## Alternatives

| Candidate | Assessment |
|---|---|
| **One repeated TypeNode shape; recommended** | One typed edge and two `u64` quantities describe any admitted fixed count without expanding elements into metadata. Nested repeats compose directly. Widening the cold node record costs 16 bytes per node and requires an explicit supplementary ABI replacement. |
| Expand elements into a tagged field-or-element subplace table | Coherent only with a new element-index discriminator, never fabricated FieldDefs. It broadens the field identity/range contract and needs one entry per element, potentially the product of nested counts. Existing `u32` graph/range counts would impose unrelated expansion limits. There is no corresponding lookup or identity benefit for homogeneous fixed storage. |
| Separate repeated-layout side table | Could save space when almost all nodes are nonarrays, but adds a batch count/pointer, ownership/link validation and another index namespace. There is no demonstrated cold-metadata constraint justifying that machinery for two quantities and one existing child edge. |
| Derive count/stride from parent and child sizes | Loses an explicitly required count, cannot distinguish zero-count/zero-stride shapes, and admits padding/divisibility guesses. The provider cannot recover full semantic shape from an opaque loader key. |
| General runtime length/stride callback or dynamic array shape | Adds value-dependent layout, publication/length synchronization, capacity/initialization and allocation contracts absent here. It is not a narrower solution to fixed inline storage. |

Reusing unrelated retirement fields or field-range words as count/stride
would keep an 80-byte extent only by changing their closed meanings.
Appending explicit fields is clearer than such a union of unrelated roles.
The new export/revision below prevents guessing the node stride; preserving
an unpublished V1 number is not a compatibility requirement.

## Exact supplementary ABI amendment

The same admitted `terrum-root-pin-runtime.dll` instance supplies:

```text
u32 __cdecl TerrumRootPinAdmitLoadedTypesV2(
    Domain* D, const RootPinTypeBatchV2* batch,
    LoadedType** vacantOutTypes, u32 outCount)
```

This is **machine interface notation, not Terrum source**. Register positions,
status values `0..3`, transactional publication, caller span obligations,
same-provider checks and no-source-code/no-native-unwind behavior are exactly
those of the loaded-type candidate. Resolve V2 explicitly before affected
use; missing export/unsupported revision rejects. Do not call V1 with V2
nodes, try alternate strides, append an operation-table entry or fall back
to a private collector.

`RootPinTypeBatchV2` is **72 bytes/alignment 8**, little-endian:

```text
u32 abiRevision=2, u32 byteSize=72
u32 targetProfileId=2, u32 targetProfileRevision=1
u32 nodeCount, u32 fieldCount, u32 originCount, u32 originUseCount
const TypeNodeV2* nodes
const TypeFieldV1* fields
const TypeOriginV1* origins
const u32* originUses
u32 flags=0, u32 reserved=0
```

Its offsets remain: header/profile at `0..15`, counts at `16..31`, pointers
at `32/40/48/56`, flags/reserved at `64/68`. TypeFieldV1 remains 32 bytes,
TypeOriginV1 40 bytes and origin-use entries four bytes, with their original
alignment, identity, extent and reference rules.

`TypeNodeV2` is **96 bytes/alignment 8**. Its first 80 bytes retain the V1
fields and offsets:

```text
u64 typeKey
u32 role, u32 shape
u64 size, u64 alignment
u64 retirementKey
u32 child, u32 fieldFirst, u32 fieldCount
u32 originFirst, u32 originCount, u32 retirementOrigin
u64 retireGcPayloadThunk
u32 layoutKind, u32 flags
u64 elementCount, u64 elementStride
```

The new fields are at offsets **80 and 88**, not a variable tail or a
TypeFieldV1 payload. The `nodes` span has checked extent `nodeCount * 96`.
All nodes use that stride, including nodes with shapes 1..8.

Append exactly **`9 FixedArray`** to the descriptor-local `shape` domain:

| Field | Required meaning |
|---|---|
| `role` | `1=Value` only. A GC box containing an array still uses GcPayload BoxPayload with the array Value child; a class payload still uses Fields. |
| `child` | Required ordinary batch-local index of the exact complete element Value node E, including all element qualifications. |
| `elementCount` | Explicit fixed nonnegative count N of this complete array type, never live length or allocation capacity. |
| `elementStride` | Completed byte stride S, validated below; not an element index, node index or FieldDef key. |
| `size`, `alignment` | The complete inline repeated-storage extent and object alignment below. |
| `fieldFirst`, `fieldCount`, `layoutKind` | All zero. No field range, synthetic field or new LayoutKind is assigned. |
| Value retirement fields | `retirementKey=0`, thunk zero, retirementOrigin `FFFFFFFF`, as for every Value node. |
| `flags`, origins | Existing checked Copy flag and complete origin closure rules. Shape alone neither grants Copy nor supplies a retirement recipe. |

Shapes 1..8 require `elementCount=elementStride=0`; their role, child, field,
presence and retirement restrictions otherwise stay unchanged. A supported
fixed repeated-storage value uses shape 9 even when rootless, count zero or
count one; it cannot alternate between Leaf, Fields and FixedArray for the
same key. Genuine wrapper fields remain Fields and may name a FixedArray
Value child through their real field keys.

All existing graph references remain zero-based `u32`. `FFFFFFFF` is still
the sentinel only in `child`/`retirementOrigin` where absence is permitted;
FixedArray.child cannot use it. Count/stride are `u64` quantities, not graph
references: zero is meaningful and `00000000FFFFFFFF` is not a sentinel.
Shape 9 is unrelated to Feature 9, TypeSpec forms, F0 actions or Body kinds.
No TRRM namespace gains an assignment.

## Fixed storage, zero and alignment

The admitted repeated storage starts at offset zero, has no header, hidden
length, inter-element state bytes, spare capacity or extra tail. Let E's
completed size/alignment be `s/a`. For this shape select:

```text
A = a when a != 0, otherwise 1
S = checkedRoundUp(s, A)
size = checked(N * S)
alignment = A
```

The zero-alignment branch applies only to the independently supported
zero-data unit E with `s=a=0`; it does not admit an unknown/underaligned stored
type. An array is not itself void and has positive object alignment, including
when its extent is zero. A zero-extent nominal E, if independently supported,
retains its own positive alignment and nominal identity. Existing gates on
zero-extent nominal layout/borrowing are not completed by this formula.

N, sizes, stride, rounding, products and cumulative offsets are checked in
the Windows x64 `nuint` domain before allocation/access. A submitted stride
must equal S and the node's size/alignment must equal the derived values.
No negative count, signed reinterpretation, narrowing to `u32`, saturated
product, sentinel extent or count inferred by division is permitted.
Physical `u64` fields do not select a new source constant-formal domain.
The loader preserves the count's original canonical domain/value and every
retained query obligation; required count/size/alignment observations outside
`0..nint.MaxValue` decline under the registered-query owner.

These are descriptor admission rules for this bounded storage form, not a
new universal array ABI. A truthful selected representation needing a header,
different stride/alignment, extra state or execution-dependent extent is
unsupported here. It does not become this shape by omitting those bytes.

* **N=0:** size is zero; still validate E, its complete layout, S, origins and
  graph. There is no element, scan or support residence. Do not compute
  `N-1`, form an element address or relax the child/cycle checks.
* **S=0:** E has zero extent; size is zero for every admitted N. Preserve the
  logical count and exact type. Do not divide by zero, enumerate physical
  residences, expose void bookkeeping or choose a neighboring byte as an
  element address. Exact roots/borrows of zero-extent elements and the whole
  zero-extent array remain under the existing unsupported zero-extent gates.
* No bytes does not mean initialized, present, Copy, dependency-free or
  retired. In particular, an independently required logical element Cleanup
  cannot be skipped because the stride is zero.

For nonzero-alignment E, element object alignment remains `a=A`; unit elements
have no placement requirement while their container uses alignment one above.
E's transitive explicit alignment requirement is preserved by loader layout
derivation, including enclosing packing; the array does not repack E or
strengthen a packed member inside E.
Array occurrences inside an aggregate use the actual alignment supplied by
that admitted containing layout, not automatically A. An unsupported
zero-extent placement rule still rejects.

For an occurrence with guaranteed actual base alignment K and checked element
displacement `i*S`, the guaranteed element alignment is K at displacement zero,
otherwise `gcd(K, i*S)`. Descending a genuine field also intersects that
occurrence guarantee with its supplied field alignment and checked offset.
Retain this evidence through nested arrays/aggregates and relocation.
PlaceRequest.knownAlignment cannot claim more than the selected path and
registered payload guarantee; an accidentally aligned current address is
not a relocation-stable proof. Pinning improves neither alignment nor access.
Use independently supported exact unaligned accesses or decline the use;
native typed projection still needs its stronger promised alignment.

## Exact element lookup and captured selection

RootExact first applies the existing complete-node/type/extent and payload
bounds checks. FixedArray adds this typed descent, not arbitrary range lookup:

1. A request at the node origin can match the **whole array** only with its
   exact complete array Value descriptor and extent. This cannot expose a
   zero-extent root through the existing gates.
2. To select within an element, require `N>0`, `S>0` and a relative offset
   `d < N*S`. Compute `i=d/S`, `r=d%S`; require `i<N`. The path is to E at
   checked offset `i*S`, then to an admitted initialized subplace within E
   at relative offset r. For an eligible positive-extent request, require
   `r<s` and `extent<=s-r`. Recheck exact requested type/extent and containment
   within E, not just within the outer payload.
3. E itself matches only at `r=0` with exact E and its complete extent.
   Otherwise descend its supported inline graph: real Fields, further
   FixedArray nodes and established Optional presence. An initially selected
   BoxPayload retains its existing offset-zero child exposure; reaching an
   ObjectCell element does not expose that allocation's BoxPayload.
   Padding and stride gaps are not places.
   An element of handle/reference type is its descriptor **slot**, not the
   pointed-to object or referent; descriptor-to-payload/referent edges are
   never inline lookup edges.
4. Preserve immediate storage authority along the complete containing path.
   A readonly array/containing path cannot yield writable element slots.
   A writable handle stored in a readonly slot retains its separate inner
   access. Compare complete types exactly; attenuating acquired access does
   not merge their identities or permit a same-size substitute.

The source producer checks the original evaluated index before multiplying,
narrowing or calling RootExact. For an admitted standard checked access,
negative, too-wide and one-past-end indices retain IndexOutOfRangeException
and publish no element place/root; an earlier evaluation failure remains
that failure. User-defined indexers retain their own bound contracts.
The provider checks the submitted physical typed match, not source index
truth. PlaceRequest has no source-index/path field: an invalid array index
cannot be made valid by landing on a separately valid sibling place, and the
provider is not promised to detect that producer lie.

Nested selection composes checked offsets from the containing payload:
real field offsets and each captured `i*S`. Preserve allocation/place
generation, exact type, final extent, actual alignment, access ceiling and
all independent backing/owner/guard dependencies. RootCell's existing
payload-relative offset suffices for positive-extent fixed storage; no new
cell field or helper parameter is required. Relocation changes only the
ObjectCell payload base. It must not reevaluate an index, reread a rebound
receiver, choose a new element or follow the old element's stored handle.

## Traversal, identity and atomic admission

**Traversal.** Scan each actual element's typed descriptor graph, including
Some-only traversal and existing object/root/pin edges. Copy/support retirement
retains/releases each existing element residence exactly once; it creates no
new root/pin and does not destroy E's referent or call authored Cleanup.
Zero count/stride causes no storage read or support residence. A complete
relocation transfers all these residences without another logical Copy.
The node is a compact repeat, not N cloned descriptors or N unconditional
collector roots; existing object/support-cycle tracing remains required.

Complete payload retirement is still the already admitted adapter's job,
including the array's applicable element retirement/ordering and zero-byte
logical obligations. This proposal selects no new array Cleanup order,
partial-element publication scheme or ownership recipe. If that complete
recipe, element storage/tracing or required origin/codec is unavailable,
reject affected admission/use rather than infer it from N or Copy.

**Identity/interning.** Keep `(D, role, typeKey, targetProfile 2/1)` and the
one loader's full canonical subjects. Normalize the element reference to E's
full role/key, then include shape, N, S, size/alignment, flags and retained
origin/retirement relationships in the exact graph comparison. Two different
array constructors, nominal origins, element qualifications, constant domains
or counts never merge because their extents match, including zero extents.
Repeated AOT/JIT admission of the same subject returns the same LoadedType
pointer. A conflicting repeat, changed child or old Leaf description for an
already interned key rejects; there is no in-place descriptor upgrade.

**Cycles.** FixedArray.child is an inline-storage edge even at N=0 or S=0.
Add it to the existing Fields/Optional/BoxPayload storage-cycle check.
Any all-inline cycle rejects before publication; an empty count does not
hide recursion or license infinite type substitution. Independently valid
ObjectCell/RootCell/StableRef/StableClass edges continue to break storage
expansion under their existing roles. Validate the entire finite graph with
a complete worklist, not a depth cutoff that treats unvisited nodes as valid.

**Admission.** The loader first supplies exact closed type/cardinality,
binding, target/layout, Copy and complete retirement/origin facts. The
provider checks revision 2, every 96-byte node, shape/role/child domains,
zero extension fields on shapes 1..8, fixed-storage equations, ranges,
actual placement bounds/alignment and cycle freedom, including otherwise
unused batch entries. Count zero bypasses none of that work. It copies the
repeat data and retains actual element/layout/code origins in the existing
domain-lifetime graph, never pointers into the batch.

Keep the original all-or-nothing publication and loader statuses: `1` for
metadata/resource capacity exhaustion, `2` for malformed/conflicting graph
data, `3` for unsupported revision/representation/retirement support. Overflow
in submitted framing/layout and inconsistent stride/extent are not truncated
success. Unsupported source layout or missing codecs may reject at loader
preflight without a call. No failure publishes nodes/output words or mutates
existing descriptors/objects. No admission itself allocates/publishes a GC
object, initializes an array or acquires support.

**Remapping and replacement.** Reorder/deduplicate batch nodes only while
remapping the child, real field value-node, origin and origin-use references
in their own namespaces. N and S are values, not row references; leave them
unchanged. Compare normalized full keys, not input indexes, record addresses,
row numbers or hashes. Preserve real field declaration order and original
binding expectations. Re-encoding semantic artifacts retains complete type/
constant facts and existing element-place operations; the loader reconstructs
fresh target binding data afterward. No native descriptor, key, callback or
live selection is serialized. A changed element/count/layout invalidates
dependent observations and generated data under existing replacement rules.

## Focused acceptance cases and remaining gates

These are required future cases, not tests run or runtime claims. Runtime
cases assume the independently admitted registered-object boundary.

| Case | Required outcome |
|---|---|
| RPA-01 Scalar elements | An independently admitted three-element array of a size-8/alignment-4 aggregate has S=8, extent 24 and alignment 4. Root first/last elements and their real fields; reject padding, an interior byte as a whole E, wrong extent and same-size different types. |
| RPA-02 Nested storage/relocation | Use unrelated arrays of arrays and aggregates containing arrays, then arrays containing aggregates with real array fields. Capture nonzero indices once, end/rebind the original handle/index locals and force repeated relocation. Each root still selects the original allocation/element/field; no flattened product becomes a field identity. |
| RPA-03 Descriptor elements | Arrays of optional GC handles, RootCell descriptors and stable pin-supported values scan/copy/end each actual residence once. None reads no payload; handle-slot roots do not follow a replaced handle. Relocation does not duplicate holds; an unreachable support cycle is not a global root. Unsupported support-bearing Union overlap still rejects. |
| RPA-04 Empty and zero data | N=0 with nonzero E still records/validates S and E, has extent zero and no element. Independently admitted zero-data E retains N with S=0 and no physical access. Reject element/whole zero-extent rooting through this candidate; preserve initialization/type/retirement facts and reject unsupported children even when empty. |
| RPA-05 Arithmetic and bounds | Check zero, count one, last valid index, `00000000FFFFFFFF` as a count, maximum representable products/offsets and overflowing round-up/product/nested sum. Reject negative/too-wide/one-past source indices before address arithmetic, including a one-past offset coinciding with a sibling. Preserve retained `nint` query limits and distinguish capacity exhaustion. |
| RPA-06 Access/alignment | Preserve readonly containing paths versus writable element handles, packed occurrences, over-aligned elements and packed members inside elements. Actual alignment follows the whole path across relocation. No write upgrade, manufactured aligned reference or pin-induced repair; unsupported native typed projection rejects. |
| RPA-07 Shape and cycles | Reject payload-role FixedArray, missing/wrong-role/sentinel child, nonzero field range/layoutKind, unknown shape 10 and nonzero repeat fields on shapes 1..8. Reject direct/indirect all-inline cycles even at N=0; admit valid handle-mediated nominal recursion without expanding repeated elements into metadata. |
| RPA-08 Identity and remapping | Re-admit independently bound AOT/JIT graphs with permuted node/origin arrays and remapped indexes. Preserve descriptor pointer identity and exact selected elements. Distinct nominal/qualified/count-domain subjects stay distinct at equal/zero sizes; conflicting N/S/child/origins for one key reject atomically. |
| RPA-09 ABI/framing | Use only the same-provider V2 export, revision 2, 72-byte batch and 96-byte node stride. Reject V1/80-byte mixing, truncated spans, nonzero reserved fields and failed prospective agreement before use; outputs remain zero on failure. Retain the unmodified V1 104/112-byte RootPin helper discovery. |
| RPA-10 Scope gates | Reject dynamic length, unknown/scalable element layout, spare capacity, variable tail, unsupported array initialization/retirement and absent semantic codecs. No name-based array admission, fake FieldDefs, source factory, new wire record, implicit construction or F/G/RootPin composition. Inspection/descriptor admission alone proves no execution. |

The concrete decision is the repeated shape, bounded layout/lookup rules and
supplementary V2 handoff above. Adoption and implementation remain separate.
On adoption, integrate this amendment with the loaded-type candidate in the
owning target/runtime handoff; preserve the normative source, layout, binding,
consumer-trust and schema authorities. Compiler/binding/verification/lowering
and runtime qualification must carry these exact facts before claiming support.
No implementation, additional proposal, conformance-inventory or normative
document change is made by this documentation slice.
