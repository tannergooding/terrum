# Artifact-local fixed-array metadata in private revision 100

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract authorization.** This companion recommends a narrow amendment
to [private fixed-array persistence](native-fixed-array-private-persistence.md).
It does not formally approve revision 100, change implementation or establish
compiler conformance. [Proposal index](README.md).

## Decision and alternatives

Recommend **Direction 1: metadata-only recipes are artifact-local, not exported
provider contracts**. Artifact-local describes their contract role: it does not
prevent their preservation, structural remapping and re-emission inside full
private modules, sets or graphs.

The base proposal permits descriptive nested arrays, pointer-to-array and
function-pointer recipes and nominal field layouts, but rejects every exported
runtime signature/carrier containing array metadata, including through nominal fields.
The current [public reference projection](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
is function-contract-rooted. Neither that API nor revision 100 defines a
type-only export/import identity. Requiring these descriptions to survive
bodyless projection as original provider expectations therefore lacks a legal
contract root; retaining a table row cannot supply one.

| Direction | Integration and tradeoff |
|---|---|
| **1. Artifact-local metadata — recommended** | Scope closure by the retained artifact's role. Preserve full implementation closure, prune unrooted bodyless metadata and compare only genuinely retained provider contracts. No source, wire or ABI addition. |
| 2. Type-only export/import | Could expose descriptions without making them executable; the flat runtime boundary does not inherently forbid it. It needs source lookup/visibility, structural versus nominal export identity, an expectation carrier with declaration kind and exact Module/export-key ownership, nominal bindings, complete projection/embedded-contract closure and provider-origin/layout/Copy/replacement checks. None is selected by the array table. This is a larger, separately selectable contract, not a reason to admit runtime arrays or infer exports from names/row IDs. |

## Exact candidate behavior

1. **Full artifacts retain their actual closure.** Start from every retained
   declaration/field/layout, pointer/function-pointer recipe and admitted body
   occurrence, not just public signatures. Follow complete array elements and
   nominal fields with the base's family-specific rules. Preserve referenced
   metadata-only recipes even at zero count/stride/extent. Canonical omission
   of genuinely unreferenced array rows remains allowed; this is not a promise
   to retain every unused row byte-for-byte. Full module, set, graph-library,
   linked graph and re-emission paths use this rule.

2. **Bodyless references retain only admitted contract closure.** For
   `CreateReference`, set/graph projections and their re-emission, roots are the
   existing independently admitted exported function/native contracts and
   their required declarations, layouts and dependencies. An implementation
   row, a public type name or metadata presence alone is not a root. Prune
   metadata-only recipes unless an independently admitted non-array contract
   genuinely requires them; retain that contract's entire required closure.
   Under the present exclusions there is no legal array-derived public
   signature/carrier to provide such a root. Reject an attempted excluded
   export, rather than erasing its type facts or silently dropping the export
   to make projection succeed. No type-only source/API/wire carrier is added.

3. **Embedded Contracts follow the same distinction.** Contract-only
   projections retain the required closure of their original legal
   expectations, not the provider's unrelated descriptions. A full persisted
   module or snapshot retaining implementation records still preserves those
   records' closure under rule 1; putting them beside Contracts does not turn
   them into exported expectations. Snapshot comparison roots remain the
   retained contracts and relied-on dependencies. A bodyless reference is not
   a substitute for the full provider artifact needed for source-free
   re-emission; omitted implementation data cannot be reconstructed from it.

4. **Replacement does not compare unrelated private recipes.** A replacement
   may change or remove metadata-only implementation descriptions, including
   their element identities, dimensions and geometry, if its retained records
   remain valid and every existing exported/relied-on contract is satisfied.
   Retain original selected identities and expectations for all independently
   admitted non-array contracts: nominal identity, layout/alignment/Copy,
   access, effects, native/output/backing and code-lifetime obligations do not
   weaken. This includes a consumer's flat local array whose non-array element
   type E depends on an admitted provider type. If retained code, a constant or a
   layout observation actually relies on a provider fact, preserve its
   existing admitted dependency; if 100 cannot represent it, reject the use.
   Calling a recipe private never discharges that reliance. Do not overwrite
   old expectations with the replacement's tuples. Rebuild or reject affected
   derived code under existing rules; this adds no live replacement mechanism.

5. **All retained data still receives complete checking and remapping.**
   Pruning does not excuse malformed selected input or repair an excluded
   runtime occurrence. Keep the base's table/framing/reference, geometry,
   normalization/uniqueness, cycle, resource and enclosing-use checks.
   Resolve existing admitted nominal bindings before interning; reject
   ambiguous or unrepresentable required origins, not compare spelling.
   Rebuild family-specific maps for arrays, nominal fields, pointers,
   function-pointer contracts, body/layout types and symbolic functions;
   preserve ordered children, local-slot meaning, source spans, geometry and
   provenance. Keep deterministic first-use/element-before-parent writing,
   enclosing lengths/checksums and cache invalidation for changed inputs.
   Row/order permutation is not cross-provider identity or byte substitution.

## Intended amendment and preserved owners

When provisionally selected with the base, these rules supersede only:

* The root interpretation of **Identity, layout authority and canonical
  writing** and **Metadata-only nested and zero-payload types**: full
  implementation records and public contract projections have different
  retained roots, not different array identity/geometry rules.
* **Dependencies, structural remapping and replacement**, steps 1 and 5 and
  its final all-path applicability paragraph: full internal closure remains
  mandatory; public comparison covers retained legal expectations and
  relied-on facts, not every descriptive recipe in a provider.
* The metadata-only expectations in **Identity and closure** and
  **Remap/replacement**, plus the **Full metadata declaration closure** and
  **Provider-origin replacement** gap rows: use the acceptance cases below.
  Do not require a positive type-only publication/replacement fixture in 100.

All byte shapes, table placement, tags 43–46, exact selection and flat runtime
exclusions remain unchanged. This amends the unapproved exact candidate
`terrum/private-semantic-100-structural-fixed-arrays-1`, read together with this
companion; it adds no alternate decoder for i32 100 or outer version 3.
Agreement includes these contract rules, not merely matching revision bytes.
Regenerate affected projections from full retained input; no historical
reader, header-only conversion or compatibility alias is selected.

The accepted [persistence taxonomy](../specs/representation/persistence.md),
[format versioning](../specs/representation/format-versioning.md),
[binding/replacement](../specs/representation/binding-and-replacement.md) and
[consumer checking](../specs/representation/reader-trust.md) remain controlling
and are not edited. In particular, this removes no retained public meaning
and grants no resilient layout. Private revision 100 remains 99 plus its
array delta, not a composition of 93–98. The separate unapproved
[Standard A2 contract](standard-trrm-rootless-copy-type-evidence.md), source
array direction, Core APIs, target ABI and broader runtime/place admission
are unchanged.

## Acceptance cases

These are required success/rejection gates, **not test-run or support claims**.
Use public compiler APIs and independent fixtures; synthetic in-memory
reference-table merging demonstrates mapping, not type-only publication.

| Case | Required disposition |
|---|---|
| Full descriptive closure | Retain a nominal field/layout containing nested arrays and pointer-chain/function-pointer recipes rooted in retained full records, including zero-extent dimensions. Ordinary module, set and graph decode/link/re-emission preserve their complete normalized closure without activating runtime storage. |
| Public projection | A scalar-export provider with those unrelated descriptions yields ordinary, set and graph bodyless references with no unrooted array recipes or descriptive declarations. Contract-only embedded projections agree. Independently admitted non-array contracts retain all required type/layout/native/dependency facts. |
| Excluded export | Direct arrays, nested arrays, array-containing nominal carriers and transitive pointer/function-pointer contracts reject when exposed as runtime contracts, including N=0, uncalled functions and unreachable code. Projection cannot erase the offending export or substitute unit/scalar/first-element types. |
| Invalid input and bounds | Missing/wrong-family references, duplicate/cyclic recipes, inconsistent field/array geometry, invalid required origins and exceeded inherited budgets reject under the existing checks even when a later projection would prune the data. Zero extent does not waive element/nominal closure validation. |
| Exact remapping | Independently permuted forward/reordered struct/array/pointer/function-pointer tables survive full ordinary/set/graph merging and canonical re-emission with every retained reference/layout mapped correctly. Different E, N or qualifications do not merge merely because byte layouts match. |
| Compatible replacement | With the same legal exports and satisfied original expectations, change valid unrelated provider descriptions, dimensions/geometry or remove unused recipes; ordinary/set/graph replacement succeeds. Consumer-owned arrays and retained non-array dependencies keep their own facts. |
| Incompatible replacement | Missing/ambiguous/wrong provider or export, changed retained non-array nominal layout/R/Copy, access, unsafe/native/output/backing contracts or any actually relied-on fact still rejects. Include an imported non-array E used in a flat consumer local array; artifact-local array identity cannot hide an incompatible E. |
| Source-free boundary | Re-emit from full persisted providers/consumers and retain required bodies/descriptions without source rereading. A pruned bodyless reference alone cannot restore them. Scalar execution beside descriptive metadata is not nested-array execution evidence; all base runtime exclusions remain negative gates. |

## Dependencies and formal approval

Implementation depends on the complete selected 99-plus-array codec,
role-correct projections/snapshots, existing non-array provider comparison and
the base's complete retained-data remappers/checks. Missing general nominal
origin or other 93–98 facts retain their separate rejection/dependency gates.
No type-only contract is a prerequisite for this direction.

**Formal approval remains outstanding** for this artifact-local boundary and
its scoped supersession, as well as revision 100's broader pending decisions.
A future type-only contract must separately select all source/metadata
identity, closure and consumer/replacement obligations before exporting these
recipes. Neither this proposal nor focused implementation evidence approves
that future direction, Standard A2 or the wider fixed-array milestone.
