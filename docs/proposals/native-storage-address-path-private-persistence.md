# Nested storage-address paths in private persistence

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This document selects a candidate representation,
not formal approval or format stability. The byte representation is now
implemented in the worktree; the separate
[address-only backing companion](native-storage-address-address-only-backing.md)
amends its retained nested-runtime exclusion for one narrowly scoped use.
Neither proposal changes or completes the ongoing source-to-Standard A2 work.

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.” Preserve its boundary: “Implement accepted contracts as
written. This approval does not approve unassigned syntax, schemas, APIs or
runtime policy.” The choices below supply provisional direction only within
that workflow; adjacent missing contracts remain separate.

## Decision and scope

Recommend **private semantic-module i32 revision 101**, exact draft identity
**`terrum/private-semantic-101-storage-address-paths-1`**:

1. Inherit the complete [revision-100 fixed-array schema](native-fixed-array-private-persistence.md),
   including its [artifact-local metadata amendment](native-fixed-array-private-metadata-boundary.md),
   revision-99 u64 storage-address quantities and revision-92 FieldMove 42.
2. Extend existing **expression tag 35**, not a new operation/tag, with one
   mandatory **i32 ancestor-projection count and ordered list**, immediately
   before its existing `Index` expression. Each entry contains an immediate
   parent array type reference and its original index expression.
3. Keep the existing `Index` as the **final selection**, encoded once.
   Reconstruct every ancestor's complete geometry from its exact array-table
   row. Keep the existing address fields and requested-extent meaning.

An ancestor list contains only the selections **before** the existing final
selection. A flat array address or existing one-object address has count zero.
A two-dimensional element address has one ancestor; a three-dimensional one
has two. No duplicated final wire index, flattened offset/count, repeated layout
tuple, path discriminator, optional trailer or serialized authorization bit
is introduced.

For p=0, retain every independently admitted enclosing-use role. The added
list supplies no new NativeOutput or separately formed pointer-value role.

