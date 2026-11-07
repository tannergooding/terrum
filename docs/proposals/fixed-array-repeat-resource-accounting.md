# Fixed-array Repeat resource accounting

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract instruction.** This is a candidate completion of
`fixed-array-repeat-state/1`, not approval of Repeat, compiler support,
normative integration or a stability promise. [Proposal index](README.md).

## Decision, scope and owners

The [Repeat proposal](standard-native-fixed-array-repeat-initialization.md#integration-resources-and-native-boundaries)
names the recipe but does not determine its action expansion or logical
map/path storage. Recommend the ordered sparse reference recipe below.
It charges the actual seed/facts and stored exceptions, never N implicit
elements. Unlike charging host dictionaries or allocator sizes, it gives
reproducible receipts; unlike a general proof-state redesign, it adds only
the missing Repeat storage and traversal conventions.

| Owner | Preserved contract and intended integration |
|---|---|
| [Resource accounting §§2, 4–6](../specs/representation/resource-accounting.md) | Root aggregation, separate phases, elementary actions/bytes, simultaneous live state, inclusive preflight and receipts remain authoritative. On adoption, integrate this exact recipe and its logical shapes there; replace only the Repeat proposal's incomplete compact-accounting summary with a reference. |
| [Source proof accounting/envelope](../specs/language/proof-and-constant-evaluation.md#proof-outcomes-and-accounting) | Preserve scalar fact shapes, reference-floor traversal and numerical minima. Repeat contributions use the owning proof ledger when serving a source proof; this is not a new proof engine or a constant-execution shortcut. |
| [Repeat facts/joins](standard-native-fixed-array-repeat-initialization.md#repeated-facts-nested-paths-and-joins) and [raw-Union facts](standard-trrm-raw-union-initialization.md#minimum-representation-facts-and-transfer) | Preserve once-only seed evaluation including N=0, C plus sparse M, D/typed validity, guarded backing obligations, captured paths and build-before-publication. Neither proposal's approval status or non-resource choices change. |
| [Consumer verification](../specs/representation/reader-trust.md) | Mandatory safe decoding/binding remains distinct from optional semantic re-proof. This recipe does not require consumers to re-prove producer-published body facts. |

No source syntax, runtime array representation, API, wire field, Feature,
schema/revision selection or numerical resource envelope is allocated.
Private Repeat persistence and native storage/execution remain separate gates.
All recommendations below, including byte costs, are **unapproved**.

## Candidate logical state

Use an ordered tree of checked fact nodes. A Repeat node has one template C
and an ascending vector M of distinct `(uint64 index, child)` entries,
`0 <= index < N`; N comes from its checked layout, not a second stored extent.
Nested arrays retain nested nodes, not a flattened product. Ordinary inline
fields use their admitted fact subtree in declaration order. Preserve all
carried typed/guard/backing facts, not just initialized-byte masks.

These are **abstract accounting records**, not host layouts or codecs.
Each listed record/list item is one logical state entry, with its scalar
fields/references included; nested records and variable payload count
additionally. Every reference targets an already charged, checked context
or a charged child. The smallest proposed otherwise-unowned shapes are:

| Logical object | Live abstract bytes and fields in traversal order |
|---|---|
| Repeat component of a retained snapshot | 16: root count, root-list reference; each root below counts separately |
| Root attachment | 32: storage-context identity, value generation/revision, checked type reference, fact reference |
| Repeat map | 24: checked layout reference, template reference, stored-key count; plus 16 per entry: full-width index, child reference |
| Materialized/captured path | 16: root-context reference, step count; plus 16 per step: kind, selector |
| Otherwise-unowned byte-vector or range-list fact | 32: fact kind, checked type reference, context/revision reference, payload length or item count; plus B bytes for a byte vector, or 16 per range: start, exclusive end |
| Active Repeat operation frame | 48: operation, left reference, right reference, result reference, cursor, path-context reference |

Each named metadata field is one abstract 64-bit quantity. This width is a
budget convention, not a narrowed source index, new metadata-reference width
or target pointer ABI. A path step is Field(kind 0, declared ordinal),
Default(kind 1, selector 0), Index(kind 2, full uint64), or query-only
Unknown(kind 3, selector 0); derive each owner/type from the checked root and
preceding steps. Unknown is not a retained exceptional key. Charge its
index-domain facts separately under their owner.

Use an existing exact fact-owner shape instead of adding a second wrapper
header for that same fact. The proposed 32-byte payload header applies only
where no such shape is assigned; it does not replace scalar proof headers.
Range/region semantics and compact repeated-region children retain their
owning representation. This recipe does not turn a compact region into a
byte-per-covered-position mask. Missing child shape/action accounting is an
unsupported prerequisite, not a zero-size fact or permission to drop it.
Selected child-recipe editions and their logical input shapes are part of
the checked context; do not switch shapes during an operation to fit a limit.

At N=0 retain a map with null template and no entries, but fully analyze and
stage the seed before retiring it. At N>0 retain C even if every element is
overridden; Common includes it only when the feasible domain has an
unoverridden element. An implicit default edge costs no per-element entry.
Stored paths count their full descriptors/steps; implicit tree prefixes do
not additionally materialize path vectors. No source-required supporting
storage or acquisition is retired early by this analysis-state convention.

For these objects the live byte total is the sum of all table instances,
their payloads, and live scratch/output buffers. Existing owner-assigned
child facts add their own exact sizes. Selected retained input counts under
the resource chapter's backing-interval rule as well. If the selected
envelope also limits logical entries, apply the table's entry count alongside
bytes; neither dimension substitutes for the other.

For example, one nonempty root with one eight-byte D vector, no overrides
or materialized path and one active frame costs
`16 + 32 + 24 + (32 + 8) + 48 = 160` bytes, **before** seed staging,
other facts and retained input. One distinct eight-byte override adds
`16 + 32 + 8 = 56` bytes. These are candidate size oracles, not envelopes.

Only immutable type/layout/G/P input may share a charge, with the same checked
identity, context and supporting lifetime. A map's mutable D/template,
overrides and independent logical snapshots do not share charges merely
because the host interns nodes or uses copy-on-write. A captured copy owns
its complete logical fact tree. This preserves the source proof rule that
coexisting branch snapshots count separately.

## Exact work expansion and traversal

All charges are additive. Metadata fields above are abstract scalar fields,
not encoded byte buffers; their byte sizes determine live state, while each
logical input/output field read/write costs one action. Processing an encoded
field additionally charges every encoded byte examined/generated in the
structural phase. An existing child-fact recipe supplies its internal
expansion instead of receiving these wrapper actions a second time; charge
the Repeat attachment edge and all of that owner's actions.

| Primitive | Work units |
|---|---:|
| Read or write one fixed-width logical field, including a key/selector/count/reference | 1 each |
| Visit one list item, follow one graph edge, or check one reference | 1 each, separately |
| One index/range check or bounded scalar comparison/calculation | 1 per check/comparison/calculation |
| Read, install, remove, logically copy or meet one fact/obligation node | 1 per transition, plus its fields, payload and children |
| Read one payload byte | 2: one byte-element action plus one examined byte |
| Generate one payload byte | 2: one byte-element action plus one generated byte |

Thus a logical payload-byte copy costs 4, equality costs 5 per compared pair
(two reads plus comparison), and bytewise meet costs 7 per produced byte
(two reads, one scalar combination, one generation). Header/fact/edge actions
are additional. Zero payload does not erase node/field/transition costs.
For ranges, process each endpoint as a field and each comparison separately;
never charge the number of implicitly covered bytes unless they are actually
examined/copied/generated. Repeated visits/copies incur their full charges,
including visits physically avoided by caching.

Read a header once on entry; retain current list heads until consumed.
Each list availability test costs one index/range check, including the final
failed test; binary search tests `lo < hi` each iteration, computes its
midpoint with three scalar calculations (subtract, divide, add), and charges
`mid + 1` separately when advancing lo. Cursor advance is part of the list
element action, not an arbitrary sequence of host loads/stores. Incremental
output lists start with count zero and write the count once per appended or
removed item. Established checked identity handles may be compared as bounded
scalars; establishing their type/binding identity is fully charged under its
owner, not collapsed to a handle comparison.

The following expansion defines the reference algorithm, not a charge for
an arbitrary helper callback:

1. **Enter/construct/retire.** Enter an input node by following its reference,
   checking it, reading its fact, then reading every header field in table
   order. Visit a list item before its fields; following its child performs
   that child's ordinary entry expansion. Construct a node by reserving its
   header and known byte payload, installing its fact and writing its header
   fields in order. Write a child reference only after its complete child
   returns, keeping the parent's reservation live meanwhile; do not create
   an uncharged placeholder copy. Reserve/visit/install each appended item
   before writing its fields.
   Payload bytes run from offset 0 upward. A frame's entry costs seven actions
   (install its obligation, write six fields); exit removes its obligation
   for one action. Semantic removals visit affected facts in child-before-
   parent reference order. Last-use retirement of an entire snapshot/buffer
   is bookkeeping, not a new semantic transfer or payload scan/zeroing.
2. **Order/lookup.** Roots follow bound declaration order (selected Body Home
   order after reconstruction), then storage generation order. When walking
   children, visit C first, then keys ascending as unsigned uint64; ordinary
   fields follow declared ordinals. Materialized paths sort
   lexicographically by `(kind, selector)`, prefix before extension. Known-key
   lookup uses lower-bound binary search with midpoint
   `lo + floor((hi-lo)/2)`: visit/read the midpoint key and compare it with
   the query once, then test final position availability and visit/read its
   key for one equality comparison if in range.
   Check the original index against N before lookup, without narrowing.
   Path lookup processes each original step outer-to-inner, checking its
   kind, owner/type continuity and bound/reference before descending.
3. **Repeat/seed.** Charge the ordinary checked type/Copy/layout/geometry and
   count operations individually, including overflow/range checks. Analyze
   the bound seed once under its ordinary binding, effect, structural and
   proof recipes; this includes all analysis those recipes require, not one
   observed runtime path or inspection of an opaque callee's private body.
   Every required owner pass/revisit is still charged.
   Stage its completed facts, then logically clone them into C for N>0.
   Nested Repeat invokes this same expansion once per authored nested node.
   N=0 does not skip seed work or its peak staging. Required constant work
   for a count/seed retains its own evaluator recipe, not this compact rule.
4. **Clone/equality.** Clone every header field, stored item, payload and
   child in reference order, charging a copy transition per fact as well as
   input entry and output construction. Write each destination field once
   in its rebased form; charge the owning identity/path-rebase operations,
   never rebase pointee/code origins. Equality enters both nodes, left then
   right, compares semantic fields/keys/payload in order and stops at the
   first difference.
   Child references are followed, not compared as allocation addresses;
   copy-local bookkeeping revisions do not make equal facts unequal.
   This is owner-normalized fact-representation equality, not a general
   semantic-equivalence solver. A hash/cache hit cannot waive its expansion.
5. **Writes/Common.** A write constructs a fresh affected root from the
   current post-RHS snapshot, cloning unchanged children and applying the
   ordinary transfer at the captured path to changed children. Stage each
   complete replacement while its predecessor remains live. Merge a new
   known key into the ascending source-key stream, not by an uncharged
   vector shift. Compare every candidate output override with result C and
   append only deviations; path normalization returns inner-to-outer.
   An unknown write transforms the original C, then each original stored
   override, and normalizes against the new C in ascending-key order; updating
   C must not change later input facts. A proven singleton uses exact
   replacement, including N=1. Common starts
   with a cloned C only if an unoverridden candidate exists, otherwise the
   first feasible override; meet subsequent feasible entries in key order.
   Determine that existence by the owning compact domain cardinality minus
   stored feasible keys, never by enumeration; charge its domain/key tests
   and arithmetic. For the unrestricted domain it is exactly `keyCount < N`.
   N=0 has no successful selection.
6. **Meet/join.** Enter left then right, check exact array/type identity and
   N, and meet C first for N>0; N=0 checks null templates/empty maps instead.
   Merge the two sorted key streams with one three-way
   key comparison per pair of current heads; retain each head until consumed.
   At equal keys consume left then right. For each union key, meet incoming
   effective children (override or C) recursively, then compare the staged
   child with result C and append only a deviation. Charge both effective
   template visits when reused at multiple keys. Check both stream heads in
   left/right order each iteration, including the final exhausted pair.
   For normalized D-range intersection, load each consumed interval's two
   endpoints once; compare starts for max, ends for min/advancement, then
   compare max-start with min-end (three comparisons per current pair).
   Emit a nonempty intersection; advance the smaller end, both on equality,
   left first. Byte-vector meets examine both full payloads. Meet other facts
   under their owner, retaining all required guarded obligations. For more
   than two incoming edges, left-fold in bound predecessor declaration order
   (Body Block order after reconstruction), true before false, never worklist
   arrival order.
   Any comparison with the previous fixed-point state uses step 4, and every
   required repeated analysis/loop visit is charged again.

No operation enumerates implicit default elements or builds an uncharged
HashSet, sorted-key array, prefix lookup result, Common cache or merged
dictionary. The reference recipe needs only ordered cursors, frames, staged
facts and the output tree. A different logical algorithm, including extra
materialized lists/caches, needs a separately identified recipe charging its
item/field/copy work and live records/payload. Physical caching/parallelism
cannot change this recipe's logical trace or canonical live overlaps.

## Reservations, lifetime and depth

Source state, seed/RHS staging, both incoming branch snapshots, old fixed-point
state, result-under-construction, paths and scratch coexist until their last
reference-recipe use. Keep the old destination until complete result
normalization/required comparison succeeds. Retire a superseded child only
after its replacement is established; retire branch inputs only after their
last join/comparison use. Moving a result into the enclosing analysis does
not release it from that root's live total. Each later clone is fully charged.

For every next action, compute its complete work charge, additional live
reservation and proposed active depth before doing it. All selected
object/root/explicit aggregate scopes must pass before any is advanced.
Ledger arithmetic itself does not recursively generate accounting actions;
reads/scans used to discover a charge do consume their ordinary actions.
Use nonnegative mathematical quantities, or the equivalent bounded algorithm:

```text
require valid counters and nonnegative charges/limits
if deltaWork > workLimit - usedWork: decline Work
if proposedDepth > depthLimit: decline Depth
if fixedBytes > liveLimit - liveBytes: decline State
remaining = liveLimit - liveBytes - fixedBytes
for each (count, positiveStride) reservation term:
    if count > remaining / positiveStride: decline State
    remaining -= count * positiveStride
check each separately selected entry/input/payload capacity likewise
only after all scopes pass, advance/reserve counters and perform the action
```

The subtractions occur only after establishing `used <= limit`; products
occur only after the division test. Apply the same remaining-capacity method
to work sums/products and all count/index/range arithmetic. A reservation
unrepresentable by the supported counter explicitly declines capacity; it
never wraps, saturates to a cheap value or becomes invalid source geometry.
Broken ledger invariants/invalid envelopes are tool/configuration failures.
Exactly-at-limit reservations succeed. Failed actions are not performed or
charged as completed; earlier work and peak live/depth remain recorded.

The reference recipe reserves a fixed record and its known byte payload in
full before constructing it; variable output lists reserve exactly one item
before each append. Results remain private until complete. A preliminary
sizing/sorting pass is not free; a different logical reservation/lifetime
strategy needs its own recipe. Physical spare capacity is not logical state.
No retirement of still-needed input may finance the output.

Each active Repeat rule/clone/meet/selection expansion owns one 48-byte frame
and adds one depth level to its caller's current logical depth. Recursive
child expansion adds another; an unresolved path ancestor keeps its frame
until its child returns. A path step and the node expansion it selects are
one level, not two. Flat key/byte iteration reuses the current frame; sibling
snapshots add live state, not fictitious active depth. Root entry is depth
`callerDepth + 1`; iterative traversal must model the same active frames.
Seed/type/subrecipe expansions inherit the caller's active depth and retain
their own specified frames/state. N contributes no depth.

Nested Repeat, paths, comparisons, dependency access and joins share the
root operation's remaining work/state; they do not get per-map or per-key
budgets. Structural decoding stays structural work, semantic transitions
stay semantic work, and actual required constant execution stays evaluator
work. Where these facts support source proof, their semantic work contributes
to that callable's existing proof total and their live bytes to that
analysis's existing state total, alongside ordinary scalar facts. Preserve
the inclusive portable minima **1,048,576 proof work units per callable**
and **16,777,216 live abstract proof-state bytes per analysis/request**.
Those are not interchange loader/audit defaults. Repeat introduces no new
depth minimum and does not borrow the evaluator's 256-call-frame limit.

## Exhaustion and publication

Use Work, then Depth, then State priority for simultaneous failures, retaining
the owning operation's other established failure categories. Exhaustion
declines the unfinished operation/root claim: no weakened D/backing facts,
successful partial array, validation certificate, retained successful cache
decision or emitted partial artifact. Discard private tentative results and
keep pre-operation published state unchanged; source seed/RHS effects already
required by the language are not rolled back. Compile-time analysis does not
execute those effects. Releasing abandoned analysis storage is bookkeeping,
not further analyzed user cleanup or a refund of work.

Unsupported shape/recipe, invalid input and deterministic exhaustion remain
distinct; cancellation, physical OOM and I/O failure are host failures.
No retry with a fresh nested budget, logical-extent expansion or silent
runtime fallback is permitted. Use the existing logical receipt contract:
exact inputs/context and scope, owning rule/recipe editions, selected
envelope, cumulative phase work, peak live entries/bytes/depth, outcome and
any admitted completed-decision reuse. This assigns no receipt API/codec.

## Acceptance, implementation gaps and remaining decision

These are required **future tests**, not evidence executed by this
documentation change. Use public compiler APIs and the active
[Repeat fixture](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayRepeatInitializationTests.cs);
huge fixtures remain semantic-only, without native allocation.

| Cases | Required evidence |
|---|---|
| Positive/compact | Hold seed shape, stored exceptions and selected domain/rule branches fixed while varying N>1 through H=`0x1_0000_0001` and the maximum layout-valid count. Repeat, copy, Common, unknown writes and joins have the same logical traversal/charges apart from actual checked input decoding. Separately test N=0 seed/staging, N=1 exact writes and N=2 fully overridden Common excluding C. Zero-payload/max-u64 counts retain metadata/key costs. |
| Negative facts/inputs | Vacant/non-Copy seed, out-of-range high key, broken path/type/generation, missing required typed/backing fact and unsupported child recipe cannot succeed under a large budget. Exhaustion cannot manufacture Wide coverage or lose an obligation. Invalid geometry remains distinct from capacity decline. |
| Inclusive boundaries | Hand-compute a small trace including header fields, payload copy/meet/equality, high-key lookup, path steps and override removal. For work/live/depth independently, exact required limit passes and one less declines at the same next action; keep other limits ample. Test the existing portable proof boundaries as well as explicitly constrained fixtures, not a new Repeat default. |
| Coexisting state/nonwrapping | Hold two snapshots, old destination, seed, staged child, output and active frames alive. Verify the sum/peak, last-use release, no work refund, and failure before append/publication. Exercise zero remaining capacity, exact products, one-over products and max-u64 count/key/stride terms without allocating their extents or overflowing `used + delta`. |
| Nested H | Both outer-H/inner-2 and outer-2/inner-H trees, directly nested Repeat and a seed with sparse child overrides: capture a copy, mutate one high nested path, then join different templates and unknown ancestor writes. Charge every visited prefix/key/child, retain siblings and copied facts, and grow depth with nesting rather than H or flattened products. |
| Determinism/failure | Permute physical insertion order/cache/parallelism for identical logical inputs: receipts and the first exhausted action match. Exhaust midway through a payload, second branch clone, child normalization and join; no success or reusable partial result escapes. Distinguish cancellation/OOM from deterministic exhaustion. Reconstruction/remapping charges its actual structural work and retains the compact tree. |

Current [support inventory](../implementation/conformance-gaps.md#cg-004-native-declaration-and-body-integration)
records implemented bounded Repeat routes and still-open sparse resource
qualification. In
[`SemanticVerifier`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs),
`RawUnionArrayElementState` stores C plus an ascending sparse override vector,
with lower-bound lookup and ordered merge/equality traversal. A growing subset
of frame, key/path, payload and state-transition work is charged, and
multi-unit work requests retain all completed units and fail at the first
unavailable unit. Frame entry preflights its combined work/depth/state claim
atomically. Operation-scoped input/destination state reservations remain on
`InitializationState.Clone`, `CopyTo` and snapshot routes when the input is
not already covered by an owner lease. The callable-root raw-Union map now
retains a persistent lease between operations; completed output transfers
into it, and snapshot scopes borrow it without double-counting or disposing
the root lease. Other output maps now grow incrementally.
Raw-Union guarantee-union template and override bytes charge seven work units
per produced byte for two reads, the bitwise combination, and output
generation. Its sparse-key merge performs both ordered head checks, reads
each key once, and charges the comparison. `ClearRawUnionArrayFacts`,
`CopyRawUnionArrayFacts`, `SetRawUnionArrayFacts`, exact element writes to
existing roots and new roots, including inherited template facts,
existing/new-root template writes, and existing-root byte-range writes build
fresh ordered root maps with per-item output reservations. The main
`MergeRawUnionArrayFacts` and equal-specificity template-state `Merge` outputs
also construct roots and child overrides incrementally. These remain partial
and do not yet conform to the full recipe. Unknown-index and byte-range writes
now build ordered output maps with per-item reservations for existing exact
roots, matched ancestor-prefix roots and newly introduced roots; completed
output reservations transfer into tracked snapshot state. The old
`CopyRawUnionArrayStateMap` write helper is removed. No-match ancestor-prefix
queries now run a charged existence scan before the single ordered output
pass and use the ordinary missing-root route, avoiding a discarded candidate
map. The scan charges path descriptors/segments, first-hit/final-miss, and
result-mode/cursor-dispatch work; the matched output pass repeats prefix tests.
This does not establish exact full-recipe charges or receipts. Outcome clones
retain leases in branch/loop, fixed-point,
failure/exception, statement-input and temporary proof scopes, with map
replacement adjusting the owning lease. If/loop paths explicitly release
condition/outcome and unreachable-branch snapshots, and converged entry/head
snapshots; iterative loop condition-only clones and post-test inputs now retire
at their last use. Fixed-point iterations now also retire temporary
continue/break, body/iterator, post-test condition, back-edge and native-merge
snapshots at their last use, reset stored Breaks/Continues edge owners after
transfer, and retain entry/head through convergence.
Failure-edge state is released on reset/disposal. Other
raw-Union prefix filters across copy, clear, snapshot, set and lookup routes
use the charged path predicate; selected template-state clones retain their
scratch lease through consumption, including CopyOrigin source selection.
Rebase prefix validation is charged in those routes as well.
Other call-staging, failure/proof and intermediate loop snapshots still need
last-use review. Exact last-use retirement, source-proof, complete action
expansion, receipts and public boundary routes remain open. Remaining
whole-map sizing/input reservations and other transition costs have not been
reconciled with the candidate's exact
first-exhausted-action trace; do not claim exact receipt conformance. A public
fixed-extent source case currently compiles with 150 sparse replacements and
observes work-limit exhaustion with 151; this is not a one-unit ledger
boundary. Preserve
failed-root publication while completing those routes; physical container
iteration order is not the accounting order.

`MaximumAnalysisVisits = 100_000` is current implementation support, **not**
a new accepted proof minimum or this recipe's envelope. It is not changed
here. Mapping/replacing that visit guard, supplying the accepted proof
envelope where applicable, exact child-fact accounting and exercising
public-API boundary receipts are implementation dependencies. Keep existing
structural guards explicitly scoped; do not simply rename visits as full
work units or raise the constant and claim conformance.

**Remaining decision:** approve, amend or reject this exact ordered recipe,
including the proposed logical record sizes and their conservative
independent-snapshot charging. Until reviewed, it is provisional only under
the user's task instruction. There is no separate Repeat numeric limit/API
to choose. Adopting it would not approve the parent Repeat proposal, migrate
private persistence, establish native execution support or stabilize a schema.
