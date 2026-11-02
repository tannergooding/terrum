# Address-only nested backing for native storage addresses

**Status: unapproved; provisionally actionable under the user's explicit
missing-contract workflow.** This selects a bounded implementation direction,
not formal approval, implementation completion, format stability or Standard
TRRM conformance. Implementation is underway; current support and evidence
belong in the implementation references, not this proposal's acceptance
matrix.

## Decision and alternatives

**Admit recursively initialized nested direct-local storage solely as backing
for non-retaining direct `NativeParameter` address arguments.** Preserve the
exclusion of ordinary nested array value operations in private persistence.
This makes revision 101's nonempty ancestor paths producible and consumable
through source compilation without requiring a general nested-value codec.

The [path-persistence proposal](native-storage-address-path-private-persistence.md)
retains revision 100's nested runtime exclusion. Its tag-35 extension alone
therefore supplies encoding preparation, not a complete nested address route:
an initialized nested local needs excluded initialization, while a vacant
`init mut` local cannot satisfy even a Write address argument. Changing index
framing cannot resolve that admission contradiction.

The [implementation summary](../implementation.md) and
[module support reference](../implementation/modules.md#definitions-and-scope)
separate the selected private flat-operation boundary from direct-source and
Standard nested support. In the inspected code, `ModuleCompiler`'s 101 path
codec coexists with flat-only local/tag-43 admission, and
`BindNativeParameterAddress` rejects initialization locals. No codec-only
positive is evidence that this enclosing route works.

| Resolution | Assessment |
|---|---|
| Keep p>0 unusable; call 101 encoding preparation only | Coherent and required until a backing profile is implemented. It resolves the support claim, but leaves the public producer/consumer path blocked. |
| **Construction-only nested backing — recommended** | Reuses the array table, recursive tag-43 framing and tag-35 paths. Adds a precise storage/use role, not an array value ABI or ordinary nested read/write operation. |
| Admit general nested values or introduce a common place/construction codec | A cleaner eventual unification could cover copies, replacement, fields, references and call carriers. It requires those operations' complete identity, capture, state and wire contracts, substantially beyond the address prerequisite. Flattening into a flat local instead is not equivalent backing identity. |

## Owners and exact supersession

Accepted owners remain [initialization and flow](../specs/language/initialization-and-flow.md),
[lifetimes](../specs/lifetimes.md), [native boundaries](../specs/native.md),
[Windows x64 layout/access](../specs/targets/windows-x64-data-layout.md),
[private persistence](../specs/representation/persistence.md),
[format versioning](../specs/representation/format-versioning.md),
[reader trust](../specs/representation/reader-trust.md) and
[binding/replacement](../specs/representation/binding-and-replacement.md).
None is edited or broadened here. In particular, accepted Standard input
still has [no NativeParameter clause](../specs/conformance/native-boundaries.md#4-pointer-dependency-and-lifetime-boundaries).
The structural source family and foreign protocol remain the separate,
unapproved [source-array](standard-native-fixed-array-source-contract.md),
[storage-address](standard-native-storage-address-formation.md) and
[foreign-invocation](standard-native-import-function-pointer-contracts.md)
directions.

Within the revised **101 selection only**, supersede:

* The path proposal's **Decision and scope**, **Selection, regeneration and
  implementation gates**, and conditional positive acceptance gates, insofar
  as they exclude all executable nested backing or leave its initialization
  composition undecided. Its path bytes, final-index alias, checks, suffix,
  failure, traversal and binding rules remain controlling.
* The [revision-100 proposal](native-fixed-array-private-persistence.md)'s
  blanket nested-local and recursive-43 exclusions, inherited by 101, **only
  for the construction/use role below**. Tags 44–46 retain their flat rules;
  no nested element value, pointer-to-array or whole-array copy is admitted.
* The [artifact-local metadata companion](native-fixed-array-private-metadata-boundary.md)
  only insofar as it calls every nested recipe metadata-only: a recipe used
  by this backing role is now an implementation-body root. Full-artifact
  closure and contract-only pruning/replacement rules otherwise stay intact;
  no type-only export is created.

These are intentional amendments to unapproved candidates, not corrections
to the historical meaning of exact 100 or permission to relax every
`IsRevision100RuntimeArrayLayout` call.

## Address-only storage and initialization

This is an **enclosing-use profile**, not a new type, source qualifier,
serialized authorization or sample/control-flow recognizer.

* A root is an ordinary explicitly initialized direct local, outside the
  function's parameter/receiver slots. Its structural type has at least two
  inline fixed-array dimensions. Every dimension has positive N, stride and
  extent, with the inherited complete, acyclic identity/layout rules.
* The final non-array E must be positive-size, rootless Copy, directly
  interoperable on the selected Windows x64 target, and independently
  representable by the inherited 99-based runtime/initializer codec. It must
  satisfy the existing flat-row eligibility check, excluding its unit case.
  Array-containing nominal leaves, metadata-only pointer/signature recipes,
  unsupported wide values, managed/support-bearing values and owning cleanup
  are not newly admitted. Existing eligible scalar, inline aggregate and
  pointer-cell leaves retain their complete contracts; size alone is not
  evidence of eligibility.
* The local declaration's initializer must be a tag-43 construction tree of
  its exact type. At each array-valued E, each child is another exact tag-43
  constructor; at a non-array E, each child is an independently admitted
  exact-E expression. Require exactly N ordered children at **every** level.
  No array-valued read, conditional, call result, cast, spread or synthetic
  temporary can replace a constructor subtree.
* Evaluate/acquire captures once, left-to-right, recursively when reached.
  Complete private construction and install elements in order before
  publishing the root as live. Partial construction is never addressable or
  externally observable. An implementation may use the root's unobservable
  construction storage instead of separate captures only when equivalent in
  effects, dependencies and failure behavior; this grants no array value
  carrier, implicit allocation or observable early publication.
* A failing capture publishes no root or readable prefix. Preserve earlier
  effects and settle actual private acquisitions under ordinary unwinding;
  do not run cleanup for a nonexistent complete root. Payload and padding are
  not implicitly cleared or defaulted.
* Permitted subsequent uses are the address adaptations below and ordinary
  lexical retirement of this cleanup-free storage. An unused initialized
  root is harmless; admission does not require recognizing a particular call
  sequence. Whole-root assignment/copy/move, row/scalar reads, element or row
  writes/updates, checked references, separately stored/returned pointers and
  ptr-to-array formation remain outside this private profile.
* Vacant `init`/`init mut` declarations and later initialization are **not**
  added to this role. Neither Write nor `&init` initializes such backing.
  Empty dimensions, unit and non-unit zero-payload nested backing remain
  excluded; independently admitted flat/unit operations remain unchanged.

No new source spelling is proposed. This constrains private persistence, not
the whole source language: existing direct-source nested reads/writes may
continue to bind and execute while their private producer still declines.
The [accepted grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments)
does not yet include this structural family; the source-array proposal owns
that independent amendment.
On eventual adoption, the source-array owner should cross-link this narrower
private capability; it must not narrow its broader source/Standard direction.

## Access, invocation and failure

Each p>0 tag 35 must occur as the direct address-adaptation argument of an
already admitted direct `NativeImport`'s saved `NativeParameter` formal.
Require its exact pointee, Read/Write/ReadWrite access, **NoEscape** retention
and unsigned minimum `0..uint.MaxValue`. Missing annotations or MayEscape
cannot be treated as NoEscape. This adds no indirect/vtable protocol, escape
adapter, pointer-value flow or NativeOutput role; existing p=0 roles keep
their independent admission.

Read requires the **whole recursively constructed root live**, including
every counted child. Write/ReadWrite requires the same initialization and
actual mutable authority throughout the path; Write is not initialization.
Retain `IsReadonly == (Access == Read)`. A pointer's write qualification
cannot upgrade its containing local. Opaque-void adaptation keeps the
existing Read/NoEscape/minimum-zero restriction, deriving geometry from
actual E rather than void or a byte count.

At the argument's original Stage position:

1. Capture the actual local/storage generation. Evaluate each ancestor index
   once, root-to-leaf, checking original numeric `0 <= i < N` and selecting
   that child before evaluating the next index.
2. Evaluate/check the final index once, then require
   `Q = max(1, declaredMinimum) <= N - i` in that **immediate leaf array**.
   Retain its full `N-i` suffix; requested `ByteExtent` is exactly `Q*S`.
   Outer suffixes count array objects and never concatenate adjacent rows.
3. Use inherited checked target-nuint layout:
   `A=max(1,a(E))`, `S=roundUp(s(E),A)`, `B=N*S`, preserving transitive R.
   For array-valued E use its complete B/A/R. Check products, displacements,
   containment and actual occurrence alignment before address use. Padding,
   equal-size storage and root bytes after the offset are not extra elements.
   Preserve original supported 8/16/32/64-bit integer indices; do not narrow
   int128/uint128 into this codec.
4. Preserve the captured backing, alignment, access, aliases and dependencies
   through later arguments and Invoke. Rebinding index/pointer slots cannot
   redirect a staged address. Known invalidation, forbidden overlap or
   missing required facts declines use, not an externally opaque fallback.

The pointer is non-owning, non-rooting and call-confined. NoEscape covers
the complete invocation and applicable exceptional exits, including copied
interior addresses; it is a provider promise, not proof of foreign behavior,
an exclusive loan, no-reentry guarantee or lifetime extension. Preserve
ordinary alias, callback, cleanup and code-provider obligations. Add no
marshalling, pin, retain, ownership transfer or stack-to-heap promotion.
Raw calls and the existing address operation retain explicit lexical unsafe
authorization; an unsafe declaration alone does not authorize its body.

Statically impossible requests diagnose/refuse before publication. Dynamic
index/suffix failure uses the existing exact
[IndexOutOfRangeException](../specs/runtime/standard-failures.md#3-checked-index-access)
under an admitted checked-failure runtime, or its admitted terminal policy.
Prior initializer/index/call failures propagate unchanged. Evaluate no later
index/argument or native call after failed formation, publish no pointer or
result, and preserve earlier effects. A later-argument failure does not undo
successful local initialization; there is no output reservation to cancel.

Once Invoke starts, native writes affect the original initialized storage,
not a detached copy, and are not rolled back on a failure/status result.
Foreign type/validity promises still apply on every admitted outcome.
Pointer-cell initialization does not initialize its pointee or establish its
extent/ownership. Preserve stored pointer/backing/code facts through recursive
construction; foreign Write/ReadWrite conservatively invalidates facts for
all potentially written cells in the retained leaf suffix, not merely Q.
Keep outstanding dependencies that may survive partial writes. No old fact
is restored on failure; unsupported required fact transport still rejects.
No new native-exception crossing or success-correlated publication is added.

## Exact private schema and cross-layer obligations

Recommend replacing the unpublished 101 draft agreement with
**`terrum/private-semantic-101-storage-address-only-backing-1`**. The name is
out of band, not a new serialized field. Keep the on-wire **i32 101**, the
full 100-plus-path schema, and all byte shapes:

* Tag 35 retains its mandatory i32 ancestor count/list at offset 50, parent
  type plus original index per ancestor, and final index encoded once.
* Tag 43 remains `13-byte envelope; i32 childCount; Expression children[]`.
  Its amended admission permits the construction-only recursive tree above.
  Array rows remain 44 bytes; local records and tags 44–46 do not change.
* Keep FieldMove 42, all other inherited allocations, TNPK/TNSP/TNGP outer
  version 3 and independent semantic-payload selection. No Standard opcode,
  section, Feature or schema is assigned or composed, including A2/A2E/A2EU.
  Candidates 93–98 are not inherited by numeric ordering.

A fresh private revision solely to preserve the superseded draft is not
required: framing is unchanged and drafts have no historical compatibility
entitlement. **Exact agreement includes the amended admission rules.**
Update all participants to this one complete selection and regenerate
artifacts, snapshots and native outputs from source or complete retained IR.
The old 101 agreement is not an alias or alternate decoder. Matching 101
bytes alone cannot prove agreement or detect every mislabeled artifact.
Entry points continue using one 101 decoder in the participating build;
that build must implement the complete revised agreement.
Continue rejecting 100/99/other selected revisions before decoding records;
add no retry, header-only conversion or historical reader.

Before returning an admitted program, producer/reader paths must check:

* Complete inherited framing, type/reference namespaces, geometry, cycles,
  uniqueness and source spans; exact local root, constructor-child types and
  cardinalities, legal body context and actual saved native formal.
* Role-sensitive admission of each nested local and every retained use,
  including uncalled/unreachable bodies. Decode functions as needed before
  final role admission; a valid table row is not permission for tags 44–46 or
  a general array-valued expression. Reuse definite-initialization/access
  verification without replacing the accepted producer-truth boundary with
  mandatory semantic re-proof of arbitrary Bodies or foreign promises.
* Inherited shared budgets: 4 MiB, 1,024 functions, 256 slots, 100,000
  nodes/type-work visits, depth 128 and 1,024-byte strings. Charge every
  constructor/child and ancestor/index; retain p<=127 and effective path
  depth checks. Check physical child extent and remaining work before
  allocating builders. A u64 N is not an allocation request; huge metadata
  cannot bypass exact child counts or concrete backend capacity.

Failure uses the existing contract/format diagnostic boundary and publishes
no partial artifact/program/image. Do not repair mismatches with empty
initializers, scalar/unit substitutes, implicit zeroing or a general-value
fallback.

Integration owners are the binder's array/native-argument paths,
`NativeFixedArrayContract`, mandatory source/module verification,
`ModuleCompiler`'s local and expression codecs, and native frame/construction/
address lowering. Use role-sensitive checks rather than globally widening
the flat-row helper. Lower recursive construction into the actual aligned
root and child offsets without array-valued loads, spills or ABI copies;
account for every leaf/index call in frame/spill planning. JIT and AOT must
execute the same captures, checks and mutations.

Shared traversal/rewriting visits constructor children and all path indices
once, including dependencies found only in ancestors. Rebuild the final
`Index` alias from the last rewritten projection when a path is present.
Ordinary, set, graph, embedded-contract/snapshot and source-free re-emission
paths must remap local slots in their containing function,
nominal/array/pointer/fnptr identities,
index types, callable references and source coordinates. Reconstruct layouts
from mapped rows, retain original expectations and recheck adjacency; do not
byte-substitute offsets or serialize stack addresses/evaluated indices.
Full bodies retain this implementation closure; bodyless references do not
gain nested type exports or omitted bodies. Compatible unrelated provider
implementation changes remain permitted; changed relied-on nominal layout,
R/Copy, access, native or provider identity still requires binding rejection
or independently lawful regeneration.

## Public-API fixture and acceptance

This is a **proposed private-profile acceptance fixture**, not a claim that
the current private producer accepts it. It uses existing implemented,
provisional structural-array/native-annotation forms, not newly approved
grammar. Bind with an explicitly selected `NativeCoreRegistration`, following
the active [native-address test](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs)
pattern; this proposal chooses no production Core provider or registration
codec.

```terrum
[Terrum.Interop.NativeImport("fixture.dll", "ReadPair")]
[Terrum.Interop.NativeParameter(1, Read, NoEscape, 2)]
extern unsafe int32 ReadPair(ptr<int32> values);

public int32 Exercise() {
    mut [[int32; 3]; 2] values = [[1, 2, 3], [40, 41, 42]];
    unsafe { return ReadPair(&values[1][1]); }
}
```

Required public path, without internal-node fabrication:

1. `Compilation.CreateLibrary` produces checked source IR;
   `ModuleCompiler.CreateProducer(program, ["Exercise"])` succeeds.
2. `ModuleCompiler.CreateReference` reads/validates the artifact. Compile
   `int32 Main() { return Exercise(); }` against that reference, then use
   `CreateConsumer` and `Link` with the full producer bytes.
3. Drop the original library source/program. Re-emit `Exercise` from the
   decoded complete IR using
   `CreateProducer(linked with { EntryPoint = -1 }, ["Exercise"])`, recreate
   its reference/consumer, and link again. Removing entry-point selection
   makes this a library projection; it does not fabricate or rebind a body.
   Bodyless reference creation alone is not a body round-trip.
4. Independently inspect the typed record: p=1; outer N=2/S=12/B=24,
   leaf N=3/S=4, Q=2/ByteExtent=8, indices 1 then 1, final referent int32,
   root-relative displacement 16. With constant indices the tag-35 extent
   is 100 bytes, not two copies of the final child.
5. Execute both linked results against a controlled headless native
   `ReadPair` fixture returning the sum of those two cells: **83**.
   Separate decode, re-emission, JIT and emitted-image assertions.

These are required gates, not reported test results:

| Area | Positive, negative and boundary cases |
|---|---|
| Reachability/framing | The fixture above and a three-dimensional p=2 variant serialize, read and source-free re-emit through public APIs. Preserve p=0's 75-byte vector and exact-100 rejection; independently assert mandatory count, ancestor types/order and single final child. |
| Construction/publication | Unequal dimensions, effectful leaf calls and distinct typed captures retain recursive source order exactly once; a declaration-only initialized nested local is also positive. Inner/outer underfill, overfill, spreads, array-valued read/conditional children and vacant Write backing reject. A failing middle capture prevents all later captures/addresses/calls and publishes no root. |
| Access and mutation | Fixed backing works for Read; mutable backing works for Write/ReadWrite. Write then Read through two native arguments/calls observes the original cells; verify adjacent rows remain unchanged through separate native reads. Fixed-to-write upgrade, partial/ended backing and conflicting reserved outputs reject. |
| Continued exclusions | Ordinary nested `values[0][0]` reads, scalar/row writes and updates, row/whole-root copies/replacement, separately formed pointers, ptr-to-array, fields, parameters/results and vacant initialization remain negative **private producer/reader** gates even when source binding succeeds. Keep broader direct-source/selected-Standard support separate. |
| Retention/unsafe | MayEscape, absent NativeParameter, storing/returning/capturing the address, unsupported indirect/vtable transport and declaration-only unsafe authorization reject. Test both source and independently malformed/unsupported persisted contexts; no empty-contract or opacity fallback. |
| Staging/order | Side-effecting ancestor/final indices execute once in order, even when the final index is zero. A failing ancestor suppresses later indices; failed final suffix selection suppresses later arguments. A later-argument failure prevents native entry but preserves the initialized root and prior effects. Repeated activations use their own storage generation. |
| Bounds/extent | Outer nonzero/final zero and vice versa; first/last/N, negative and high-bit unsigned indices; minimum zero/one/multiple/uint.MaxValue; Q below/equal/above the final suffix. Outer 0/inner 2/Q=2 rejects despite spare root bytes. Dynamic failure suppresses later indices/arguments and native entry. |
| Layout/resources | Zero dimensions/stride/extent, unit and nominal-zero nested backing reject while valid metadata remains descriptive. Check target-nuint product/offset overflow, transitive explicit alignment, unsupported capacity without allocation, p=127/128, effective depth 128/129 and shared node/byte exhaustion. Wire capacity is not a promise that other budgets permit that fixture. |
| Reader safety | Corrupt constructor counts/types/contexts, root slots, parent adjacency, final geometry/Q/access/formal, Booleans/spans, truncated children, duplicate/cyclic/wrong-family rows and trailing bytes reject without partial output or alternate decoder. |
| Cross-feature facts | Exercise independently admitted Copy aggregates and pointer cells as leaves, including their captured backing/code dependencies; foreign writes cannot reuse stale provenance or imply pointee initialization. Missing required origin/contract/value codecs, later-argument invalidation and incompatible selected-provider replacement reject. |
| Closure/routes | Permute array/nominal/pointer/fnptr rows and function/local indexes; retain calls only in ancestor indices or initializer leaves. Ordinary producer/consumer, sets, graph libraries/root/edges, snapshots and native embeddings preserve those facts. Bodyless pruning and compatible unrelated provider changes still follow the metadata companion. |

Starting code evidence is
[`BoundNativeStorageAddress` and constructor nodes](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs),
the [array eligibility contract](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs),
[private codecs](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs),
[address binding](../../sources/Terrum.CodeAnalysis/Semantics/Binder.DirectNativeImport.cs)
and [native storage lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs).
Active [flat/private tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs),
[p=0 tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs)
and [nested source tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayStorageTests.cs)
establish starting patterns, not this profile's completion. The
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
excludes none of those files. Native qualification retains Windows x64 and
runtime-policy gates, supervised deadlines, owned-process exit/wait and
bounded cleanup; emission alone is not execution evidence.

## Prerequisites and remaining decisions

Implementation requires the complete revised 101 participants, role-sensitive
local/constructor/use admission, retained state/provenance and remapping,
supported target frame/storage lowering, and the existing direct-import and
runtime-failure contracts. Until those gates are met, decline nested private
use and describe the current path codec as preparation, not a completed route.

Formal approval remains outstanding for this exact revised draft agreement
and scoped exclusion replacement. No policy needed for this bounded route
is intentionally deferred to an implementer. General nested values/places,
vacant initialization, escaped/indirect protocols, richer private codecs and
broader Standard adoption remain separate, unselected work; none is silently
enabled or declared complete here.
