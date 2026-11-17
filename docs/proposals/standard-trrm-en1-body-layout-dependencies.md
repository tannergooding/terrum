# Standard TRRM EN1 Body layout-query dependencies

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** Implementation is in progress; this proposal
does not claim build, test, or runtime qualification. The existing class
[selection](standard-trrm-en1-class-field-query-selection.md) and
[layout](standard-trrm-en1-class-field-layout.md) proposals retain their status.

## Decision and scope

**Use an ordered, exact one-to-one binding between retained Body layout-query
instructions and `0045` rows not owned by ConstantExecution or
TargetValueBinding.** Keep both existing row layouts and `004A`'s exclusive
field ranges. For persisted completed values, use the actual
ConstantMap → ConstantSource kind 3 → TargetValueBinding relationship when
that complete route is supported; otherwise retain the symbolic Body or
refuse persistence of the evaluated form. Do not invent a required
ConstantExecution request merely to provide an owner.

This rule is part of each of the four exact
[class-instance-layout successor identities](standard-trrm-en1-class-field-layout.md#exact-successor-identities),
not base EN1 or the standalone LayoutQueries profile. It completes their
proposed dependency reuse without changing a row, opcode, Body prefix,
Feature revision, target assignment or identity literal. Every root,
supplied provider and retained origin must select the identical complete
successor, including this rule. Equal `(1,0)` bytes, Feature `13/1`,
TargetProfile `(1,1)` or an earlier incomplete draft bearing the same label
do not establish [exact schema agreement](../specs/representation/format-versioning.md).
No fallback decoder or automatic composition is selected.

| Alternative | Disposition |
|---|---|
| **Ordered residual rows with exact signatures and multiplicity** | **Recommended.** Expresses symbolic Body provenance using existing fields. Costs an artifact-wide structural query inventory and requires rebuilding the relation after relocation. Does not encode arbitrary optimized-Body dependencies. |
| Explicit Body owner/range record, for example `u32 bodyRow, u32 layoutDependencyFirst, u32 layoutDependencyCount` | A coherent larger design: the unique MethodDef owner supplies the Module, with no second owner word. It avoids global implicit association and could retain dependencies on a whole optimized Body. It needs a required section/allocation and rules distinguishing live queries from retained folded observations; the three fields alone do not define those rules. Not selected for this slice. |
| ConstantExecution or TargetValueBinding as owner of every live query | Not selected. An ordinary Body is not a required execution request or a completed frozen value. Existing explicit ranges cannot attach unrelated live instructions to those owners. |

This changes only ordinary-Body linkage in the composed candidate. It
preserves per-execution and completed-value ownership, source authorization,
class applicability, physical layout and all independent Body/feature gates.

## Required relation

1. Validate admitted `0042` ConstantExecution and `0049` TargetValueBinding
   owner records and their checked `0045` first/count ranges. Nonempty ranges
   are one-based; empty ranges are `(0,0)`. Ranges are exclusive within and
   **across** these owner families. Unsupported owner records refuse affected
   consumption; they are not ignored to manufacture Body-owned leftovers.
   Each owner's existing profile, value/request and using-context checks
   remain mandatory.
2. Enumerate locally implemented MethodDefs in increasing Module row, then
   MethodDef row, following each unique `bodyRow`. Within stream Bodies,
   enumerate increasing Block index, then instruction ordinal. Every decoded
   `DE` kind 1..4 contributes one occurrence, including discarded results and
   instructions in blocks not dynamically reached. TypeOf kind 5, unused map
   entries and bodyless imported expectations contribute none. Other
   admitted implementation forms contribute no occurrence unless their
   selected schema assigns a layout query. Unknown required forms cannot be
   treated as empty.
3. Enumerate the `0045` rows outside the explicit ranges in increasing row
   order. There must be exactly as many as the Body occurrences. The ith
   residual row belongs to the ith occurrence, with the owning MethodDef's
   **using Module**, not the queried field's provider Module, supplying context.
   This order is a required encoding relationship, not an optional
   canonicality check. Do not search for a different matching row.
4. Require the paired signatures to agree: query kind, complete normalized
   root type, selected target identity and complete ordered FieldReferences.
   Resolve TypeMap/FieldMap operands in that Body's context; expand a direct
   eligible field token to its exact nongeneric declaring root and one step,
   or expand the complete `004B` FieldPath. Both offset kinds retain the full
   root/path even though kind 4 returns only the terminal displacement.
   Size/alignment have empty paths. The selected target for this successor's
   class-layout route is concrete `(1,1)` with the layout proposal's Module
   requirements; a query has no separate target operand that can override it.
5. Compare decoded identities, not TypeMap indexes, raw TypeSpec encodings,
   names, field ordinals, computed offsets or result equality. Equivalent
   complete-type spellings and direct/one-step FieldPath spellings remain
   ordinary inputs. Preserve nominal identity, qualifications, substitutions
   and **original expected** Module/key/owner/field contracts: two different
   Import expectations do not become interchangeable merely because they
   bind to one actual provider. Validate every immediate owner and inline
   transition before use.

Thus every `0045` row has exactly one owner: an explicit request, an explicit
completed value, or one Body occurrence. Every offset observation in all
three families owns its own nonempty ordered `004A` range; ranges are
disjoint and cover the retained `004A` table. Sharing identical signatures
does not share rows or erase multiplicity.

The Body entries are static potential-observation dependencies, not an
execution trace. A loop's one encoded query has one Body entry; a separately
persisted ConstantExecution still retains each actually reached observation
under its own request. It cannot borrow the Body entry or another execution's
range. No reachability proof or execution is needed to establish Body linkage.
Obtaining the residual relation requires the complete structural query
inventory; inability or resource exhaustion refuses that claim, not partial
success. This added inventory requirement is specific to these successors.

## Folding and existing value owners

An in-memory fold or native constant must retain the paired occurrence,
original Body/artifact snapshot, exact schema, provider/target context and
complete layout-coupling basis. Its persisted interchange may remain the
original symbolic `DE` plus its paired rows; native lowering need not execute
that instruction at runtime.

A supported persisted **value** fold instead uses a real ConstantMap entry
whose ConstantSource kind 3 selects `0049`. That binding owns cloned `0045`
observations and exclusive `004A` ranges, with its frozen root supplying
the exact typed value and its profile agreeing with every dependency.
Remove the replaced live occurrence's residual entry; do not leave it orphaned
or share its rows with the value binding. Preserve every contributing query
occurrence when several queries produce one value. Equal scalar bits do not
justify merging away target, original-view or occurrence obligations.
Validate shared value sources in every using Module context; clone them when
their binding contexts differ.

This proposal does not activate an unsupported `0049` codec or general
ConstantExecution. A fold into control flow, a removed value use, or another
optimized Body with no surviving genuine value-owner edge cannot hide its
coupling in an unused ConstantMap entry or unreferenced value binding.
Retain/reconstruct the symbolic Body or refuse that serialization. Arbitrary
optimized-Body provenance needs a separate contract, such as the explicit-owner
alternative; no such permission is inferred from optional folding.

## Consumer, transformation and replacement obligations

* Before affected use, validate selected schema/support, Body framing and
  unique MethodDef ownership, the complete ownership partition, exact
  signature/count correspondence, reserved zeros, concrete profiles, checked
  extents/ranges and FieldReference namespaces under the existing global row
  and independent Body-local width limits. Require Feature `13/1` in
  each using Module with retained query/dependency content; preserve the
  existing independent feature prerequisites for values and requests.
  Complete target/provider binding checks still precede numeric use.
  Consumers rely on producer-published operation-local unsafe/access facts;
  matching dependencies neither proves those facts nor creates ambient
  authorization. No semantic re-proof of authored Bodies is required.
* Re-encoding decodes the **source** relation before moving anything.
  Rebuild Module partitions and MethodDef/Body ownership, Imports and original
  expectations, complete TypeSpecs, layouts/profiles, FieldMap/FieldPath
  roots/steps and Body indexes/counts/extents. Then rebuild destination
  request/value ranges, residual occurrence order and exclusive `004A`
  ranges together. Reordering MethodDefs or Blocks changes occurrence order;
  relocating Body rows alone remaps `bodyRow` without changing that order
  when MethodDef ownership is unchanged. Preserve or regenerate affected
  source/reference slots. Byte substitution and replay of unchanged row
  numbers are not remapping.
* Preserve distinct expected views, aliases and stronger relied-on actual
  facts through Import-to-direct packing, cloning, shared rewriting and
  lowering. Duplicate/inlined queries receive corresponding distinct Body
  rows; deletion cannot discard a surviving folded-value/decision obligation.
  Never replace the current all-rows check with unchecked acceptance of
  unclaimed rows.
* Retain complete selected class/inline representation closure, not just
  queried fields. Changed membership/order/type, nested representation,
  provider binding, target or rule context invalidates affected cached
  observations and code before reuse, even if offsets happen to stay equal.
  Compatible physical relocation rebuilds references without inventing a
  representation change. Rebind/regenerate or refuse stale use; rebinding
  metadata alone does not patch embedded constants. Detached native code
  lacks the required provenance.

## Integration owners and inspected evidence

On adoption, amend the
[`DE` retention rule](../specs/il-instruction-set.md#assigned-long-families)
with the static Body relation above; extend
[`0045/004A` ownership/context](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and the [registered-query](../specs/representation/logical-records.md#registered-query-contracts-and-representation)
and [evaluation ownership](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
text to distinguish Body, execution and value owners. Specifically replace
the unqualified statement that layout dependencies belong to the
ConstantExecution occurrence, not its actual-execution rule. Document the
ordered relation with [canonicality/remapping](../specs/representation/encoding-canonicality.md)
and [schema-owned consumer checks](../specs/representation/reader-trust.md).
Keep [FieldDef binding/replacement](../specs/representation/binding-and-replacement.md#fielddef-importexport-and-layout-path-obligations)
and the class-layout recipe unchanged. No registry allocation is needed.

Static inspection only; these are gaps/evidence, not acceptance results:

| Source | Evidence |
|---|---|
| [`TrRmConstantExecutionTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantExecutionTable.cs) | Marks request-owned layout ranges. The exact class-layout successor now permits residual rows only long enough for the ordered Body relation validator to account for them; other profiles retain the all-rows check. |
| [`TrRmStructuralMetadataValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) | Constructs the dependency readers and ConstantExecution reader separately, then invokes the successor-aware Body relation validator. This path remains unqualified. |
| [`0045` reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmTargetLayoutDependencyTable.cs), [`004A` reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmTargetLayoutDependencyFieldTable.cs) | Validate row shape, complete subjects/profiles, exclusive field ranges and reference kinds; supply no MethodDef/Body owner. |
| [EN1 writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.C1EnumClosureWriter.cs) | Emits `0045/004A` rows beside the selected query Body. The exact successor validator now checks ordered linkage, and the focused regression remains unverified under the active hold. |
| [Provider field validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs) | Traverses retained field paths/dependencies; Body pairing must supply the actual using Module instead of inferring it from a first field reference. |

## Required acceptance, not current results

| Case | Required outcome |
|---|---|
| Ordinary Body | Source-free emit/load/re-emit under each exact successor binds a query with zero ConstantExecution/TargetValueBinding rows; base EN1 or a mixed/unused wrong-schema provider does not select this relation. |
| Identity and multiplicity | Repeated equal queries in one or several Modules retain separate rows. Different aliases/expectations cannot be exchanged on equal actual layout. Missing/extra rows, wrong kind/root/profile/path, or swapping unequal residual signatures reject. |
| Encoding variants | Consistently reordered/duplicate maps, alternate complete-type spellings and eligible one-step FieldPaths preserve meaning. Discarded/unreached encoded layout queries count; TypeOf and unused maps do not. Loops do not invent static entries per iteration. |
| Owner partition | Where requests/values are independently supported, mixed Body/request/value dependencies have disjoint ranges and cloned repeated observations. Cross-family overlap, orphan fields and an attempt to use one row for live and executed queries reject. Unsupported owner families refuse, not fall through. |
| Folding | Native folds retain validated snapshot/plan provenance. A supported kind-3 ConstantSource fold retains all contributing observations under `0049`; otherwise symbolic retention or explicit refusal is required. Bare scalar replacement, dead-value owner tricks and detached cached code do not pass. |
| Remapping and bounds | Method/Body/Block permutations, Import-to-direct packing and non-first/provider-owned Imports rebuild the complete relation in the correct using context. Truncation, wrong reference kinds, checked range overflow and exhausted inventory processing refuse without partial success. |
| Replacement | Compatible relocation preserves every original view; changed unqueried field/layout/provider/rule facts invalidate affected values/code even with equal results. No query enables class value access, construction or another profile composition. |

No in-scope linkage choice is left to implementation. Formal approval remains
outstanding; general optimized-Body persistence and unsupported request/value
execution remain separate gates.
