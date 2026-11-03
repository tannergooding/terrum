# Standard native fixed-array repeat initialization

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** Every recommendation below remains a candidate,
not normative adoption, implementation support or execution evidence.
[Proposal index](README.md).

> “At a genuine missing contract, prepare the smallest concrete decision needed
> as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it
> being treated as a provisional approval/direction. I will go and appropriately
> review/approve those proposals at a future point; this allows you to continue
> work without being blocked. Do not invent compatibility aliases, fallbacks or
> encodings to avoid the decision.”

## Decision and preserved baseline

Recommend **target-typed `[seed; N]`, evaluating seed exactly once even when
N=0, followed by ordinary Copy repetition**. Retain one element-fact template
and sparse deviations, not N expressions, captures or analysis entries.
For compact Standard retention, select the distinct **A2R1+Repeat** successor
below with one explicitly amended SemanticStore combination; allocate no
opcode. The next implementation slice can qualify huge source programs through
public semantic APIs without emitting or allocating their native storage.

This enables the pending
`standard-native-storage-array-raw-union-array-compact-counts` acceptance work:
flat and nested high-count D, unknown-index/common-D joins, overflow/padding
bounds and resource bounds. Writing this proposal completes none of those gates.

The [specification index](../specs/README.md) reaches the governing owners:

