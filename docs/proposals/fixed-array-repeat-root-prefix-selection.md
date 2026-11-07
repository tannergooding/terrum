# Fixed-array Repeat root-prefix selection

**Status: unapproved.** Proposed completion of the
[Repeat resource recipe](fixed-array-repeat-resource-accounting.md), not
approval of that recipe, Repeat, compiler changes or a schema.

## Missing decision and scope

The base recipe fixes root/path order, record sizes, ordinary lookup and
write transfers, incremental reservations and first-exhausted-action reporting.
It does not select when an ancestor-prefix rewrite determines whether any
stored descendant root matches. A charged search before output and a search
interleaved with speculative output have different work and live-state traces;
charging both output passes does not make them interchangeable under `/1`.

In the current working tree,
[`InitializationState`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
performs a charged prefix-existence scan before constructing output. A hit
builds one ordered result map; a miss takes the existing missing-root route,
which may normalize to no retained root. The scan charges root context,
descriptor and segment comparisons, first-hit/final-miss selection, and the
result-mode write plus cursor-reset/dispatch work before output. The matched
output pass repeats prefix tests while constructing one ordered result map.
The physical key is `(Variable, ArrayPrefix)` inside one
`InitializationState`; it has no per-key generation field. The logical
generation/revision is owned by that state snapshot: lookups never join keys
across states, and clear/copy/merge/replacement must preserve or retire the
owner generation before slot reuse. This implementation does not complete the
base recipe's remaining output-construction, first-exhausted-action receipt,
inclusive-boundary, last-use or failure-atomicity obligations. The base recipe
and this exact expansion remain unapproved; implementation does not constitute
approval or full conformance.

On adoption, add the following prefix-selection expansion between the base's
**Order/lookup** and **Writes/Common** rules. Preserve all its other rules and
logical shapes, and the authority of
[resource accounting §§2, 4–6](../specs/representation/resource-accounting.md).
A selected recipe edition must include this trace. It changes no
source/runtime/library/ABI rule, resource envelope, API, receipt codec,
Standard/private bytes or exact exchange schema.

## Viable choices

| Algorithm | Consequence |
|---|---|
| Charged existence scan, stopping at the first match, then one ordered output pass | Recommended. Needs only the existing frame and ordered input; misses scan every stored root, without a candidate output map. |
| Complete charged prefix scan, retaining only an existence bit, then the same output pass | Also viable with the same logical storage, but charges the remaining prefix tests even after existence is established. Incremental output needs no match count, so that extra work buys nothing here. |

These are different logical traces, not interchangeable optimizations.
A replayable physical cursor may implement the selected trace only if every
logical replay action below is charged. Neither choice permits a free
preflight, materialized key/match list or uncharged cached prefix decision.

## Recommended deterministic trace

1. **Dispatch.** Perform the base's checked target/path/index operations and
   exact-root lookup first. An exact attachment wins even when its transfer
   changes no fact; do not also select descendants. A concrete missing path
   retains its ordinary missing-root route. The added scan applies only after
   exact lookup misses for a non-exact ancestor-prefix query Q.
2. **Select without output.** Use one base 48-byte root-rewrite frame across
   selection and output, with its ordinary seven entry actions and one exit
   action. Its operation field holds the phase/result mode, its cursor the
   current root position; the other fields keep their input/RHS/result/path
   roles. Enter the input component and read its count/list reference in
   table order. Scan roots in bound-variable order, then the
   `StorageProjectionComparer` path order, prefix before extension. Each
   availability test costs one action, including exhaustion; visit each
   available root item and enter its attachment using the base expansion.
   Compare the variable/storage context first. Its generation/revision is
   carried by the enclosing `InitializationState`; do not match or merge
   roots across state owners. Do not follow the fact child merely to test its
   path.
3. **Prefix predicate.** For an eligible root, enter Q's path descriptor, then
   the candidate descriptor, reading each header once for this comparison.
   Compare their lengths; a longer Q fails immediately. Otherwise process
   steps outer-to-inner: test availability of Q then candidate, visit/read
   Q's kind/selector then candidate's kind/selector, and perform the existing
   checked kind, owner/type, bound/reference and selector tests in that order.
   Stop at the first mismatch. A successful prefix ends at Q's failed
   availability test, not the candidate's end. Unknown uses its separately
   charged compact feasible-domain predicate, never enumeration of N.
   Checked path/child recipes remain prerequisites; this adds no wildcard
   meaning or range-record shape. Re-entry/replayed steps incur their full
   charges, not one charge for a composite `IsPrefixOf` callback.
4. **Choose the branch.** Stop immediately at the first matching root;
   otherwise finish the final failed root-availability test. Write the
   `Matched` or `Missing` mode once, reset the cursor once, then read/test the
   mode for dispatch: respectively one field write, one field write, one
   field read and one comparison. No matching-root count, path vector,
   output header, child clone or whole-map sizing pass precedes this choice.
   For R roots, a miss visits R items and tests availability R+1 times; a
   first match at zero-based j visits/tests j+1 times, without an extra
   exhausted test.
5. **One ordered output.** Enter the input component again and construct one
   private result component with the base 16-byte header and count zero.
   Revisit roots in the same order, retaining only the current head until
   consumed; re-entry reads and list tests are charged again.
   In `Matched` mode, repeat the prefix predicate for every root, including
   the previously found match. Apply the ordinary possible-target transfer
   to matching roots and clone others. A matched-but-unchanged result never
   falls through to insertion. In `Missing` mode, merge the ordinary
   missing-root result at its canonical storage/default path before the
   first greater input head, or after the final failed availability test.
   Compare context then generation, stopping at the first difference; for
   equal roots, enter the target path then the head's path, test both step
   lists target-first, and visit/read each kind/selector pair target-first.
   Compare kinds then selectors, stopping at the first difference; when a
   list ends, order the prefix first, equality only if both end. Charge every
   field, list test/visit and scalar comparison; do not repeat prefix matching.
   At the insertion point retain the current head, reserve/construct the new
   attachment/path, then derive its child from the original snapshot under
   the base lookup/transfer rules, not merely type G. Normalization may
   require no retained new root; a miss does not itself require insertion.
   Unknown remains query-only: if the required retained path/child shape is
   unsupported, decline it.
6. **Construct and finish.** Reserve/visit/install each root, path step,
   override and child under the unchanged base shapes before writing it.
   Clone unchanged mutable facts fully; do not share their logical charge
   with the predecessor. Write child references only after complete children,
   and update each output-list count once per appended item. Perform required
   normalization/comparison before publication, including on no-change paths;
   there is no free equality/preflight shortcut. Installing the completed
   result reference is a charged field write; snapshot installation/retirement
   retains its ordinary transition charges. Publish at most once.

All field reads/writes, list visits, reference/edge checks, bounded
comparisons/calculations and child/payload actions retain the base charges;
current-head retention does not authorize caching other candidate headers or
prefix results. Predicate iteration adds no depth per root/step. Actual
recursive path/child expansion retains its base frames and depth.

Keep the original complete snapshot through normalization/comparison and
commit, and the captured path/post-RHS facts through their last base-recipe
use. Do not retire copied input roots early to finance output. The frame is
charged through both phases; no second frame/map is hidden in cursor storage.
Only after selection reserve the output header, then each item incrementally;
known child headers/payloads still reserve in full. Independent snapshots
count separately even if physically shared. Transfer completed output into
its enclosing tracked state; release the predecessor only at its last use.

Preflight every next action in nonwrapping arithmetic across all selected
scopes, with the base **Work, Depth, State** priority and separate selected
entry/input/payload capacities. Failed actions are not performed or charged
as completed; earlier work is not refunded.
On exhaustion, unsupported shape, invalid input or host failure, abandon only
private output and retain pre-operation published facts. Keep those categories
distinct; neither speculative success nor a fresh budget is permitted.
Required seed/RHS effects retain their existing non-rollback rules.

## Preserved transfers and acceptance

The [Repeat transfer owner](standard-native-fixed-array-repeat-initialization.md#repeated-facts-nested-paths-and-joins)
still decides exact versus possible writes over the **complete** destination
path. A known leaf index or N=1 does not make an unknown ancestor exact.
Whole-element replacement and byte-range W-transfer remain distinct; possible
targets meet old facts with their transfer result, retaining unaffected bytes,
typed validity and backing obligations. N=0 has no successful element
selection, with its ordinary pre-RHS bounds failure, and still analyzes/stages
its Repeat seed once. No scan expands implicit elements or changes
source/geometry/unsupported/capacity errors.

Required future tests, not evidence executed by this documentation change:

| Cases | Required evidence |
|---|---|
| Positive selection | Exact root despite matching descendants; first/middle/last prefix hit; matched but unchanged; empty map and no-match insertion before/between/after roots. Unrelated roots and inherited facts survive, and only one output map is constructed. |
| Transfers/compactness | Known/unknown leaf indices under exact/unknown ancestors, whole versus byte-range writes, N=0/1/2, H=`0x1_0000_0001` and maximum layout-valid N with fixed stored shapes. Check singleton strengthening only for a singleton complete path, untouched bytes, sparse normalization and no retained Unknown key. |
| Negative contracts | Invalid query owner, a stale/reused variable slot, matched layout/type mismatch, invalid original index/range, unsupported path/child/domain recipe and missing typed/backing facts retain their ordinary failures, not a miss or invented coverage. Roots from unrelated `InitializationState` owners are never candidates for this scan. |
| Inclusive trace boundaries | Hand-expand a small hit and miss, including headers/fields, R+1 versus j+1 tests, phase/cursor writes, replay, insertion ordering/count writes and child work. For work/live/depth independently, exact required capacity passes and one less declines at the same next action; test zero remaining capacity, nonrepresentable reservations and simultaneous-failure priority. |
| Determinism/publication | Permute physical insertion, caches and cursor implementation without changing logical inputs: charges, peaks and first failed action agree. Exhaust during selection, output-header/root/child reservation, payload copying, normalization and final comparison; no candidate map, weakened old state, reusable partial decision or partial artifact escapes. Output remains charged after publication. |

**Remaining decision:** adopt, amend or reject this selection trace as an
explicit completion of the base recipe. Receipted rule/recipe editions must
identify that selection; existing partial implementations/receipts do not
establish it. No compiler/test change or normative adoption is authorized here.