Scope is a direct local root followed only by fixed-array element selections,
ending at an independently admitted positive-extent native referent. This is
a representation prerequisite for direct NativeParameter adaptation. It
adds no source spelling, field-path variant, checked reference, nonlocal base,
pointer arithmetic, pointer-to-array runtime carrier, general array value or
initialization operation, NativeOutput protocol, foreign ABI or runtime policy.
The [source array direction](standard-native-fixed-array-source-contract.md#places-addresses-and-failures),
[A2 identity/Copy composition](standard-trrm-rootless-copy-type-evidence.md),
and [S address semantics](standard-native-storage-address-formation.md#source-region-and-failure-semantics)
remain distinct unapproved owners.

**Encoding capacity does not grant executable backing.** Revision 100
deliberately excludes nested runtime locals and array-valued operations.
This proposal specifies how a path-bearing tag 35 is retained and checked;
it does not lift those enclosing-use exclusions or widen tags 43–46.
A complete module needing an excluded nested local, initialization/value
operation or index codec must still reject before executable admission.
Completing that route requires its own explicit complete composition; merely
implementing this codec, or possessing nested metadata rows, is insufficient.
This boundary is material to the positive end-to-end gates below.

## Owners, live evidence and allocation review

Accepted owners remain [private persistence](../specs/representation/persistence.md),
[exact format selection](../specs/representation/format-versioning.md),
[reader trust](../specs/representation/reader-trust.md),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[initialization](../specs/language/initialization-and-flow.md),
and [Windows x64 layout/access](../specs/targets/windows-x64-data-layout.md).
None is amended here.

These are observations of the inspected worktree, not test results:

| Owner | Observed boundary |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundNativeStorageAddress` | Retains a root `Variable`, final `Index`, carrier/element/address types, three u64 quantities, readonly fact and span. The worktree also has a full-path `Projections` collection with final `Index` alias. That in-memory extension supplies no private byte schema; preserve the existing fields and collection meaning. |
| Same file, `BoundNativeFixedArrayProjection` and array pointer/path nodes | Represents one immediate array layout plus its original index. Reuse that entry shape; other nodes' full-path lists need not change meaning. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader`, `PutExpression`, `GetExpression` | Final structured writer selection and header admission are **exactly 100**. Tag 35 already writes and reads u64 quantities, followed by Boolean and one expression. The earlier revision-99 proposal's revision-92 width mismatch is not the current codec. |
| Same file, `ReadNativeFixedArrayTypes`, `GetPrivateFixedArrayLayout`, `GetFunction` | Mandatory bounded 44-byte array rows follow pointer types. Rows describe nested recipes; runtime-local admission separately rejects excluded nested rows. |
| Same file, `Calls`, `RequiredStructures`, `RemapNativeTypeIdentities`, `RemapExpression`, `ResolveExpression`; [`BoundTreeTraversal.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs) | Traversal/remapping generalization is in progress. Every route must retain the additional children/layout identities and final-child alias; changing only the writer loses facts on other routes. |
| [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), `ReadGraphLibrary`; [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs) | Graph libraries share header/table/body codecs but have separate reconstruction/admission. Set/reference merging and graph edges also require the same path/type mappings. |
| [`Binder.DirectNativeImport.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.DirectNativeImport.cs), `BindNativeParameterAddress`; [`DirectNativeImportContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs), `MatchesAddress` | Source/carrier generalization is in progress: the binder now retains root `CarrierType`, leaf geometry/index and full projections. The selected private tag-35 codec still has only one index; source binding does not select or implement its missing transport. |
| [`NativeEmitter.NativeStorage.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs) | `EmitNativeStorageAddress` and the general array-path helpers are the lowering boundary. A zero-index root-address shortcut cannot discard nonempty ancestors. |

The allocation check covered the current private constants, proposal catalog
and proposal compositions, including working/untracked documents:

| Namespace / existing selection | Consequence for this recommendation |
|---|---|
| Legacy private constants through 92, including span/fixed-layout selectors in `ModuleCompiler.TokenSpans.cs` and `ModuleCompiler.FixedLayout.cs` | Historical constants are not additional currently admitted header schemas; header admission is exactly 100. |
| Private 93 [dependencies](reference-provenance-carriers.md#private-exchange-candidate-revision-93), 94 [wide scalar codec](scalar-wide-value-carriers.md#exact-private-schema-and-composition), 95 [copy-origin summaries](reference-field-value-provenance.md#necessary-private-transport-candidate-95), 96 [foreign identities](native-output-foreign-interface-private-persistence.md#exact-revision-composition), 97 [native composition](native-calling-convention-private-persistence.md#exact-composition-and-selection) and [checked references](checked-reference-private-persistence.md#exact-schema-composition), 98 [interfaces/witnesses](source-interface-witness-private-persistence.md#exact-revision-composition) | Distinct complete candidates; 101 does **not** inherit them numerically or cherry-pick their type/value/contract codecs. |
| Private 99 [u64 storage-address codec](native-storage-address-private-persistence.md), then 100 [array composition](native-fixed-array-private-persistence.md#allocation-and-exact-selection) | These are the actual base chain. Select **100 plus this delta**, not standalone 99 plus guessed array fields. |
| **Private i32 101** | No competing private assignment was found. Proposed here, not allocated by the Standard registry. Unrelated decimal exponents, source/scanner operation numbers and descriptor byte sizes are other namespaces. |
| Private expression u8 / statement u8 / type references | Keep tag 35 and all other 100 allocations. In particular 42 remains FieldMove, 43–46 remain the flat array operations, and 47 gains no meaning. The checked-reference 97/98 meanings of overlapping expression numbers are not composed. No statement tag or type-code range changes. |
| Private native containers | Code selects TNPK version 3, TNSP version 3 and TNGP version 3 in [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [sets](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs) and [graphs](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs). Keep those outer layouts; embedded semantic payloads select 101 independently. |
| Standard F+S+A2 | Keep `trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`, selector `TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft`, `(1,0)`, EC/Body-0E, TypeSpec 11, section 0012 and their requirements. None selects a private tag-35 decoder. |

A distinct **exact schema** is necessary. A new private header revision is
the simplest selector because current entry points have only the i32 revision,
not a second out-of-band private decoder parameter. Reinterpreting 100 would
give the same selected header two record shapes. Pre-release mutability
permits deliberate reassignment; it does not authorize decoder guessing.

| Alternative | Assessment |
|---|---|
| **Extend 35 with ancestor count/list; select 101 — recommended** | One operation, no duplicate final child or layout authority. Preserves scalar/flat meanings and supplies every missing parent selection. |
| Full projection list on wire, replacing final `Index` | Coherent and can reuse the bound alias, but array selections then live entirely in the list while one-object addressing needs a separate case or synthetic entry. An ancestor-only wire list retains the final field uniformly for scalar, flat and nested forms. |
| New tag 47 for paths, retaining old 35 | Can be defined under another exact schema, but duplicates storage-address semantics, prefix validation and rewriting. It does not remove schema-selection or closure work. No capacity benefit warrants it here. |
| Add fields while retaining 100, or append an optional trailer | Requires a new explicitly selected exact-100 draft and consistent replacement of all participants. Current interfaces cannot distinguish it from active 100; an optional/guessed trailer can misframe the next expression. Not selected. |
| Flatten to root offset/count, derive capacity from remaining bytes, or generalize all private places | Flattening loses immediate bounds/identity/check order. A common place codec could preserve them but also selects fields, references and other unrelated contracts. Neither is the bounded fix. |

## Exact tag-35 layout

Keep magic `54 52 52 4D`; header offset 4 contains little-endian **i32 101**
(`65 00 00 00`). The named draft identity documents that complete selection;
it is not a new serialized string. All multibyte operands below are
little-endian, without padding.

The first **50 bytes** of tag 35 retain revision 100's fields:

| Offset from expression start | Field | Encoding / meaning |
|---:|---|---|
| 0 | `kind=35` (`23` hex) | u8, existing private expression namespace |
| 1 | `type` | i32, existing address-result identity; reconstruct `AddressType` from this common field |
| 5 | `spanStart` | i32 |
| 9 | `spanLength` | i32 |
| 13 | `variable` | i32, root slot in the containing function |
| 17 | `carrierType` | i32, root storage carrier; equals the root local's type |
| 21 | `elementType` | i32, actual final referent E |
| 25 | `elementCount` | u64, final immediate parent's full N, not its selected suffix |
| 33 | `elementStride` | u64, final immediate parent's S |
| 41 | `byteExtent` | u64, existing requested region Q*S, not whole carrier B |
| 49 | `isReadonly` | u8, exactly 0 or 1 |
| **50** | **`ancestorProjectionCount=p`** | **i32**, mandatory, `0..127` subject to stricter remaining budgets |
| **54** | **`ancestors[p]`** | Ordered entries below, variable extent |
| after the list | `index` | Existing final complete recursively encoded expression |

Each ancestor entry is exactly:

```text
i32 parentArrayType
Expression index
```

`parentArrayType` references the inherited 44-byte array row; its complete
layout includes E/N/S/B/A/R. No quantity is repeated in this entry, and no
byte length, span wrapper, prefix/escape or sentinel is added. Every index
has its own ordinary type/span/payload envelope. The whole address retains
its existing span; no new per-selection source-coordinate representation
is selected.

The total expression extent is:

```text
54 + sum(4 + ancestorIndexExtent) + finalIndexExtent
```

Inherited tag-1 scalar constants remain 21 bytes. Thus a zero-ancestor
constant-index address is **75 bytes**, one ancestor with two constant
indices is **100 bytes**, and two ancestors with three are **125 bytes**.
There is no legacy 71-byte alternative within 101, even for scalar addresses.

### Independent nested vector

Use IR notation, not a new source spelling: inner row 0 is an array of
private `UInt32` code 3 with N=3, S=4, B=12, A=4, R=1. Outer row 1 has
E=`0x20000000`, N=2, S=12, B=24, A=4, R=1. A direct local has outer type
`0x20000001`.

For outer index 1, inner index 1, Q=2 and readonly access, tag 35 retains
`carrierType=0x20000001`, `elementType=3`, `elementCount=3`,
`elementStride=4`, `byteExtent=8`, `isReadonly=1`. For the inherited direct
NativeParameter adaptation, the result identity remains
`PrimitiveType.NativeStorageAddress` (private code 29).

Count 1 is at offset 50 (`01 00 00 00`); the ancestor's parent type is at
54 (`01 00 00 20`); its constant index occupies 58–78; the final constant
index occupies 79–99. The derived address is root+16 bytes with final
known suffix 2 UInt32 elements. These offsets are independent framing
oracles, not proof from a writer/reader agreeing with each other.

Change the indices to outer 0, inner 2 and keep Q=2: **reject**. The final
parent has only one remaining element even though the root has enough bytes
to continue into the next inner array. Outer suffixes never concatenate
inner arrays or enlarge the final suffix.

## Identity, reconstruction and canonical rules

The **wire** list is ancestor-only. Preserve the worktree's full-path bound
`Projections` representation: it contains all array selections, including the
final layout/index, and `Index` mirrors its last index. For a full path of
length k, emit its first k-1 entries as ancestors and its final index once.
Require the bound alias and final projection to agree in complete typed
expression/span and layout facts, not merely evaluated value; disagreement
rejects rather than selecting whichever copy is convenient.

On reading p>0, reconstruct the p ancestor layouts, derive the final parent
row through their element types, and append one final projection using that
row and the decoded `Index`. Set both final-index references from that one
child. For p=0, reconstruct the inherited empty collection. A checked
single-step full bound path may normalize to this same flat p=0 encoding;
it supplies no alternative wire shape or extra evaluated child. Rewriters
likewise rewrite the final child once and restore the alias.

* `Variable` remains the root **local**, not an inferred temporary for the
  final parent. Require an actual nonnegative slot below the function's
  count, after parameters and any constructor receiver. It is not a global,
  parameter, implicit-reference base, descriptor cell or provider slot.
* With p=0, preserve existing behavior. An array carrier equals the local's
  exact array type and `Index` selects its E. The non-array one-object case
  retains `CarrierType=ElementType=local.Type`, N=1 and its existing
  zero-index/positive-object-extent rules.
* With p>0, the first `parentArrayType` equals the local's exact array type.
  Each entry selects one E subplace. That E must be the next entry's exact
  parent array type. The last ancestor's E identifies the **final immediate
  parent array**; derive its row without adding another type operand.
  The existing `Index` selects that row's exact E=`ElementType`.
  No step loads an array value.
* Array parent/carrier references use private i32
  `0x20000000 + zeroBasedRow`, within the actual inherited table and its
  100,000-row cap. Code 20 is not an array identity; Standard TypeSpecRefs,
  layout rows, Body maps and same-sized nominal types are not substitutes.
  Identity is the inherited normalized `(FixedInlineArray,E,u64 N)`, with
  complete nominal bindings, pointer depth/access and fnptr contracts.
* `CarrierType` continues to denote the **root storage carrier** and must
  equal the local's type, including when p>0. The final immediate parent
  is the root array at p=0 or is derived from the last ancestor at p>0.
  Final E/N/S must equal that final row. `ByteExtent` remains the requested
  region and may be smaller than the final parent's B. It is never rewritten to root B,
  `N*S` unconditionally, or an accumulated flattened suffix.
* All rows belong to the same selected private target/layout agreement as
  100: Windows x64, with no new target/profile operand here. Resolve types
  in the original **containing/source Module's** context. No provider,
  entry Module, source spelling or file extension supplies an omitted
  binding. No extra Module row or persistent runtime storage-generation
  identifier is invented.
* Preserve original integer index types and expressions. This 100-based
  codec admits signed/unsigned 8/16/32/64-bit carriers; inherited native-width
  aliases keep their resolved private type. The source direction's
  int128/uint128 indices still require a separately selected wide codec,
  not narrowing or implicit inclusion of candidate 94. Bool, character,
  enum, pointer and other non-index types reject.

Canonical writers emit the mandatory p, then every ancestor in root-to-leaf
order, then the final index. Omit no zero-valued index/selection, even if its
offset is zero; it still has parent bounds, identity and possible effects.
No alternative full-list encoding, duplicate final selection, redundant
one-object entry, flattened equivalent or optional zero-count representation
is canonical or valid under this schema.

Use 100's deterministic first-use/element-before-parent array interning,
extended to include every new parent type and index-child dependency in
encoded order. Ordinary legal reordered acyclic type tables remain readable;
re-emission rebuilds their references. Readers reconstruct each retained
ancestor layout from its row. Producers reject a conflicting in-memory
layout rather than silently replacing it with table geometry.

## Required checking and resource limits

Before affected consumption, the reader must establish:

1. **Exact selection and framing:** header 101 before schema-dependent
   records; complete inherited tables/functions and tag-35 prefix/count/
   entries/final child; assigned discriminators; canonical Boolean; valid
   nonnegative spans with checked end coordinates; inherited strict strings;
   no truncation or trailing bytes. Close each recursive child using its
   assigned shape, not guessed record lengths.
2. **Count/work safety:** `0 <= p <= 127` before allocating a builder.
   Bound its minimum physical extent by checked `p*(4+13)+13` remaining
   bytes, then decode every complete child under the remaining budgets.
   Charge each added ancestor entry and each ordinary expression child
   to the shared work/node allowance. If the enclosing tag-35 expression
   has zero-based depth d, ancestor j's index has effective depth
   `d+j+1`, and the final index `d+p+1`; require all depths <=128.
   The flat list must not bypass conceptual path depth.
3. **References and adjacency:** actual function-local bounds/roles,
   table reference families/bounds, complete normalized types and all
   root/carrier/parent/E relations above. Preserve `Type=AddressType`;
   direct adaptation retains its inherited code-29 result role, rather
   than treating that pseudo carrier as a public raw-pointer type.
4. **Complete geometry:** validate every retained row's E and derived
   S/B/A/R, uniqueness, acyclic recipes and inline containment, even at
   N=0/S=0. The selected ancestor operation checks original numeric `0<=i<N`,
   checked `i*S`, and containment of the selected child object within that
   immediate parent. Continue with the child object's own extent/alignment.
   For the final array require matching N/S, positive S and ByteExtent,
   `ByteExtent % S == 0`, and Q=`ByteExtent/S` in `1..N`.
   At a NativeParameter adaptation require **Q=`max(1, declaredMinimum)`**
   from the actual saved contract, in actual E units, not bytes, and
   **`IsReadonly == (Access == Read)`**. Other inherited p=0 roles retain
   their own unchanged contract checks.
   Final selection additionally checks `0<=i<N` and `Q<=N-i` before
   narrowing/multiplying. Use checked target-nuint arithmetic throughout.
   The one-object case keeps Q=1 and its inherited geometry.
5. **Supported use and binding:** actual positive-extent rootless stable
   backing, exact pointee/depth and access, actual occurrence alignment,
   supported index/element/runtime representations, declaration/native
   contracts and original provider expectations. Reject absent required
   origins or unsupported enclosing nested storage rather than exposing a
   partially admitted executable program.

Static invalid constant indices/requests diagnose or fail admitted IR
validation before use; dynamic checks execute at their original positions.
Never evaluate an index expression while decoding or remapping. Dynamic
inequalities are execution obligations of the selected operation, not a
requirement for the reader to statically prove an arbitrary index expression.
Known table/geometry inconsistency is malformed input, not a runtime index
event. Dynamic storage-generation, initialization and lifetime facts remain
operation-derived obligations. Producers must publish them truthfully;
mandatory safe-consumption checks do not require consumers to semantically
re-prove arbitrary Body facts or foreign promises. Keep existing private
verifier passes and the accepted reader-trust boundary.

Retain inherited **4 MiB/artifact, 1,024 functions, 256 slots, 100,000 nodes/
bounded type-work visits, depth 128 and 1,024-byte strings**, plus actual
table limits. The p bound is private codec capacity, not a new source-language
depth or array-count domain. u64 N/S/B are quantities, never allocation
counts: do not expand N elements, allocate backing to inspect a path, or
truncate high words. Valid wire/target geometry can exceed concrete backend
resources; reject that use explicitly. Translate truncation/arithmetic/I/O
failures through the existing module-format diagnostic boundary; failed
read/write/admission must not publish a partial artifact/program/image.

## Capture, suffix, access and execution semantics

At the address-adaptation argument's actual **Stage** position:

1. Capture the original local/storage generation. For each ancestor in
   order, evaluate its original index once, check it and establish the
   selected child subplace before evaluating the next index.
2. Evaluate/check the final `Index` once. Require the exact positive Q
   request against that final immediate parent's suffix, then establish
   the pointer. Retain the **full `N-i` suffix**, not only Q.
3. Accumulate checked byte displacements only to locate this original
   subplace. The retained path still includes every immediate parent,
   index, N/S/B, authority, alignment and backing dependency. A summed
   displacement is not a replacement semantic representation.

An outer selection's suffix counts **inner array objects**, not scalar
elements. Selecting one such object restricts further selection to that
object. Never compute final capacity from root bytes minus total offset,
multiply outer and inner counts, concatenate adjacent inner arrays/fields,
use padding as backing, or weaken Q/bounds to fit. A final zero index does
not permit `EmitNativeStorageAddress` to skip nonempty ancestors.

Require whole initialized backing at every counted array in this bounded
profile, including for Write. Preserve containing mutability/readonly
authority, initialized typed pointer cells versus pointees, actual packed/
overaligned occurrence guarantees, known alias/output overlap and invalidation
rules. `IsReadonly` retains the saved parameter's access relation; it cannot
upgrade a readonly path. Opaque readonly void adaptation still derives
geometry from actual E, not void or bytes. Zero-count, zero-stride/extent,
vacant/partial backing, one-past and fabricated anchor/copy storage do not
qualify. No array decay or new whole-array pointer ABI follows.

Preserve existing explicit unsafe authorization and raw-call obligations,
including in nested index calls; an unsafe declaration alone does not
authorize its body. No new serialized unsafe/stable/initialized/Copy flag
is added. Forming an address acquires no owner, loan, root, pin or cleanup
responsibility and extends no backing/code lifetime. Rebinding or retiring
storage cannot retarget/revive a captured path.

On failed index/suffix selection use S's existing exact
[Core IndexOutOfRangeException](../specs/runtime/standard-failures.md#3-checked-index-access)
under an independently admitted checked-failure runtime, or its admitted
terminal policy. Earlier index/call failures propagate unchanged. Evaluate
no later index/argument or native call; publish no pointer/result, leave its
result destination vacant, and preserve earlier effects without rollback.
There is no NativeOutput reservation to cancel. Once Invoke begins, preserve
the actual foreign outcome/validity rules; this encoding adds no rollback,
success classification or output-initialization policy.

[JIT](../specs/jit.md) and [AOT/native lowering](../specs/native.md) must
implement the same typed checked path against the same logical local in
the actual activation, preserving one-time evaluation, mutations and failure
ordering. Persist no stack address, frame offset or cached evaluated index.
Any materialization must implement that same storage, not a detached ABI
copy. Retain all existing code/binding/lifetime obligations. Emitting a PE,
decoding metadata or executing a scalar neighbor is not evidence that the
nested address executed.

For Standard F+S+A2 production, the corresponding lowering is the existing
ordered intrinsic PlaceSelection chain followed by EC with Q at this Stage
position. Each selection retains its own row/parent; EC consumes the final
captured place. This requires actual A2 production, source-free reconstruction,
binding and lowering work. Private 101 is neither its wire layout nor proof
that those paths exist; no Standard record or allocation changes here.

## Dependency closure, remapping and re-emission

The path belongs to its original containing Module/function, not the native
provider or chosen root Module. Before merging, retain original import
expectations and bind each referenced nominal identity through the existing
private mechanisms. Private 101 adds no general candidate-96 origin codec;
unrepresentable or ambiguous origins reject rather than merge by spelling.

Apply the following on ordinary producer/reference/consumer links, set
merges, graph-library/root/edge routes, embedded semantic Contracts,
provider/consumer snapshots and every source-free re-emission:

* Shared nested-expression traversal/rewriting visits each ancestor's
  index in order, followed by final `Index`, **once each**, using the full
  bound projection list without a second visit through its final alias.
  Type visitors also visit each hidden layout's carrier/E identities. Include calls,
  function addresses, data/code-pointer provenance and all retained effects
  inside any child in dependency closure, even when the final index is zero.
* Remap root slots only in their actual containing function, using an
  explicit local map if slots move. Remap result/address/carrier/element
  types, each parent type, every index-child type/symbolic function,
  and all referenced nominal/array/pointer/fnptr recipes through
  family-specific source-context-to-destination maps. Reconstruct redundant
  layouts from the mapped rows and recheck chain adjacency. Preserve each
  span with its original source context; any optional source-data rewrite
  follows that coordinate mapping, not a function/type-row offset or the
  entry Module's source text.
* N/S/B/A/R, Q and readonly facts are not row indexes. Preserve geometry
  and original expectations for unchanged binding/target. Do not repair
  incompatible replacement by recomputing a more convenient stride/count
  over the replacement. Relevant changes invalidate admission/layout/code
  caches and require affected binding checks and re-lowering or rejection.
* Full artifacts retain the closure of every retained body/path, including
  ancestor-only type and callable dependencies. Bodyless references and
  contract-only projections retain only independently legal contract roots,
  as in the artifact-local metadata companion. A private implementation
  path creates no type-only export/provider expectation. Unrelated provider
  implementation arrays may change if actual retained contracts/dependencies
  remain satisfied; consumer-owned paths keep their own relied-on facts.
* Compare retained original expectations against the actual selected
  provider: existing exact identity/export key, nominal layout/R/Copy,
  pointer access/depth, native parameter/minimum/NoEscape, output/effect/
  backing and code-lifetime requirements. Preserve private slot-flexible
  vtable replacement versus stricter native-package binding.
* Re-emit decoded typed IR using the exact selected writer. Rebuild all
  affected type references, function/local maps, contract spellings,
  container lengths/checksums and optional source data as applicable.
  A bodyless reference cannot regenerate omitted implementation indices
  or bodies. Do not reread source to invent them or patch matching bytes.

## Selection, regeneration and implementation gates

All fresh structured writers select **exact 101**, including scalar-only
artifacts, empty array tables and addresses with p=0. Audit every
exact-membership gate, not only `ReadModuleHeader`: tag 35, array tables/
layouts, structure fields, invocation buffers, local/body admission,
retained profiles, producer validation, graph reads and snapshots must
select the whole stated schema. Do not make 101 an alias for 100.

Readers for this selection reject honestly declared 100, 99, candidates
93–98, earlier/unknown revisions and other formats **before records**.
Old exact-100 readers reject 101. No numeric roll-forward, p=0 fallback,
filename/profile/Feature sniffing, equal-magic retry or alternate decoder
after a failed parse. Keep outer TNPK/TNSP/TNGP version 3 and existing
native-profile labels; the new complete outer-3/inner-101 agreement must
hold for each embedded semantic payload/snapshot. Outer integrity checks
never replace inner selection or binding.

Recommend **regeneration**, not historical compatibility readers or a
converter in this task. Rebuild affected producers, consumers, graph
libraries, embedded Contracts/snapshots and enclosing outputs from actual
source or independently retained complete checked IR. Reuse an existing
native image only under independently verified unchanged semantic/target/
binding obligations, not a changed header/checksum. A separately authorized
converter would need exact source-schema decoding, typed reconstruction/
reference remapping and complete destination re-emission; header substitution
or inserting four zero bytes is not conversion. There is no promise to
identify every dishonestly relabelled stream that happens to satisfy 101.
An extended bound node written as 100 with projections dropped does not
define another historical 100 schema. Missing ancestors cannot be recovered
from its surviving type/count/offset facts; use original source or complete
independently retained IR, not an inferred path.

Until migration, any extended bound carrier reaching an exact-100 writer
must **reject nonempty ancestors**, not silently serialize only `Index`.
The 101 codec and remappers can be implemented as this bounded prerequisite.
They must not turn 100's excluded nested-local/value forms into accepted
runtime input merely to obtain a round-trip or passing test. A required
richer backing/initialization codec needs its own expressly selected complete
composition; source-to-Standard A2 work remains independent and unfinished.

## Required acceptance cases

These are future gates, **not tests run or support claims**. Use public
compiler APIs, independent byte fixtures and unrelated synthetic providers.
Codec/structural cases must distinguish successful representation checking
from declined enclosing runtime use. Positive nested full-module/execution
cases are conditional on a complete separately admitted backing route; until
then, retain explicit unsupported-use negatives rather than assert success.

| Gate | Required cases / outcome |
|---|---|
| Exact selection and inherited behavior | All fresh producer/consumer/graph-library outputs select 101, even without addresses/arrays. Ordinary/set/graph/snapshot readers reject 100/99/93–98/unknown headers before body decoding. Preserve scalar, flat array, readonly/writable, u64 extents, FieldMove 42 and tags 43–46 semantics; keep excluded operations rejected. |
| Independent framing | Assert prefix operands at 25/33/41, Boolean at 49, count at 50 and 75/100/125-byte vectors. Use independent 2D bytes above and adjacent expressions/statements/functions to detect drift; round-trip agreement alone is insufficient. Mixed signed/unsigned constant types retain their own envelopes/bits. |
| Nested identity and suffix | Two and three dimensions, unequal extents/strides, nonzero outer and zero/nonzero final index. Preserve every row/index/N/S/B and exact final E. Q below/equal/above final suffix; reject the outer-0/inner-2/Q-2 crossing despite sufficient flattened bytes. Never multiply dimensions or borrow adjacent/padding storage. |
| Capture/order/failure | Side-effecting indices, a throwing/failing outer or middle selection, rebinding, later failing argument and final suffix failure. Each reached index executes once in order; earlier failure suppresses all later indices/arguments/calls. Original storage mutates/readbacks correctly, with untouched neighbors and no result publication on failure. |
| Integer and geometry boundaries | Negative signed indices; first/last/N; u64 high-bit/max values; counts above i32/u32; checked stride/product/offset/summed-offset/extent overflow; unit/nominal-zero/count-zero metadata remains valid where inherited but cannot back an address. Preserve original bits or explicitly reject unsupported codec/target/backend use, without huge backing allocations or narrowing. Int128/uint128 remain negative under this 100-based composition. |
| Access/state/alignment | Fixed versus mutable root, readonly attenuation versus write upgrade, unsafe block/expression versus declaration-only authorization, vacant/partial/retired backing, packed underalignment, transitive explicit alignment, pointer-cell depth versus pointee, output overlap, NoEscape/later-argument invalidation. Retain existing obligations; no anchor, temporary copy or external-opacity fallback. |
| Malformed bytes and references | Negative/excess/truncated count, partial parent reference, truncated/wrong-type index, absent final child, Boolean 2, invalid/overflowing spans, unknown tags, wrong-family/absent/retired-20 type, bad local/parameter/receiver root, chain gaps, wrong first/last parent/E, forged final N/S/request, duplicate/cyclic/conflicting type rows and trailing bytes. Reject through the format boundary without retries or partial outputs. |
| Resource limits | p=0/1/127/128, effective depth 128/129 with enclosing expressions, remaining-node/type-work exhaustion, maximum slot and 256/out-of-range slot, 4 MiB boundaries and insufficient bytes before allocating count-driven builders. A wire-maximum count is not a promise that all other limits permit that fixture. |
| Structural remap and dependencies | Independently permute struct/array/pointer/fnptr tables and move local/function indexes; include calls/function addresses in ancestor-only children and unrelated equal-layout types. Reconstruct/re-emit non-byte-identically, preserving child order/spans, all original expectations and conditional dependency closure. Same spelling/different admitted origins must not merge. |
| Cross-route and replacement | Full ordinary artifacts, producer/consumer dependencies, sets, graph libraries/root/edges, embedded Contracts/snapshots and source-free re-emission preserve retained path closure. Bodyless projection prunes only unrooted implementation data. Compatible unrelated provider changes succeed; changed relied-on E/N/layout/R/access/native contract or wrong/missing/ambiguous provider/key rejects. |
| Standard isolation and native outcomes | No private/Standard alias: unchanged F/F+S/A2 schemas and EC/0E remain separately selected. Once backing and A2 body gates are actually implemented, qualify source-to-A2, source-free reconstruction, semantic rewriting, JIT and AOT/native package/set/graph execution with nonzero nested selections, native read/write/readback and exact failure order. Decode/emission/metadata-only/scalar-neighbor results are not those execution results. |

Active starting patterns are
[`NativeOutputModulePersistenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs),
[`NativePointerModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativePointerModuleTests.cs),
[`DirectNativeImportTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs)
and
[`NativeFixedArrayStorageTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayStorageTests.cs).
The inspected [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
excludes none of these. Keep ordinary tests headless; native checks retain
their explicit Windows x64/runtime-policy gates, supervised deadlines,
owned-process termination/wait and bounded `TestFileCleanup` retries.

## Intended supersession and remaining decisions

Within the proposed **101 selection**, this document supersedes only:

* The [storage-address proposal's exact record layout](native-storage-address-private-persistence.md#exact-record-layout)
  after offset 49, its zero-ancestor 71-byte vector and revision-99
  producer/reader selection. Retain its u64 quantities, requested-region
  semantics, access/binding obligations and regeneration policy.
* The [fixed-array proposal's tag-35 framing statement](native-fixed-array-private-persistence.md#body-encoding-and-reconstruction)
  and matching 71-byte regression vector, exact-100 selection and single-index
  traversal/remapping assumption for this node. Keep the mandatory array
  table, layout identity, metadata boundary and other operation exclusions.

This does not retroactively redefine 99/100, edit either proposal, allocate
a Standard ID, or amend accepted owners. The recommendation settles the
candidate number/composition, sole record shape, ancestor/final-field roles,
integer domains, reconstruction/checking, mapping and exact-only regeneration.
No alternative byte interpretation is left to implementation.

**Formal approval remains outstanding** for those choices. **Still separate**
are the complete private nested-local storage/initialization/value admission
composition if required, additional private wide/provenance/origin codecs,
field/nonlocal paths and broader Standard A2 producer/source-free/provider/
JIT/AOT adoption. They are not approved or implemented by this document.
Implementing this persistence prerequisite is not acceptance of the A2
milestone; no source, test, inventory, plan or todo change accompanies it.
