# Private raw-Union return-coverage persistence

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This is provisional implementation direction,
not normative approval, implemented support or native qualification.

## Decision and owners

Recommend exact private candidate
**`terrum/private-semantic-106-raw-union-return-coverage-2`**:
the complete
[105 B+C+R selection](checked-reference-private-persistence.md#exact-schema-composition)
plus **U**, the mandatory declaration-owned coverage vector, and **A**, the
bounded aggregate-array persistence/geometry delta: **B+C+R+U+A**.
It includes already source-admitted rootless-Copy aggregate results with
inline fixed-array fields and raw Union in their inline graph, including
nested array/field compositions within the materialization/resource envelope. Bodyful and
bodyless `ModuleFunction` records retain the same promise.

This replaces the earlier array-free recommendation, not merely its wording.
The `-1` draft is withdrawn, not an alias or fallback. Select only `-2` for
this exchange, including its semantic catalog, before decoding. The i32
header remains 106 because the record shapes do not change; equal header
bytes alone do not select a draft. No per-function array opt-in is added.
There is **no new source grammar or call ABI**. Direct structural-array
results and by-value structural-array formals retain their separate ABI
gates; they are not required to transport an admitted aggregate's fields.
The in-scope aggregate-result cases are selected for implementation, not
left as unsupported/deferred work.

| Owner | Boundary preserved |
|---|---|
| [Specification index](../specs/README.md), [grammar layout rules](../specs/grammar-spec.md#4-source-semantic-constraints), [Windows x64 layout](../specs/targets/windows-x64-data-layout.md) | Raw Union is overlapping inline storage, not a tagged source union. Alternative access is unsafe; padding, validity, ownership and representation remain independent. |
| [Raw-Union direction](standard-trrm-raw-union-initialization.md#minimum-representation-facts-and-transfer) | Owns availability, possible payload `P(T)`, complete-value lower bound `G(T)`, actual definite coverage `D`, joins and overlap invalidation. Reuse those meanings, not its Standard bytes. |
| [105 declaration order](checked-reference-private-persistence.md#one-checked-publication-authority-per-declaration), [CopyOrigin](reference-field-value-provenance.md#the-bounded-callable-guarantee) | General Dependency, checked/NativeOutput state and entry-copy guarantees remain distinct. Existing normal-result and fresh-inline-construction phases are not replaced. |
| [Private arrays](native-fixed-array-private-persistence.md), [source arrays](standard-native-fixed-array-source-contract.md#declaration-contexts-and-indexed-use), [Windows x64 calls](../specs/targets/windows-x64-call-interface.md#4-by-value-copies-and-result-storage) | Replace the private metadata-only exclusion for already source-admitted bounded array-field aggregates. Reuse their existing aggregate call/result contract; do not admit structural-array formals/results, foreign array mappings or pointer-to-array values. |
| [Persistence](../specs/representation/persistence.md), [versioning](../specs/representation/format-versioning.md), [binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Exact schema selection, original expectations, producer truth and mandatory safe structural/binding checks; no required consumer semantic re-proof. |

### Why a carrier is needed

Inspected source/contract evidence, not newly executed tests:

* **Source admission exists.**
  [`SemanticVerifier.HasUnsupportedMetadataOnlyArrayType`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
  permits `IsRootlessCopyAggregateWithFixedArrayFields` on returns and
  field-array variables. The
  [predicate](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs)
  requires a non-array root, inline field storage, rootless Copy and valid
  signatures. Private
  [`IsRevision100RuntimeType`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  lacks that exception; this is a private transport gap, not missing grammar.
* **Geometry/summary paths are incomplete.** `GuaranteedInitializedPayloadBytes`
  repeats nested element masks, but `RawUnionReturnCoveragePossibleBytes`
  throws on a materialized array and `CarriesRawUnionPayloadFacts` excludes
  array carriers. `CarriesRawUnionArrayFacts` /
  [`RawUnionArrayFactsForCall`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.TokenSpans.cs)
  separately carry element snapshots/entry-copy facts. A must compose those
  facts with fresh published masks, not depend on CopyOrigin being present.
* **Local caches are not promises.**
  [`BoundFunction.RawUnionReturnCoverage`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  is a full-extent 0/1-per-storage-byte vector; its builder intersects normal
  snapshots. `VerifyReferences` computes but does not install immutable
  summaries; `TryVerifySourceCore` is the source path. NativeEmitter does
  not read this property. The exact 105 schema has no coverage field.
  Recomputing a loaded body cannot restore an original bodyless expectation.
* **Existing codecs/evidence are narrower.** Standard
  [L1 writing](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutBodyWriter.cs) /
  [reading](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutReader.cs)
  explicitly transport direct-Union promises; A2R1's
  [`AddArrayRepetition`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs)
  handles compact array geometry. Neither establishes this private route.
  [`PrivateNestedFieldPathBackingRoundTripsAndExecutes`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs)
  has a local four-byte constructor summary plus CopyOrigin; that alone is
  not stronger-than-G fresh array-field provider evidence.

The dense bound reuses
[`ValueLayout.MaximumStructureSize`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs)
(8,192 bytes); current capacity and source support do not qualify the revised
private writer, binding, source-free or native execution paths.

## Exact selection and framing

Use `TRRM` followed by little-endian **i32 106** (`6A 00 00 00` at offset 4),
not Standard's u16 major/minor pair. Keep 105's directory order, type codes,
record widths and operation allocations, with this header and the insertion
below. A changes their explicitly bounded value/subject admission, not their
framing. This supersedes 105 **for this selected exchange**, not unrelated
source/runtime owners or other candidate selections.
Retaining an A-domain aggregate runtime use selects this whole `-2` draft
even when every coverage vector canonically has count zero. A nonzero mask
is neither a feature discriminator nor a decoder/native-profile selector.

An amended exact 105 draft would be permissible under format versioning,
but would need a different whole-schema selection and coordinated decoder
change. Select 106 instead: the mandatory new extent is explicit, rather
than overloaded onto the current 105 reader/writer selection. This is not a
claim that unpublished 105 bytes are stable. A recomputation-only fix adds
no bytes but fails the bodyless requirement. Neither option justifies an
optional trailer, feature sniffing or an alias.

On **every ModuleFunction**, after its complete checked-output-state vector
and immediately **before its CopyOrigin summary-kind byte**, insert:

```text
i32 rawUnionReturnCoverageByteCount
u8  rawUnionReturnCoverage[rawUnionReturnCoverageByteCount]
```

Thus the metadata order is: general Dependency count/rows; checked selector
count/rows; checked output-state count/rows; **coverage count/bytes**;
CopyOrigin kind/payload; foreign-member variant; direct-import expectation.
The subsequent inherited fields and conditional Function keep their order.
The new field is outside the `hasBody` condition, including on nonexported
local functions, producer exports and declaration-only consumer/graph imports.
Existing `Import`/`hasBody` validity rules do not change.

The owner is the containing declaration, with at most this one vector.
There is no owner index, result-type duplicate, presence/tag/profile byte,
padding, Contract-string token, Body copy or alternate encoding.
The exact added extent is **`4 + byteCount`** bytes. Integers are
little-endian; each vector element is the byte value `00` or `01`.

* `byteCount=0` is the mandatory normal empty representation: **no additional
  published coverage beyond `G(T)`**. It does not mean full coverage, vacant
  result, no normal completion or an unsupported promise that may be ignored.
* A positive i32 count equals the completed storage extent `sizeof(T)`
  exactly; admitted materialized subjects have at most **8,192** bytes.
  Preflight actual remaining input and all resource limits before allocation.
  No shortened prefix, bit packing or implicit trailing zeros is admitted.
* Let `C` be the set of byte positions containing `01`. Require
  **`G(T) subset C subset P(T)`**. Zero means no stronger initializedness
  fact, not definitely uninitialized. Padding-only positions must be zero.
  A full-length `C=G(T)` vector is valid noncanonical input; canonical writers
  use count zero instead. Otherwise write the full mask, including `G(T)`.

This full-mask normalization matches call paths that replace a baseline
coverage array with a nonempty summary. A differently represented input,
including Standard regions, must first reconstruct `G(T)` union its promised
bytes; raw region payloads are not private mask bytes.

### Subject, layout and publication

Derive `T` and its result role from the existing declaration Contract in
**that declaration's owning type/binding context**. With a Function present,
its complete ReturnType and role must agree. Nonempty coverage requires a
closed inline result containing a raw Union in its type's inline graph.
Traverse ordinary fields, every Union alternative and every array element
type, **including at N=0**; pointer/reference pointees are not inline children.
Each raw Union retains its Windows x64 Blittable requirement `1`,
rootless-Copy alternatives and ordinary validity rules.

Preserve already admitted array-free cases. The new array-bearing slice
requires an already source-admitted **non-array-root, rootless Copy
aggregate**, validated fixed or explicitly layout-coupled Windows x64
geometry, and **`0 <= sizeof(T) <= 8192`**.
It includes arrays of aggregates inside fields, nested aggregate/array fields
and array-valued Union alternatives. Transparent wrappers participate only
under their existing source/target admission. Scalar/pointer/code-address
leaves retain their existing admission; arrays do not admit
GC/owning/checked-reference elements or turn
pointer/signature recipes into runtime values. Open generics, managed/unique
containers, structural-array result/formal ABI, foreign array calls and
arbitrary pointer-to-array values are not selected by A. Array nodes inside
P/G recursion or local value staging do not make them callable result roots.

Validate every complete element/alternative type even when its storage has
zero occurrences. A zero-count ancestor may retain a layout-valid wide
element recipe without materializing that element. This is precisely
zero-occurrence metadata support, not execution of an oversized value.
A positive materialized result larger than 8,192 bytes is outside this
private capacity even with count-zero coverage; it cannot be relabelled
metadata-only success or silently published with a weakened promise.

For an ordinary callable, `C` promises initialized bytes in its actual normal
result at normal publication, after its applicable checks and crossed cleanup.
For an existing inline-constructor `C` declaration, it describes its fresh
inline result at ordinary successful constructor completion. The existing
constructor role—not `#ctor` spelling or CopyOrigin presence—selects that
boundary. Subsequent caller initializer entries/final validation update facts
normally; this is not early complete-parent publication.
It is never a receiver-mutation, checked-init-output, raw-pointee,
exceptional-output or deferred-completion summary.

No result exists on a failing/nonreturning path. The vector supplies no
rollback, failure-state, completion or reachability guarantee. No observed
normal return means source derivation publishes no extra coverage, not an
all-ones mask by vacuity. Coverage alone establishes no typed interpretation,
pointer validity/provenance, backing, lifetime, ownership or active alternative;
unsafe access and every independent obligation still apply.

### Bounded aggregate values and private persistence

**A changes private transport admission, not source grammar or call
classification.** Apply the source verifier's rootless-Copy aggregate
exception consistently to private declarations, bodyless expectations,
ordinary aggregate-value formals, locals and retained field/type closures.
Do not implement it by permitting every type for which
`HasMetadataOnlyArrayType` was true: direct array formals/results,
pointer/signature edges and managed/owning carriers keep their own gates.
The nominal constructor result keeps its existing fresh-construction role.

Reuse each admitted aggregate's existing target calling convention,
independent by-value copies, result storage/alignment and zero-payload rules.
Preserve logical types/ordinals, source-order staging, invocation, cleanup
and publication even when no payload bytes move. No hidden array-decay
adapter, new reference mode, heap backing or universal ABI is selected.

Use the existing array table/type references inside aggregate return/formal
types, declaration strings, variables and expression envelopes. Admission is
structural, not conditional on a sample name or Union presence; a nonempty U
summary additionally requires raw Union in the result's inline type graph.

No new Body tag is needed. Preserve bounded recursive list/Repeat values in
43/47 while constructing/staging the admitted aggregate's fields, and
ordinary aggregate read/call/conditional/return positions.
Canonical field/nested element Copy reads and stores use R's
[PlaceRead 50 / CheckedStore 53](checked-reference-private-persistence.md#new-operation-schemas)
with recursive InlineField/FixedArrayElement places; retain 44/46 in their
existing flat contexts, without inventing longer payloads for those tags.
Captured roots/indices evaluate once; stage a returned value in lawful local
storage before treating it as a place. Preserve logical whole/child
availability, source generations and typed facts, not just its bytes.
N=0 construction is explicit; Repeat still evaluates its seed once at N=0.
Zero-extent element operations have bounds/effects but provide no payload
address. No active Union alternative, implicit zeroing or unchecked byte-blit
operation is introduced.

### Exact recursive payload geometry

Let `B(T)` be the validated complete storage extent. `P(T)` and `G(T)` are
sets of byte offsets in `[0,B(T))`; an offset is a quantity, not a row ID.
Use the following recursion, not a Union-only outer-type test:

| Complete subject | `P(T)` | `G(T)` |
|---|---|---|
| Admitted scalar, pointer or code-address cell | `[0,B(T))` | `[0,B(T))` |
| Unit/zero-payload carrier | Empty | Empty |
| Ordinary inline aggregate, including Transparent | Union of each instance child's `offset + P(child)` | Union of each instance child's `offset + G(child)` |
| Raw Union | Union of every alternative's translated `P` | Intersection of every alternative's translated `G`, viewed within the whole Union extent; empty when there are no alternatives |
| Fixed array with element E, count N, stride S | Union of `i*S + P(E)` for `0 <= i < N` | Union of `i*S + G(E)` for `0 <= i < N` |

Static fields contribute nothing. Gaps, element-stride padding and tail
padding are never added. Empty ordinary/Union minimum storage is padding,
not payload. A zero-data alternative contributes empty G, so can reduce a
Union's G to empty without making every alternative readable.

Use the existing target layout equations: array alignment is
`max(1,alignment(E))`, `S=roundUp(B(E),max(1,alignment(E)))`,
`B(array)=N*S`, with the inherited explicit-alignment requirement and ordinary
field placement/final rounding rules. Thus unit/zero-data E has a positive
array alignment and zero stride, not division by a zero alignment. Validate
stored S/B/alignment against those equations; do not change an element's
internal layout under packing.
All count products, offset additions, last-element ends and rounding must
fit **unsigned target nuint (u64)** before narrowing. Check division/remainder
or remaining-capacity bounds before multiplication/addition; reject overflow,
not saturated or truncated geometry. For N>0 require
`(N-1)*S + B(E) <= B(array)`. Reject a negative signed source index before
unsigned conversion; otherwise retain the full uint64 index domain.
Check `i<N` before `i*S`, then the selected field/child end; narrow to an
in-memory mask index only after proving it lies within the materialized root.

The precise count boundaries are:

* **N=0:** P/G are empty; retain N, E and every original nominal/layout
  obligation. Validate E and inline cycles first, even if E is large or
  another array. No array-element mask/value or element address is created;
  the independently specified Repeat seed evaluation is not skipped.
* **N=1:** translate the one element exactly once, with its padding holes;
  no independent repeated-state widening or element-identity erasure.
* **N>1, S>0:** repeat the exact element masks at checked stride, recursively
  through all dimensions. Since the materialized root is at most 8,192 bytes,
  only its actual positive-storage occurrences may be expanded.
* **S=0:** require the validated E extent to be zero. P/G remain empty for
  every valid uint64 N, including max-u64. Analyze E once, not N times.
  Completeness, nominal identity and logical element bounds still exist.

Traverse the finite type/layout graph independently of storage multiplicity;
N=0 is not a cycle/invalid-element escape. Build masks only for storage
occurrences present in the bounded root. Reuse immutable P/G only for the
same complete type, original bindings and target/layout context.

For an independent mask oracle, let an eight-byte U have an eight-byte
scalar alternative and an eight-byte padded alternative whose payload is
`[0,1) union [4,8)`. Then `P(U)=[0,8)` and
`G(U)=[0,1) union [4,8)`. A 24-byte ordinary aggregate with a byte at offset
0 and two such U elements at offset 8 has P at byte 0 and `[8,24)`;
G is byte 0 plus the two translated G(U) sets. A fresh aggregate result with
a fully covered array field writes count **24**, then `01`, seven `00`
bytes, and sixteen `01` bytes. A G-only result canonically writes count **0**,
not a short mask.

### Bounds and resource contract

Reuse 105's **4 MiB artifact**, 1,024-function, 256-slot, 100,000 shared
node/work and depth-128 limits, and its other String/argument limits.
The dense materialized aggregate-root envelope is **8,192 bytes inclusive**,
not a source layout or uint64-count maximum. Each other bound applies
independently; being small enough in bytes is not a budget bypass.

For private U+A geometry/codec processing, charge one shared work unit for
each type/field/array node or edge visited, each checked quantity calculation
or comparison, and each logical mask-byte operation (generate, copy, meet,
union or coverage-position comparison, including its operands). Repeated
passes and independent snapshots pay again. An immutable P/G cache may share
its construction once per exact context; each use/lookup is still charged.
This private processing recipe does not replace the separately selected
source proof or
[Repeat state/work recipe](fixed-array-repeat-resource-accounting.md).

Also cap **simultaneously live dense mask payloads at 4 MiB** per processing
root: retained declaration masks, P/G, old/current/branch states, staging,
scratch and output all count before their last use. Count distinct logical
D/snapshot copies separately, even with host interning; immutable P/G can
share only as above. Typed fact trees, sparse Repeat templates/overrides and
their headers retain their own additional owner-assigned charges. Zero byte
payload is not zero logical state or free traversal.

Preflight complete reservations with checked mathematical/remaining-capacity
arithmetic **before** allocation, expansion or publication. Do not reset a
budget at a function, dimension, imported expectation or recursive call.
Do not enumerate zero-count/zero-stride domains or allocate an N-entry
bitmap to decide P/G or completeness. Valid wide geometry exceeding a
materialization/work/live-state bound reports capacity exhaustion; invalid
geometry is malformed. Neither becomes count-zero coverage or success.
The existing wide type-only codec may preserve otherwise valid recipes;
that alone is not oversized callable-value or runtime support.

### Dense mask versus ranges

Select the dense vector to preserve the existing bounded fact directly.
It adds one count, a linear scan and no range normalization, stride algebra
or second comparison algorithm. A private range/repetition codec would be
smaller for large sparse layouts and could avoid the signed-count extent
bound, but would require its own exact validity, normalization and
materialization contract; reusing A2R1 bytes would not define one.
That is not needed for the present bounded summaries.

The dense vector suffices for the **required bounded array-bearing results**
above. Large positive-storage results outside that envelope acquire no
materialization claim; large zero-stride counts remain in scope through the
explicit no-expansion rules. Compact Standard region geometry remains useful
without becoming a second private coverage encoding.

## Production, loading and calls

Source producers derive the common coverage of reachable normal results,
respecting staging, joins, loops, overlap kills, cleanup and the existing
publication role. Publish derived stronger-than-`G` facts when persisting
retained declarations; do not erase them merely because they were inferred.
Recursive/unavailable analysis cannot manufacture a stronger mask.
An authoring or transforming producer must establish any mask it publishes;
changing a body invalidates affected derived facts before re-emission.

For in-scope arrays, derive return snapshots from the actual nested element/
field state, not the current helper's array-excluding fast path. Array
availability means every logical element is complete; opaque raw-Union
elements keep their own D and typed-child facts. Known element writes affect
that element, unknown writes conservatively affect every feasible element,
and joins intersect guarantees. Translate coverage at each checked
field/stride step without unioning mutually exclusive paths. Sparse Repeat
templates/overrides may stay sparse locally; materialize only the bounded
published mask, with the reservations above. Source-free reconstruction must
implement the same transitions.

At a call with a fresh published mask and **no CopyOrigin**, seed each
ordinary array field/element's D from its checked slice of the root mask:
for element i at root offset o, byte j is covered exactly when
`o + i*S + j` is covered in the root. Nested arrays apply this recursively.
Do not let the existing CopyOrigin-only array-call path discard that promise.
Completeness follows the actual complete aggregate's ordinary fields and
array elements, stopping at opaque Union alternatives; coverage does not
make an alternative's array children complete. Preserve separately justified
typed/child facts. Dense masks and sparse element maps are views of one
value snapshot: writes must update both or invalidate/rebuild a derived
view, never leave contradictory caches. Returning a value performs the
reverse projection from that snapshot, not a union of possible alternatives.

Keep the published declaration mask available independently of Function.
Capture it before body erasure, preserve it in projections/snapshots and
restore its normalized view into `BoundFunction.RawUnionReturnCoverage` (or
an equally explicit declaration-view carrier). A bodyful Function and its
declaration metadata must agree after normalization; do not serialize two
independently authoritative promises.

At calls, use the selected declaration's effective coverage: `G(T)` for
count zero, otherwise `C`. Separately instantiate justified CopyOrigin facts
from the invocation's staged entry values: intersect coverage of possible
origins, then union the justified bytes with the published coverage, retaining
all feasible dependencies. Coverage adds an initialized byte lower bound;
CopyOrigin preserves an actual value-copy relationship.
Neither is an OutputState, general Dependency or backing-only forwarding
promise, and losing either carrier cannot be repaired with the other.

**Private CopyOrigin array terminals:** retain 95's unchanged path/record
grammar and explicitly compose raw-Union value-fact forwarding. An exact
fact may select the whole admitted aggregate root or a whole
field-selected array/array-containing Copy subtree carrying raw-Union
D/typed-value facts, even without backing dependencies. Apply the existing
NormalResult or FreshInlineConstruction role, never infer it from size.
An array can be a path's terminal complete type; paths still contain only
declared inline fields, not serialized element indexes or new active-member
selectors. Each origin remains a real, nonempty entry-source alternative
with the same complete terminal type. This is the array counterpart of
A2R1's dependency-free whole-root forwarding rule, not an independent/empty
origin or a way to encode fresh initialization as copying.

An exact copy transfers the whole array's nested value/typed facts and
supporting origins, including zero-data logical facts; a byte-count-zero
coverage record does not erase its CopyOrigin or nominal validity. Return
coverage alone still cannot manufacture an initialized nested alternative,
valid bool/rune/pointer interpretation or provenance. An initialized array
field in the complete aggregate result establishes complete element values,
but does not make every interpretation of each opaque Union element valid.
Preserve and test that distinction.

Loaded bodies may recompute local facts needed for verification/lowering.
Those caches do not overwrite published promises or supply a missing import
expectation. Calling a source-binding verification path is not a substitute
for restoring decoded declaration metadata. Consumers perform the structural,
support and binding checks here, then may trust the published mask without
re-proving a provider's body. A structurally valid but false promise is a
producer-truth violation; optional body audits remain separate.

## Binding, replacement and transformations

Resolve the original selected provider/export/nominal identities and complete
callable/result roles first. Compare complete types and retained target,
layout/field correspondence, ownership and all other contracts before byte
positions. Equal names, row numbers, sizes or layout alone do not establish
identity, including between independent providers.
For arrays, compare the structural constructor, complete E/provider origins,
every ordered uint64 N and retained stride/extent/alignment at every
dimension. Do not flatten nested dimensions or compare only total bytes.
Even N=0 retains its original element-type/layout/Copy obligations; an empty
mask does not make different empty arrays or nominal elements interchangeable.

Let `E` and `A` be effective expected and actual coverage in that satisfied
type/layout correspondence. The provider satisfies this dimension exactly
when **`E subset A`**: every expected `01` is actual `01`. Additional
initialized payload bytes strengthen only this dimension. Losing a required
byte fails even if the body, CopyOrigin, Dependency and OutputState otherwise
match; exact mask equality is sufficient but not required.

Capture each original imported promise **before Function becomes null**,
in its importing Module's type context. Source-free binding checks
that retained mask against the actual provider declaration, including uncalled
expectations. Restore an imported call view with its **expected** mask, not
an unchecked overlay of the replacement's stronger mask. Keep actual provider
facts separate. If compilation relies on stronger actual coverage, retain
that additional stronger expectation; do not overwrite the old one.

Apply the same rule to ordinary links, reference sets, graph libraries,
roots/intermediates, diamonds and native snapshots. Every retained original
expectation must succeed; deduplication cannot choose the weakest one or
merge distinct provider contexts by name. A path unable to preserve these
expectations refuses the affected operation.

Re-encoding maps declaration/call identities, complete types, nominal origins,
field/layout correspondence and every existing import/contract reference.
Move each mask with its owner; its positions and 0/1 values are quantities,
**not type/field/function indexes**. Preserve offsets when the retained layout
is unchanged. A layout/target change requires a separately valid
recomputation/conversion and affected-code regeneration, not byte permutation
or permission to break an independent fixed-layout expectation.
Remap every array table's E, internal/terminal array types, aggregate formal
and result slots, recursive Contract type terms, nested places and CopyOrigin
path roots/owners. Preserve all dimensions and original binding contexts,
including metadata children under N=0; row pruning cannot erase their
required eligibility/layout expectations. Private array codes remain the
existing `0x20000000+i` family, not Standard TypeSpecRefs.
Rebuild Strings, counts, extents, offsets and hashes after rewriting.

Keep **TNPK/TNSP/TNGP outer version 3** fields and direct-expectation tables
unchanged. Select the joint **outer 3 / inner 106 / B+C+R+U+A / exact -2**
closure, with that one selection on every embedded private structured
Contract. Coverage lives in those Contracts, not an outer
NativeOutput/direct-import row. Outer typed
expectations still resolve in the importing inner universe. Validate retained
coverage before affected JIT/AOT use; stored images must match their truthful
contracts, and changed lowering inputs invalidate affected code.

Reject 105/104/other header selections and the withdrawn `-1` draft agreement
before record decoding; no `>=105`, missing-field default, relabelled header,
decoder retry or private/
Standard fallback is selected. Equal magic, outer version or repaired
SHA-256 does not identify a schema or repair a lost promise; structurally
valid bytes cannot authenticate a dishonestly relabelled stream's history.

Standard A2R1's Feature `22/1`, Contract `000F`, target references and compact
regions remain **a separate codec**. This proposal assigns none of those
numbers privately and changes no Standard selection. An explicitly selected
cross-encoding must preserve role, target/layout, byte-set meaning and every
original expectation while rebuilding the destination's records/references;
unrepresentable roles or facts refuse conversion.
For a bounded Standard-to-private mask in this aggregate-result domain,
validate the Standard region/repetition shapes and P inclusion first, then
form `G(T) union R`. Zero-occurrence type geometry contributes no regions;
it does not relax A2R1's region count/stride validity rules.
In the reverse direction, encode the guaranteed bytes beyond G as coalesced
rank-zero runs and retain exact type/layout/Import expectations, CopyOrigin
and Standard's required `22/1`, array `20/1`, CopyOrigin `15/1` where used,
and their derived prerequisites. G-only/zero-byte coverage does not remove a
retained raw-Union-bearing signature's feature obligation. Rebuild TypeSpecs,
FixedArrayLayout/FieldPath/MethodDef/Contract references and Body maps;
private field ordinals are not Standard identities.
Required Standard array-provider cases use A2R1's actual published
ReturnCoverage and whole-root entry-copy contracts, not private Body
inspection or an assumed eligibility/ABI change. This private amendment
does not silently add its constructor/field-summary eligibility to another
Standard selection. Private capacity limits do not invalidate otherwise
valid wider Standard geometry; a requested out-of-envelope conversion fails
capacity rather than dropping the promise.
A Standard structural-array result is not converted into this private
domain by inventing a nominal wrapper, changing its signature or retagging
it as unit; its separate result ABI gate remains.

## Structural checks and focused acceptance gates

Apply the exact bounds/reservations above. Preflight the signed count,
checked `4 + byteCount`, remaining input and cumulative budgets before
allocating or walking a mask. Resolve the entire result/layout closure,
including zero-occurrence element types, before admitting the result.
Reject negative/truncated counts, extent mismatch, bytes other than 0/1,
missing mandatory prefixes, wrong subjects/contexts, padding-only promises,
missing `G(T)` bits and unsupported target/layout requirements before affected
use. Resource exhaustion is distinct from malformed input and never a
successful weaker contract. Existing trailing-byte/cycle checks still apply.

Implementation must update shared declaration carriers, producer validation,
writer/reader restoration, projections, remappers, all binding/snapshot routes
and required analysis together. Public compiler API tests must establish:

| Area | Acceptance |
|---|---|
| Exact framing | Independently encode empty and nonempty bodyful/bodyless records, including uncalled imports and private constructors. Every function has the count; adjacent general Dependency, checked-output, CopyOrigin, foreign and import records retain their exact order. Reject unsupported headers and malformed/missing-field framing without fallback. |
| Array geometry/counts | Independently calculate P/G and full masks for admitted aggregate roots with nested array fields and Union alternatives; include padding holes, unrelated array siblings and already admitted Transparent cases. Cover N=0/1/many, zero-data nominal versus unit identity, max-u64 zero-stride counts, invalid E/inline cycles under N=0, checked offset/product/rounding overflow, and exact 8,192-byte/first-larger positive extents. |
| Values/calls | Execute already source-admitted bounded aggregate inputs/results with array fields through matched private providers, preserving existing direct/indirect and over-aligned aggregate storage rules. Zero-payload aggregate calls still evaluate operands/invoke once and preserve logical ordinals, exact types and failures. Exercise explicit empty/list/Repeat field construction, nested typed place reads/stores and once-only captures. Direct structural-array formals/results remain rejected by their independent ABI gate, including at zero extent. |
| Local versus published facts | Retain the existing nested-field-path constructor regression. Also publish fresh aggregate results whose nested arrays contain wide-initialized Unions, without an entry-copy origin; source-free projection/body erasure/re-emission must preserve their stronger masks. A local cache, forwarded input or four-byte constructor vector alone is not this evidence. |
| Truth and outcomes | Wide/short normal branches intersect, not union; failing cleanup/checks publish no result. Exercise padding holes, empty/all-zero-data unions, `G`-only normalization and unavailable/recursive inference without invented coverage. Producer checks reject a claimed byte not established on every normal result. |
| Copy/typed validity | Forward whole array/array-field entry snapshots, including fresh inline constructor fields, named-argument reordering, later source mutation, nested copies and joins. Preserve typed child validity and all origins, not only D; reject a projection with covered bytes but missing required validity/provenance. Do not fabricate a CopyOrigin for a fresh constant-filled result. |
| Independent replacement | Same identity/layout: equal or stronger coverage succeeds, weaker/absent required coverage fails. Check changed inner E/N/stride/alignment and different nominal providers, including empty arrays with equal byte extent. A bodyless stub is sufficient; provider-body inspection cannot repair incompatibility. Preserve separate stronger relied-on expectations and every uncalled import. |
| Malformed/capacity | Negative/truncated/mismatched counts, `02`/`FF` elements, padding bits, omitted G bits, non-Union-bearing/pointee subjects and missing layouts refuse. Test every inclusive byte/work/live/depth boundary and first excess, before allocation/publication; zero stride cannot hide N-sized expansion or free logical state. Wide descriptive metadata and a capacity failure are not oversized value execution; no count-zero downgrade. |
| Rewriting/closure | Permute/prune type/function tables without changing mask quantities. Cover ordinary links, provider/reference sets, graph roots/intermediates and diamonds with multiple original expectations, including a retained uncalled weak-provider mismatch. |
| Cross-contract/native | Combine nonempty coverage with independently meaningful CopyOrigin, Dependency, output and direct-import expectations without conflation. Round-trip outer-3/inner-106 packages/sets/graphs; reject incompatible coverage and wrong inner schemas despite valid hashes before execution. Qualify each claimed JIT/AOT route headlessly under existing gates. |
| Standard/private providers | Independently exercise corresponding bounded A2R1 and exact private -2 normal-result providers, fresh versus entry-copy promises, source-free binding/re-encoding, compatible strengthening and incompatible replacement. Preserve feature/target closure and original expectations under type/field/array/provider permutations, including empty and large-count zero-data types; neither codec's success is evidence for the other. |

Formal approval of this exact candidate remains outstanding. There is no
additional policy choice inside this bounded recommendation. Implementing and
qualifying the complete aggregate-array slice remains required work, not an
unsupported/deferred disposition for its in-scope cases. Completion of only
the array-free cases does not accept `conformance-arrays`.
Direct structural-array results and by-value array formals remain separate
ABI gates; they are not prerequisites for this already source-admitted
aggregate-result domain, and this proposal does not claim to implement them.
This does not by itself complete broader private-105 CopyOrigin, general
Dependency, reference/adaptation or other producer/consumer work; required
instances of those contracts in the matrix must still be implemented and
qualified. No oversized value support is claimed from type-only geometry.
No normative owner or compiler/test implementation is changed by this proposal.
