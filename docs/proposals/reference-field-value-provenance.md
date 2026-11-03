# Field-projected value provenance across calls

**Status: unapproved; provisionally actionable solely under the user's
authorized missing-contract instruction.** This is a bounded addition to
[reference provenance carriers](reference-provenance-carriers.md), not
approval of that proposal, a normative amendment or an implementation claim.
The [root/pin runtime proposal](reference-root-pin-runtime.md) remains the
sole candidate owner of its runtime support, layouts and operations.
The [fresh inline unique-constructor extension](reference-unique-constructor-field-provenance.md)
separately proposes superseding only this candidate's Copy-only destination
root restriction; its addition remains unapproved and does not widen Standard
TRRM.

## Decision and genuine gap

Keep the existing operation-derived ledger. Add an optional, producer-published
**entry-value copy-origin summary** for Terrum-authored synchronous Copy
results and fresh inline Copy construction. A summary identifies which staged
input value or input field can supply each result field's complete value facts.
It is not a field-granular DependsOn/Captures permission, a mutation-effect
summary or a graph of runtime instances.

Local field tracking needs no new source contract. The undecided boundary is
the **published callable fact** needed to preserve exact field origins through
an independently compiled, bodyless expectation:

| Exact owner | Existing rule and consequence |
|---|---|
| [References](../specs/language/references.md#descriptors-referents-and-bindings) | Copying a stored descriptor preserves its referent; referencing its field slot instead borrows containing storage. |
| [Dependencies and views](../specs/language/dependencies-and-views.md#stored-values-and-flow), [ownership](../specs/language/ownership-and-cleanup.md#dependencies-carriers-and-exceptional-ownership) | Fields, replacement, partial state and surviving copies retain their actual obligations. Private field knowledge cannot narrow a broad whole-source bound. |
| [Dependency clause](../specs/representation/contract-clauses.md#6-dependency), [logical records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) | Whole receiver/formal permissions do not encode a field-to-value mapping. FieldPath identifies declarations/layout paths, not a callable's value transfer. |
| [Register Body](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations), [field operations](../specs/il-instruction-set.md#fields-elements-and-ranges) | Operations and incoming edges reconstruct local facts. Calls consume published contracts rather than rescanning a callee at each invocation. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts) | A stronger provider fact actually used must remain a retained replacement obligation. Merely seeing its current body is not such a published guarantee. |
| [Lifetimes](../specs/lifetimes.md#by-value-results-and-borrowed-results), [Windows calls](../specs/targets/windows-x64-call-interface.md#4-by-value-copies-and-result-storage) | By-value copies/results are independent value residences; indirect ABI storage is not a source loan or provenance carrier. |
| [Windows layout](../specs/targets/windows-x64-data-layout.md#6-access-padding-and-observations), [deployment](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Physical overlap/alignment and retained provider/code origins remain independently required. |

The carrier proposal already supplies StorageKey, ValueKey, CapabilityKey,
current place contents and partial-child tracking. It therefore resolves the
**local** identity problem, including a once-derived intra-module body summary.
It does not assign an exchange representation or compatibility meaning to an
exact result-field summary. Unioning every permitted source is a sound
whole-source bound, but cannot distinguish two fields' selected referents for
an alias/access proof. It must not be presented as that exact mapping.

**Selected minimal integration:** derive summaries once for authored bodies,
publish only the bounded copy-origin facts below, and retain them explicitly
when a caller uses them. Select one necessary private declaration transport,
candidate **95**, composed with [candidate 94](scalar-wide-value-carriers.md#exact-private-schema-and-composition).
No standard summary encoding is selected; precise imported uses lacking a
supported transport remain unavailable.

**Alternative:** local summaries plus conservative whole-source imports needs
no new bytes and remains a valid lower-precision implementation. It cannot
claim exact separate-compilation field origins. A general symbolic heap/effect
program, field-granular lifetime permissions or runtime provenance tags would
solve broader problems, but require substantially more contracts. None is
necessary for copying two independently backed descriptors through a factory.
Jointly owned/regenerated bodies can also use once-derived summaries without
publishing them. Requiring a bodyless caller to obtain/analyze the provider Body
or pin its private implementation is not the selected declaration-only route.

## Field identity and current values

Use the carrier proposal's keys and Attachment mechanism unchanged:

* A field projection is an ordered path of exact instance-field declarations
  under the complete containing type/substitution, appended to the evaluated
  parent StorageKey projection. It is not a field name, byte offset, local
  field number or a new source-annotation recipe. Nested inline fields extend
  the path; following a stored handle/reference starts from its actual selected
  backing/place, not from inline bytes of the handle slot.
* A projected place retains its parent's actual generation, backing,
  initialization and access restrictions. Every containing readonly/fixed
  restriction still applies. Distinct paths alone do not prove disjointness.
  Prefix overlap, raw Union alternatives, ranges and unknown elements require
  the admitted overlap rules; a field path cannot improve alignment.
* Keep a value's structural child facts separately from its containing
  storage identity. An immutable compiler-side field tree over existing
  ValueFacts is sufficient for computed aggregates without a Home.
  Materialization installs that tree under the destination's projected
  StorageKeys. Copy creates independent dependent extents with the same
  selected referents/capabilities; transfer follows its existing ownership
  rule. Neither operation copies the parent's storage identity.
* Whole reads include all live child obligations and independently attached
  whole-value bounds. Reading/extracting one child selects that child's facts,
  **plus every applicable enclosing published bound**. Never use exact
  copy-origin facts to subtract sources from DependsOn/Captures.
* A checked reference to a field slot keeps that slot/parent alive and reads
  its current contents. A copied descriptor field instead preserves the
  value obtained at its read point. Rebinding the parent handle or replacing
  the field cannot retarget an already captured place or rewrite old copies.

For a checked, nonoverlapping field replacement, prepare the destination once,
stage the replacement, then apply ordinary validity/dependency/retirement
checks and commitment. Replace only that subtree's current value facts;
recompute derived ancestor unions rather than monotonically retaining every
old field origin. Do not remove an independently attached conservative capture
bound. A permitted Copy write does not itself end the field's StorageKey
generation. Old copied values keep their own facts.

A write through an alias updates the same resolved place. If its selected
target has several possibilities, merge the possible old/new states for
each overlapping target; do not strongly replace every candidate. A raw,
unknown or unsupported overlapping write invalidates the affected exact facts,
not unrelated disjoint siblings. Retain possible obligations until their
actual holder/endpoints are established; invalidation is not retirement.
Missing required content/validity facts become Unavailable, never Known empty.
The accepted unsafe/warning policy can still admit a known dangling raw
address as inert data; its preserved origins do not authorize a later access.

Joins operate per resolved field path and retain each path's possible origins,
guards and live/partial state. Unknown on one reachable edge prevents an
unconditional exact fact. Loops use the existing finite scope/site fixed point;
recursive or exhausted summary inference cannot bootstrap a known-empty fact.
An independently live child remains usable only under its existing partial
aggregate rules.

## The bounded callable guarantee

The following is semantic-IR notation, not Terrum source or a runtime layout:

```text
CopyOrigin = (SourceKey, EntryValue | EntryReferentValue, sourceFieldPath)
CopyFact   = (destinationFieldPath, nonempty set<CopyOrigin>)
Summary    = Unavailable | (NormalResult | FreshInlineConstruction, set<CopyFact>)
```

Paths contain only already identified inline instance fields. An empty path
selects the whole root value. Destination paths are prefix-free: one fact may
copy an entire subtree, or facts may describe separate children, but cannot
overlay one another. Each covered source/destination subtree has the same
complete admitted Copy type. This guarantees the value provenance of copying
one listed entry value, preserving all its applicable ValueFacts and independent
backing/code obligations. It is not merely equal pointer bits or equal types.
It performs no Copy/transfer itself and changes no call effects, responsible
residences or Pure/evaluation authorization. Different normal branches may
contribute different alternatives.
Uncovered dependency-bearing subtrees are Unavailable, not independent.
Publish facts only for provenance-bearing subtrees; dependency-free scalar
children need no invented provenance fact.
Unavailable origin precision does not vacate a published live field or erase
its whole-source bound, complete type or other established facts.
Alternative sets are unconditional and have no cross-field correlation by
position. Preserve supported local discriminator guards normally; a use needing
an unrepresented imported correlation must refuse rather than inspect branches.

The initial guarantee does not describe pointer arithmetic, fresh acquisitions,
an active Union member, optional-payload paths, arbitrary index paths, erased
values, unique results or arbitrary post-call heap state. Existing operations
can establish richer local facts, but cannot serialize them as a CopyFact.
Returning an entire admitted Copy aggregate unchanged can use empty paths
and preserve its complete nested fact tree.

### Input binding and normal publication

1. Select the declaration and stage arguments/receiver once in ordinary source
   order. Bind SourceKey through the checked formal/witness map; receiver is
   never formal zero.
2. EntryValue selects a staged by-value Copy input's value facts. It does not
   borrow caller/callee parameter storage. EntryReferentValue selects the
   current completely initialized T facts of an admitted checked ref<T> input
   or borrowed inline receiver **at Invoke**, after later argument effects.
   The captured reference identifies the place; it is not an earlier snapshot
   of T. An initialization/output formal, raw pointee or fresh constructor
   receiver is not an entry source.
3. Keep the selected entry facts as compiler snapshots for instantiating the
   promised value-copy relationship. This inserts no runtime read, copy,
   snapshot, root or owner. A copied ref<T> still designates current T storage;
   it does not freeze T. The producer must establish that an actual returned
   copy has the promised entry origin despite alias writes during its body.
4. On normal result publication, instantiate each fact against that invocation
   and its saved source values. Copying a known subtree preserves its child
   facts; a union retains all listed alternatives. Unavailable source
   backing/selection remains unavailable in that dimension. Attach the complete
   original result dependency relationship independently. Source-path selection also retains
   every applicable enclosing source bound, as an ordinary field read does.
5. FreshInlineConstruction applies only to a fresh inline Copy receiver's
   fields at ordinary successful constructor completion. Constructor Captures
   retains its full bound. Caller initializer entries and final validation
   remain subsequent phases where specified; update child facts normally and
   do not publish a complete parent early.

The normalized whole-source relationship must permit each summary source and
its actual borrowing/forwarding mode. Copying the original ref<T> does not
become backing-only. Source-field selection supplies no new source permission,
output-source projection, lifetime extension, write authority or ownership.

Source checking derives/checks these facts from typed operations once per
body; calls compose already established summaries. A local summary is a
derived compiler fact, not automatically a stable exported promise. Publishing
it makes the stated guarantee a producer obligation. An imported caller uses
the published summary, not private body inspection, parameter order, matching
field names or the fact that a provider happens to be available for inlining.

No mutation-effect contract is added. Call effects still update/invalidate
every possibly affected current place, including aliased receiver fields and
reachable ambient state. Readonly access and NoAlias are not purity;
without a supported effect description, affected exact post-call contents
are Unavailable. Staged by-value copies remain independent. An entry-origin
result fact cannot restore the original caller place's pre-call state.

### Failure, cleanup and opaque calls

No normal result/constructor summary applies before its normal completion.
Later-argument failure preserves the actual staged holders; moved sources do
not revive. Failed unpublished construction keeps acquired child facts until
reverse-acquisition cleanup; it does not invoke incomplete-parent Cleanup.
Published values, existing-holder captures and mutations surviving failure
retain their actual obligations. Unknown exceptional mutation cannot be
treated as rollback, vacancy or an unchanged field.

Complete/partial retirement keeps the accepted declaration order and surviving
sibling, external-holder and later-cleanup dependencies. Ending one descriptor
does not end its referent or another copy. This summary adds no runtime support
hold; where root/pin Copy is independently admitted, it preserves the same
capability under the root/pin proposal, not a new acquisition.

Bodyless import expectations may retain a selected Terrum provider's guarantee;
raw foreign declarations/fnptr types acquire no new summary attachment.
For a bodyless/opaque call without a published supported summary, instantiate
the whole dependency bound, but mark its unestablished field origins
Unavailable. A supported use needing only that bound can proceed; one needing
exact selected-place/alias facts must refuse. Do not manufacture field facts
by unioning all arguments, treating absence as empty, inspecting foreign code,
or attaching a borrow of an indirect ABI buffer.

In particular, [NativeOutput cells](aggregate-native-outputs.md#destination-states-order-and-failed-writes)
keep their successful-inner-value bound, complement, maybe-live, pre-Invoke
cancellation and indeterminate post-Invoke failure. The separate
[source-dependency applicability](aggregate-native-outputs.md#source-dependency-applicability)
still does not apply this summary to output destinations or admit them as
sources.
Exact fields of an opaque successful packet remain unavailable unless
subsequent admitted local operations establish them. No new annotation or
native-output projection is needed for the selected ordinary-call slice.

## Necessary private transport: candidate 95

This subsection is an **unapproved exact private schema**, not standard TRRM.
Use exactly candidate 94's complete composition, including the carrier
proposal's revision-93 declaration collection, then add this one field after
each ModuleFunction's general-dependency collection and before its
expected-vtable presence Boolean:

```text
u8 summaryKind                     // 0 unavailable, 1 normal result, 2 fresh inline construction
if summaryKind != 0:
    i32 bindingCount               // >= 1
    repeat bindingCount:
        Path destination
        i32 alternativeCount       // >= 1
        repeat alternativeCount:
            u8 sourceKind          // 1 explicit formal, 2 receiver
            u8 sourceView          // 1 EntryValue, 2 EntryReferentValue
            u16 reserved = 0
            u32 sourceOrdinal
            Path source

Path:
    i32 depth                      // >= 0
    repeat depth:
        u32 instanceFieldOrdinal
```

All integers are little-endian. A path is exactly `4 + 4*depth` bytes; an
alternative is `12 + 4*sourceDepth`; a binding is
`8 + 4*destinationDepth + sum(alternative extents)`. The absent form is one
byte; a present summary is five bytes plus its bindings. It has no flags,
padding, runtime keys, address, source text, operation-occurrence IDs or
persisted verifier cache.

Each field ordinal is zero-based in the **immediate complete containing
type's ordered structural instance-field collection**, including admitted
implicit property backing in the ownership chapter's order. Resolve it to that
declaration under the root's selected nominal origin/substitution, then derive
the next containing type. It is not a global field-table index or byte offset.
Root types come from the complete callable result/constructed receiver and
the selected formal/receiver view. Intermediate steps must be supported inline
aggregates; static/computed members and indirection steps reject. A private
representation unable to retain that exact type/field origin is unsupported.

Include the field on every definition and expectation, including bodyless
ones. Tag 0 explicitly supplies no additional guarantee. Keep one declaration
authority; restore its summary onto the bound callable rather than serializing
a second body-owned copy. Reject uninitialized in-memory collections for
present summaries. Ordinary result kind must match its callable; construction
kind requires the already admitted fresh inline Copy constructor role; both
require an ordinary synchronous Terrum provider, not a foreign declaration.
Source ordinal is zero for receiver, otherwise the zero-based explicit formal
ordinal; preserve revision 93's private implicit-receiver position conversion.
SourceView 1 requires an eligible by-value Copy formal; receiver uses view 2.
SourceView 2 requires an admitted fully initialized checked referent/borrowed
inline receiver, not an init formal or unchecked pointer.

Writers order bindings lexicographically by unsigned destination ordinals,
and alternatives by `(sourceKind,sourceOrdinal,sourceView,sourcePath)`.
Readers may normalize physical reordering; duplicate alternatives, duplicate
or prefix-overlapping destinations and physically overlapping unsupported
aggregate paths reject. Check nonnegative counts, positive required counts,
`4*depth`, `12*alternativeCount`, `20*bindingCount` minimum extents and all
sums against remaining bytes/resource limits before allocation. Validate
tags/reserved values, callable roles, ordinal namespaces, field owners,
complete Copy type agreement and source permission applicability.

Candidate 95 is necessary **only because this recommendation chooses exact
declaration-only private exchange**; derived local summaries need no new
version. The inspected proposals use 89–94 and assign no 95. This composition
does not give the new semantics a dependency on wide scalars, nor redefine
93/94. At its integration boundary select exact 95 on all structured
writer/header/nested-reader paths, even when every tag is zero. No `>=`
decoding, old-header substitution, alternate 95 layout or compatibility reader
is selected. Equal revision bytes still require exact agreement on this whole
draft. Preserve the selected outer TNPK/TNSP/TNGP version 3 and provider
binding contract from candidate 94; admit inner/outer schemas independently,
recompute extents/checksums and regenerate affected images.

### Binding, transformations and consumer obligations

Preserve every original whole-source expectation. An expected CopyFact is
satisfied only when the mapped provider guarantee covers that destination and
its possible origins are a subset of the expected alternatives. Normalize
whole-subtree copies to the required child selections before comparison;
types, selected owners, entry view and phase must agree. Missing/Unavailable
cannot satisfy an expected known fact. Extra provider precision may be ignored,
or explicitly retained if used; it cannot silently rewrite a generic/interface
requirement or remove its source bounds. Unsupported witness-summary transport
remains gated.

Remapping visits callable/formal/receiver identities, complete root types,
every ordered field declaration and retained expectation. Rebuild ordinal
paths after type/field projection or reordering; equal names, offsets or
ordinals across providers do not establish correspondence. Reconstruct local
ledger keys for cloned/reordered bodies. Declaration projection retains every
referenced type/field closure; omitted private fields cannot be guessed from
layout or names. Retaining those records grants no source lookup/access.
Native consumers retain and compare
the same expected summaries in their embedded semantic contracts even if no
machine import remains. A changed field mapping can satisfy an old broad
DependsOn contract yet fail a caller's retained copy-origin guarantee.

Producer checking establishes truthful copy-origin facts. Consumers perform
exact-schema/framing, range, type/reference, support and binding checks before
affected use, then may rely on those facts **without semantic re-proof** of a
provider body. A structurally valid but false source-to-field claim is a
producer violation, not a promised consumer diagnostic. Optional audits remain
separate under [reader trust](../specs/representation/reader-trust.md).

No standard Feature, clause, table, Body record/opcode or private expression
tag is allocated. Standard operations still reconstruct local facts, but the
current standard declaration codec cannot exchange this new guarantee.
Standard writing/replacement needing a retained imported CopyFact rejects;
do not hide it in Attr, FieldPath, unused bits or an optional section.
Standard uses that require only existing contracts need not acquire it.
This explicit standard gate is not closed by private 95, recognizable field
operations, or the root/pin proposal's separate standard runtime program.

## Required cases and integration boundary

These are acceptance cases, **not test results**. Use public compiler APIs and
unrelated synthetic declarations; test the same semantic cases locally and
through independently encoded provider/consumer expectations.

| Family | Required outcome |
|---|---|
| Two independent fields | Locally initialize Left from backing A and nested Right from B; read each without importing its sibling's value origins. A copied descriptor does not borrow its wrapper slot. A reference to that slot does. |
| Replacement and copies | Copy Left, replace only Left with C, preserve Right=B and old copy=A. Without a published whole bound, A can end when its remaining valid-use/cleanup dependencies permit it. Replacing the complete aggregate does not mutate prior copies; inert raw addresses cannot justify later invalid access. |
| Broad-bound negative | A constructor/factory names A and B in its whole-source bound. Even with an exact Left=A/Right=B summary, field extraction/replacement must not authorize shortening that retained bound or disposing a captured source early. |
| Calls/constructors | Publish two distinct field-copy origins, including an unchanged whole aggregate, nested paths and swapped named arguments. Normal result and fresh construction have distinct roles; caller initializers/final validation retain their phases. No caller/callee ABI-copy slot becomes backing. |
| Useful imported precision | A field-copy summary preserves two known distinct selected regions across an opaque ordinary factory boundary for a separately admitted alias/access check, while keeping both whole lifetime bounds. Union-of-all-arguments cannot count as this evidence. |
| Aliases/overlap | Two checked aliases of the same parent see the same later field value; earlier descriptor copies do not change. Distinct formals bound to the same actual region remain aliased. Test prefix overlap, raw Union overlap, known disjoint siblings, unknown indexed writes, repeated generations and equal reused addresses. No false strong update or disjointness by path spelling. |
| Joins/loops | Swap field origins on one branch; replace one field on another; join a missing/partial/unavailable child. Retain per-field alternatives/guards and conservative bounds, without restoring whole completeness or losing loop-carried obligations. |
| Staging and mutable referents | A later argument changes a referenced descriptor field before Invoke; use the current entry facts. A provider reads after a different body mutation cannot publish a false entry-copy fact. Reference descriptors never snapshot their referent's contents. |
| Failure/cleanup | Fail during replacement, a later argument, constructor validation and after existing-holder mutation. Preserve old/current/staged/acquired holders at their actual endpoints, sibling/later-cleanup dependencies, exact order and no cleanup replay. |
| Roots, owners and code | Copy a supported rooted/pinned descriptor or function pointer through nested fields; retain the same capabilities/code origin, not new acquisitions. Rooting does not repair a released owner/guard, and a field path adds no access or alignment. Unsupported private root/pin types remain rejected. |
| NativeOutput | A successful two-pointer packet retains its declared whole bound but has no invented per-field map. Complement publishes no value; maybe-live, cancellation and exceptional indeterminacy keep their distinct obligations. Reject raw output sources and address escape. |
| Unavailable | Opaque/fnptr/witness calls without a supported summary, fresh provenance, unsupported transformations and exhausted inference do not become known-empty. Permit only uses supported by the remaining whole contract; refuse exact-fact-dependent uses. |
| Malformed consumer input | Independently encode truncated/negative/oversized counts, bad tags/reserved values, wrong callable/foreign role, out-of-range formal/receiver ordinals or inconsistent field owners, static/non-inline paths, wrong Copy type, init/raw-pointee source, duplicate/prefix/overlap paths and illegal source permissions. Reject before affected use, including declaration-only/uncalled requirements. A well-typed but false mapping is separately a producer-truth case. |
| Separate compilation/replacement | Strip callee bodies from expectations; preserve the published mapping through source-free restore, projection, module sets/graphs and retained native images. Accept compatible origin-set narrowing; reject a lost/swapped required mapping even when the broad dependency contract still matches. No body-peeking repair. |
| Codec/rewrite boundaries | Independently supply exact-95 bytes, empty/nonempty declaration sets, nested field paths, reordered types/fields/functions and diamond providers. Rebuild all references/extents/checksums; reject wrong inner schema despite a valid outer digest. Local derived summaries require no new codec; standard imported exact facts remain gated. |

Observed implementation evidence is narrower:
[`SemanticVerifier.TokenSpans.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.TokenSpans.cs)
stores NativePointerSources by slot, reads a field's receiver origins, and
collects field writes and pointer-bearing call arguments into aggregate sets.
Its StorageProjection distinguishes whole/range/known element/unknown element,
not nested fields. The active
[`NativePointerModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativePointerModuleTests.cs)
and [`PublicFixedLayoutStorageTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/PublicFixedLayoutStorageTests.cs)
assert pointer/function-pointer field representation and projection.
[`DirectNativeImportTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs)
asserts a one-pointer-field output's whole dependency through a copy.
Those assertions do not establish independent two-field replacement or a
published field-transfer summary. The test project has no Compile exclusions;
no build, test execution or native qualification is claimed here.

On adoption, integrate this bounded guarantee with the source dependency and
ownership owners' distinction between exact value facts and whole bounds,
logical records/reader trust/binding, and the carrier proposal's declaration
exchange boundary. Compiler integration belongs to shared bound facts and
traversal, source verification, per-function summaries, call instantiation,
private projection/binding/restoration and lowering. Preserve all independent
type, runtime, initialization, cleanup and target gates.

There is no remaining choice inside this bounded recommendation. Formal
approval remains outstanding. Exact **field-granular lifetime permissions**,
arbitrary mutation summaries, native-output source projections, a standard
summary codec and broader ownership/runtime transports are excluded decisions,
not silently approved follow-up policy. No normative owner or implementation
inventory is changed by this proposal.