| Owner/direction | Preservation and intended amendment |
|---|---|
| Unapproved [fixed-array source contract](standard-native-fixed-array-source-contract.md#proposed-initialization-and-publication) | Preserve `[E; N]`, uint64 count, identity/layout, captured places and complete publication. Supersede its **sole exact-list construction restriction by adding a separate repeat form**. Comma lists still require exactly N expressions, allow their trailing comma and reject underfill/overfill/spreads; `[]` still constructs only N=0. |
| Accepted [grammar §3.4](../specs/grammar-spec.md#34-expressions-and-precedence), [construction](../specs/language/construction-handlers.md) and [evaluation](../specs/language/expressions-and-statements.md#evaluation-and-expression-boundaries) | Add the production below as another family-owned exception. Other targets retain their builder protocols. Register no nominal `InlineArray`, Core Fill API, default value or allocating array form. |
| Accepted [complete types](../specs/language/types-and-normalization.md), [initialization/joins](../specs/language/initialization-and-flow.md), [Copy/cleanup](../specs/language/ownership-and-cleanup.md) and [layout/ABI](../specs/data-layout-and-abi.md) | Preserve normalized E/provider origins, availability/authority, build-before-replacement and checked target-nuint geometry. Select no whole-array signature ABI, decay, heap allocation, ownership duplication or runtime failure policy. |
| Unapproved [A2 Copy](standard-trrm-rootless-copy-type-evidence.md), [A2E](standard-trrm-empty-fixed-array-initialization.md), [A2EU](standard-trrm-unit-fixed-array-initialization.md) and [A2R1 raw Union](standard-trrm-raw-union-initialization.md) | Preserve TypeSpec `11`, layout section `0012`, Copy evidence, empty/unit admission and raw-Union P/G/D, typed validity, overlap and ReturnCoverage/CopyOrigin rules. Their existing exact identities and Body meanings remain unchanged. |

## Alternatives

All alternatives and the recommendation are **unapproved**.

| Route | Assessment |
|---|---|
| **Once-evaluated repeat/fill initializer — recommended** | One complete Copy seed supplies uniform per-value facts at any layout-valid count, without a call ABI. The explicit N=0 rule preserves seed effects/failures. |
| Per-element fill expression | A coherent generator with increasing-index evaluation and no evaluations at N=0, but arbitrary calls may return different D/provenance and fail on any iteration. Uniform facts need a universal proof, not one observed seed; unnecessary scope here. |
| Whole-array signatures/by-value ABI | Requires logical transport, independent-copy/backing/alignment, state/provenance and target rules. A formal's published guarantee does not manufacture a strong seed or the caller's huge initialized argument. Aggregate classification does not admit this structural family automatically; [CG-004](../implementation/conformance-gaps.md#cg-004-native-declaration-and-body-integration) and `SemanticVerifier.VerifyCore` retain this gate. |
| Narrower non-source seam | Public layout/table APIs test geometry/capacity, not initialized D. Public bound list construction still requires N elements and verification rejects array formals. Injecting live state or using reflection/friend access bypasses production/verification. A new compact public bound constructor needs the same repeat contract; retain layout-only tests, not test-only initialization permission. |

## Proposed source contract

**Unapproved grammar amendment:** add `FixedArrayRepeatExpression` to Primary
in [§3.4](../specs/grammar-spec.md#34-expressions-and-precedence), distinct
from the existing comma-list production:

```ebnf
FixedArrayRepeatExpression = "[", Expression, ";", ConstantExpression, "]" ;
```

1. Require an independently established exact `[E; N]` target. Check seed in
   E's ordinary contextual conversion/validity/dependency context; its completed
   type must be exactly E. Resolve the written count under the source proposal's
   **required exact uint64 constant** rules and require equality to target N.
   Normalize aliases/constants ordinarily; add no count inference, runtime
   count, domain extraction, implicit wrapping or layout-equal E substitution.
2. This is expression-position syntax, not the type-position `[E; N]`
   production or receiver-attached indexing. Reject missing target/slots,
   commas or spreads in the repeat form, including `[seed; N,]`. A nested
   repeat receives its own exact element-array target. Symbolic/open recipes
   and non-Copy/managed elements remain outside the selected bounded profile.
3. Validate E's complete RootlessCopy/Blittable closure and the checked layout
   **at every N, including zero**. A live raw Union is a complete opaque seed;
   its alternatives need not all be readable. A vacant/maybe-live seed rejects
   even at N=0. Repetition invokes neither E's constructor nor a Clone/user-copy
   function per element, transfers no unique owner and supplies no missing
   positive Copy evidence.
4. Form/check an assignment target before its RHS. Evaluate and acquire the
   seed **once**, including its nested construction, selected branch, calls,
   checks and failures, then freeze its complete value/fact snapshot. Do not
   reread mutable seed storage per copy.
   Install N copies in increasing index order and publish only the complete
   array. At N=0 there are no copies, but seed evaluation/acquisition still
   occurs once; `[]` remains the separate zero-evaluation empty construction.
5. Preserve actual effects/failures and cleanup of that single evaluation.
   Failed seed/capture/construction publishes no array and has not retired or
   overwritten the assignment destination; earlier external effects remain.
   Settle actual acquisitions in reverse order under the checked-failure
   policy, never incomplete-parent Cleanup or N fictitious seed cleanups.
   Fail-fast promises no unwind. Add no per-element callback/failure category.
6. Live replacement stages a complete repeated RHS before ordinary retirement/
   commitment. Use private construction storage where required; no readable
   prefix, mixed/prefix array state, rollback or physical atomicity is promised.
   Surrounding containing-value validation and dependency checks still apply.
   Elision is permitted only when all retained state/value/effect/dependency
   observations permit it, not because N or byte extent is zero.

Proposed function-body fragment, assuming an admitted Copy Blittable `Word`
whose ordinary constructor initializes uint64 `Wide` in an explicit unsafe block:
**not accepted syntax or current runnable evidence**.

```terrum
const uint64 H = 0x1_0000_0001;
Word seed = new Word(uint64(7));
mut [Word; H] values = [seed; H];
unsafe {
    uint64 last = values[uint64(0x1_0000_0000)].Wide;
}
mut [[Word; 2]; H] rows = [[seed; 2]; H];
```

Here both counts exceed uint32 while their eight-/sixteen-byte strides still
fit Windows x64 nuint. The fragment is for **semantic-only** qualification,
not a request to allocate its tens of gigabytes.

## Repeated facts, nested paths and joins

Use the [raw-Union proposal's transfer rules](standard-trrm-raw-union-initialization.md#minimum-representation-facts-and-transfer)
without strengthening the type-level guarantee G. For a nonempty repeated
array, its initial per-element template C is the staged seed's D **and**
complete-child/typed-validity/pointer facts, possibly stronger than G(E).
Its sparse map M contains only exceptional in-bounds elements. A known read
uses M[k] when present and C otherwise. This is a per-value snapshot, not a
TypeDef promise, active member, byte initialization or alias of the seed.

* **Common/unknown read:** meet the facts of all feasible selected elements.
  Include C exactly when an unoverridden candidate exists; for the unrestricted
  nonempty domain this is `storedKeyCount < N`. If every element is overridden,
  meet only those overrides. N=0 has no successful element read, not a vacuous
  full-coverage proof. Bounds/refinement may reduce the domain to one element.
* **Known write:** apply ordinary staged whole-value or subrange replacement
  to that element's snapshot, preserving unaffected elements. Normalize an
  override equal to C away. An alternative/subfield store uses its actual W:
  kill covered/typed facts in W, install only justified RHS facts, and retain
  bytes outside W. Whole-element replacement transfers the new complete E
  snapshot, not the union of old/new D.
* **Unknown write:** preserve the distinction between possibly unchanged and
  overwritten paths. For a whole-E write whose checked index may select any
  element and N>1, each element's post-facts are its old facts **met with**
  the RHS facts: update C as well as every stored override. For a single
  possible target, including N=1, perform exact replacement and allow justified
  strengthening. N=0 fails selection before the RHS. For subrange writes,
  meet each possible element's old facts with the ordinary W-transfer result;
  a one-byte Low store does not kill an initialized untouched Wide tail.
* **Nested arrays/fields:** repeat the complete seed subtree symbolically.
  `[[seed; M]; N]` evaluates the inner seed once, completes the inner array,
  then copies that snapshot N times; an inner comma list still evaluates
  every listed expression once. Existing sparse child overrides in the seed
  repeat with it. A later concrete path changes only that destination path;
  unknown ancestors/indices meet all feasible affected subtrees recursively.
  Preserve supported correlations/guards, immediate dimensions and containing
  fields; never flatten counts or infer suffix backing from adjacent rows.
  Equal offsets at zero stride do not merge
  logical elements. Completing a private child does not publish its ancestor.
* **Copies/branches/loops:** transfer snapshots and rebase destination storage
  generations/paths, not pointee/code origins. Join equal array identities by
  meeting their templates and the union of stored exceptional keys, using each
  incoming effective value at those keys. Templates need not be equal.
  Retain only common validity/D but **all feasible guarded backing/ownership
  obligations**. Loop fixed points retain these transfers; no coverage is
  accumulated from mutually exclusive writes or inferred by enumerating N.

Typed Copy preserves raw-Union defined payload bits and actual initializedness,
not just the statically retained D; omitted D is not permission to clear bytes.
Array/element-only padding receives no initialization guarantee. In particular,
copying a padded alternative into an overlapping union kills its padding
interval even when another alternative could read those bytes. Repeated raw
pointer cells all retain the seed's actual backing/code origins, access,
extent and lifetime obligations; they copy neither pointees nor owners and
establish no NoAlias, pin, rooting, stability or lifetime extension.
Overlapping writes invalidate every dependent interpretation, not only the
named field. Full D alone cannot validate a pointer or an invalid scalar.
Calls obtain seed facts from published contracts, including A2R1 R/C where
applicable, never by inspecting an imported private body.

## Exact Standard successor

**A distinct successor is required for the recommended compact retained operation.**
An ordinary record-05 store consumes the destination's complete value type,
not one E repeated into `[E; N]`. A2E's None/zero-use N=0 exception and A2EU's
typed unit source do not encode repetition. Expanding N stores/records defeats
the compact route; an ordinary authored loop cannot acquire a vacant parent's
private construction permission or completion proof by pattern recognition.
No aggregate default/frozen codec or invented helper call supplies this meaning.
Fully lowering a small repeat into already admitted captures/stores could
produce an unchanged exact schema after ordinary verification; that is not
the compact RepeatCopy route selected here.

Propose exactly **A2R1+Repeat**:

**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1`**

Candidate selector:
**`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft`**.
It means the complete exact [A2R1 composition](standard-trrm-raw-union-initialization.md#exact-successor-and-allocation)
plus only this repeat contract. A2, A2E, A2EU, A2EU+SF1, A2R1, A3 and other
selectors are neither renamed, widened nor aliased.

**Unapproved record amendment:** in this successor only, assign
`SemanticStore.sourceUse=6=RepeatCopy` with the unchanged 20-byte record:

```text
u8 recordKind=0x05, u8 flags=0, u16 reserved=0
u8 placeKind=1, u8 sourceKind=1|2, u8 transition=1, u8 sourceUse=6
u16 placeIndex=P, u16 sourceIndex=S
u32 dependencyContractRow=0
u8 hasFormal=0, u8 flags=0, u16 formalOrdinal=0
```

Multibyte fields remain little-endian. `sourceKind=1|2` denotes **either**
1=Register or 2=Home, not a bitmask. P is R0..R13 containing a captured
Initialize PlaceHandle for an exact, wholly vacant `[E; N]`, rooted in a
role-3 local Home or an independently admitted private real-field/nested-array
construction subplace. It is not a checked reference, raw pointer, parameter
Home or source-visible partial array. Preserve all containing authority.
Resolve its unique using-Module/profile layout and complete E/N before use.

S supplies one completed exact E through its typed Register/Home, snapshotted
once before installation.
Inherited R14/R15 constant-source meanings require exact E context and actual
admission in A2R1; R14 can supply `()` for void, not an array or raw Union.
None is forbidden **even at N=0**. No source expression executes in the record.
N comes only from the destination's exact type/layout: no redundant count,
sentinel, new operand or narrowed quantity is encoded.

The operation copies that seed snapshot into all N logical elements and
establishes the exact array referent complete on normal completion. It retains
the source/evaluation and publication rules above, including private child
versus ancestor publication. RepeatCopy is Initialize-only: it cannot replace
live/maybe-live/partial/reserved storage, initialize a raw Union by partial
alternative writes, or bypass a containing loan. A live source assignment
constructs privately and then uses ordinary complete-array replacement.

Apply the record with assigned D9/control zero or short `92` when its u8
record index fits; form the place with existing record `04` and D8/`91`.
Keep codec `01`, instruction length bands, all record/map widths,
u16 indexes through `0xFFFF` and inventories through 65,536 unchanged.
`6` gains no meaning in CallSite, DestinationPreparation or any other
source-use domain; ordinary stores retain their original rules.
The [registry](../specs/representation/registry.md) gains no opcode, prefix,
escape, record kind, flag, section, type form or new Feature allocation.
Retained arrays still require `20/1 -> 2/1 -> 1/1`; raw-Union value/coverage
uses retain `22/1`, and scalar/C/call/field/target requirements retain their
independent closure. Windows x64 TargetProfile `(1,1)` remains selected.

Use [exact draft selection](../specs/representation/format-versioning.md)
before production, decoding, binding, reconstruction or lowering. Unchanged
type-only providers can satisfy independently checked type/layout/Copy
expectations under their explicit supported schemas, not new Body semantics.
Packing retained RepeatCopy requires this successor. No Feature sniffing,
equal-version inference, decoder retry, A2/A2EU piggyback, private fallback
or compatibility reader is prescribed. Semantic-only source qualification
needs no serialized Body; existing exact schemas remain usable for unchanged
type-only inputs, but cannot retain the new compact operation.

## Integration, resources and native boundaries

* **Exact proposed Bound representation:** add
  `BoundNativeFixedArrayRepeatInitialization(BoundNativeFixedArrayLayout Layout,
  BoundExpression Seed, TextSpan Span) : BoundExpression(Layout.CarrierType, Span)`.
  Layout carries exact E, uint64 N and checked geometry; require
  `Seed.Type == Layout.ElementType`. The written count is checked/folded at
  binding, not retained as a runtime child or duplicate count. There is no
  Elements list. `NestedExpressions = [Seed]`; rewriting replaces Seed once,
  preserves Layout/Span and re-verifies type/Copy/effects. Preserve snapshots,
  failure edges, loans, generations and nested facts through shared traversal.
* **Producer/consumer:** source producers establish seed completeness and
  truthful order/authority/publication facts. Consumers check exact schema,
  framing, closed record fields, indexes, source E, destination array/layout,
  supported requirements and actual bindings before affected use. Derive the
  compact operation's facts from its seed; a serialized live bit is not D.
  [Reader trust](../specs/representation/reader-trust.md) permits reliance on
  producer-published semantic facts without general Body truth re-proof;
  semantic-lie tests belong to source production or explicit audits.
* **Reconstruction/re-encoding:** reconstruct the repeat operation, not N
  list elements or an application-specific loop. Preserve and remap TypeSpec,
  nominal original/actual origins, layout/profile, Imports/Contracts, FieldPaths,
  Homes/register flow and all affected Body/section offsets and SourceMap
  steps. The seed has one evaluation site; the repeat store is one logical
  construction site. Counts/geometry are quantities, not row references.
* **Compact accounting:** select candidate recipe `fixed-array-repeat-state/1`
  for semantic verification/reconstruction: charge the checked type/geometry
  and count comparisons, actual seed analysis, template/fact nodes, paths,
  visited payload/range bytes, sparse updates, joins and retained snapshots.
  Visit stored paths/keys in deterministic path/index order; each field,
  reference check or fact transition and each visited byte has the owning
  resource recipe's elementary charge.
  Validate a repeat rule over its checked count, not N independent source
  evaluations. State/work scale with seed structure and stored exceptions,
  not declared extent; implicit default subtrees are met symbolically.
  Coexisting snapshots/scratch, nested depth and root budgets follow
  [resource accounting](../specs/representation/resource-accounting.md).
  Reserve before construction; nonwrapping charges and explicit exhaustion
  cannot become guessed minima, unvisited success or weakened D/provenance.
  This summary is **not** constant execution or a one-step charge for copying
  N runtime elements: actual execution/byte expansion retains its full work.
* **Native lowering:** preflight actual storage, alignment, scratch and frame
  capacity independently. For supported bounded arrays, perform the captured
  typed representation copies without seed replay, new padding guarantees,
  per-alternative reads or widened/faulting neighbor accesses. Physical loops/
  bulk copies are permitted only when those semantics hold; counters, offsets,
  final increments and products must not wrap. Zero-payload lowering still
  preserves seed effects, complete state and each indexed bounds check.
  RepeatCopy introduces no implicit heap or PE static-data allocation.

Type/layout validity, compact semantic analysis, Standard encoding capacity,
native storage capacity and execution are different gates. Current
[frame lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
has signed-32-bit slot/frame limits; valid huge nuint geometry does not widen
them. Use `Compilation.CreateLibrary`/public verification for huge locals;
qualify **small** arrays separately through selected Standard source-free
re-emission and supervised headless JIT/AOT. Unsupported huge native requests
decline before materialization without partial output. Never allocate/execute
multi-gigabyte fixtures or call semantic acceptance PE/JIT/AOT evidence.

## Required cases and current gaps

These are **future acceptance tests**, not tests run by this documentation
change. Use public source/compiler APIs and active test-area conventions.
For D examples, let Word have uint8 Low and uint64 Wide, with `G=[0,1)`;
let a complete padded alternative have payload `[0,1)` and `[4,8)`.

| Gate | Concrete positive, negative and boundary cases |
|---|---|
| Syntax/identity | `[seed; 0]`, `[seed; 1]`, H=`0x1_0000_0001`, equal uint64 constant expressions/aliases and nested exact targets pass. Missing target/slots, count mismatch, runtime/wrong-domain counts, bad E origins/qualifiers, spreads and repeat trailing comma reject. Existing exact lists retain their cardinality/evaluation behavior. |
| Zero/Copy | N=0 evaluates a side-effecting/throwing seed once; failure publishes nothing. `[]` evaluates no seed. Vacant seed, unique/Cleanup/non-Copy or root-bearing E reject at N=0/1/large N. `[(); 0xFFFF_FFFF_FFFF_FFFF]` has zero payload but live logical elements and checked bounds after its one seed evaluation. |
| Capture/failure | Small N=0/1/3 fixtures count one seed call, retain conditional effects and suppress copies on failure. Later seed-source mutation does not change copies. Failing live replacement preserves the old array and prior RHS effects, with actual cleanup once. |
| Flat high-count D | Repeat a Wide-defined seed at H; first/middle/`0x1_0000_0000`/dynamic-index Wide reads succeed. Replace one high element with Low-only or padded Word: that element's Wide read rejects, another element's succeeds, common Wide rejects. Copy before mutation preserves the captured D snapshot. |
| Common/join | Unknown whole-element Low/padded replacement weakens **both template and overrides**; subsequent possibly affected Wide reads reject. Unknown Low-field writes preserve a previously initialized Wide tail. Test N=1 exact-write strengthening; N=2 with both keys overridden excludes an unused weaker template; branch/loop joins of different seed templates intersect D, never union it. |
| Nested high-count paths | Test `[[Word; 2]; H]` and `[[Word; H]; 2]`, outer/inner high keys, unknown ancestors/indices, a seed with sparse child overrides and copies before/after mutation. One path write preserves nonoverlapping siblings/rows; nested common reads reject exactly when some feasible child lacks coverage. Test raw-Union array fields within currently admitted small aggregates as well as pure array nesting. |
| Geometry/bounds | At S=8, N=`0x1FFF_FFFF_FFFF_FFFF` fits x64 nuint; N=`0x2000_0000_0000_0000` overflows. S=1 permits max-u64 count; S=0 still preserves that count. Check nested product/round-up/offset overflow, N=0/1, negative/N/too-wide original indices and last-valid uint64/int128/uint128 indices before narrowing. Queries keep their existing nint result bound. |
| Padding/provenance | Wide then padded replacement yields D `[0,1)` plus `[4,8)`, not `[0,8)`; Low-only subrange writes preserve untouched bytes. Packed/overaligned and tail-padding cases do not create backing. Repeated pointer/code cells retain the same origin/extent; expired/short backing, overlapping typed-pointer invalidation, lost required facts and zero-extent addresses reject under existing rules. |
| Resources | Hold seed/path/override shapes fixed while increasing N through H and the maximum layout-valid count: traversal, snapshots, Copy, Common, unknown writes and joins remain compact. Increase actual stored keys/nesting separately; test supported work/state/depth limits at and beyond their inclusive boundary, nonwrapping charges and no partial success on exhaustion. Native payload bytes are not allocated by semantic tests. |
| Standard isolation/remapping | Round-trip RepeatCopy with scalar/unit/raw-Union/nested-array seeds and changed map/record/Home/field/provider indexes, including short/full index boundaries. Reject value 6 under old schemas, wrong role/type/transition/None, bad reserved fields/references/requirements and incompatible bindings. Retain D/R/C without source/debug data or fallback. |
| Native separation | Small supervised JIT/AOT fixtures verify once-only effects, copies, failure/publication, fresh readback and untouched neighbors after Standard remapping. Separately verify native capacity decline for unsupported huge frames; no huge execution, silent heap fallback or emission-only runtime claim. |

Current implementation observations, not newly run qualification:

| Evidence | Required work, not a missing accepted rule |
|---|---|
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.NativeSampleArray.cs), [bound initializer](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) and [array verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.NativeSampleArray.cs) | Only exact lists are represented; add the proposed source/bound repeat route and all shared traversal/failure checks without changing list invariants. |
| [`RawUnionArrayElementState`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | It stores GuaranteedBytes plus sparse `Dictionary<ulong, byte[]>` overrides. Known access, Copy and Common visit stored keys, but it cannot install a richer uniform seed; unknown writes currently update overrides and Merge requires equal defaults. Generalize per-instance templates, nested/default path coverage and guarded provenance without changing type G or allocating extent-sized masks. |
| [Standard writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs) and [source-free reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs) | The writer visits list elements and the reader accumulates an element list until N is complete. Add exact successor selection, record reader/writer/validation and compact reconstruction/remapping; existing index-counter Homes are not a repeat/completion carrier. Other persistence routes must retain the node under their independently exact schemas or explicitly decline it. |
| [Layout](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs), [VerifyCore](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), [support inventory](../implementation.md) and [CG-004](../implementation/conformance-gaps.md#cg-004-native-declaration-and-body-integration) | Checked count-times-stride can exceed int/u32 while fitting target nuint. Array formals/results still reject; struct-field size/mask and native frame representations have narrower implementation limits. Those do not redefine layout validity or justify an ABI/test-state bypass. Keep huge qualification in supported local/nested semantic paths and report remaining field/storage gates separately. |

Upon adoption, grammar/construction own the new expression, initialization/
expressions own once-only capture and whole publication, and the instruction/
registry chapters own record-05 value 6 under the exact successor.
Complete types, layout/ABI, A2 Copy and A2R1 remain the referenced owners of
unchanged rules; resource recipes and conformance cases integrate through
their existing owners. No owner is amended by this proposal.

**Remaining user decision:** approve, amend or reject this repeat contract
and its exact successor together; they remain unapproved and only provisional
under the quoted authorization. No in-scope evaluation, zero-count, Copy,
coverage or wire sub-decision is left to implementation. Whole-array ABI,
general generators/owning arrays, other target/schema compositions and wider
native frame support are independent decisions/work, not prerequisites for
the proposed semantic-only high-count slice.
