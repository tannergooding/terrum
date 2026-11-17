# Standard TRRM LayoutQueries reference composition

**Status: UNAPPROVED proposal; provisional direction only under the user's
authorized missing-contract workflow.** The user authorized preparing the
smallest missing decision, then treating it as provisional approval/direction.
This is not formal adoption, implementation evidence or milestone acceptance.
This change is documentation only. [Proposal index](README.md).

## Decision and preserved owners

Recommend one explicit `TrRmExecutableCompiler.CreateStandardReferenceSet`
operation over already selected LayoutQueries `CompilationReference` objects.
It reconciles actual definitions and combines explicit selections while
retaining **every original expected view**. It does not concatenate type
tables or reselect providers.

This fills only the composition gap in the
[source/provider-boundary proposal](standard-trrm-layout-queries-source-provider-boundary.md#recommended-consumer-input-and-api).
That proposal still owns publication, single-Module key selection, provider
admission and query-only projection. On adoption, its “Combining references”
paragraph should refer here; no accepted text is superseded.

The [specification index](../specs/README.md) leads to the preserved owners:
[binding and replacement](../specs/representation/binding-and-replacement.md)
owns nominal identity, aliases, each original expectation and FieldDef owners;
[manifests](../specs/representation/semantic-manifests.md) owns per-Module
requirements and Feature 13/1;
[reader trust](../specs/representation/reader-trust.md) owns mandatory
safe-consumption checks and producer truth; and
[format versioning](../specs/representation/format-versioning.md) owns exact
whole-artifact schema agreement. Existing
[source lookup](../specs/language/names-and-binding.md) and
[query authorization](../specs/language/registered-queries.md) do not change.

Scope remains exactly `trrm/1-draft-layout-queries-1`, the existing safe,
closed nongeneric scalar/inline-struct query projection, Windows x64 and
fail-fast. There is no new source syntax, schema, wire ID, generic Body,
Copy/value capability, field load/store/address permission, Core designation,
CLI transport, AOT/other-target support or live replacement protocol.

## Inspected gap and alternatives

[`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
is internally constructed, and ordinary
[`Compilation`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs)
accepts one such reference. The
[`Standard projection`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs)
retains selected type/field origins and hidden layout closure. In contrast,
[`ModuleCompiler.CreateReferenceSetFromReferences`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs)
is internal private/native-set machinery: it imposes 2..16 producers, rejects
duplicate structure names and remaps ordinary structures by name. Making it
public unchanged would not reconcile a hidden Leaf view with an explicitly
selected view of that same actual Leaf.

| Candidate | Assessment |
|---|---|
| **Explicit Standard operation — recommended** | One method, no new selector/options record; eligibility and failures stay with the exact-schema Standard factories. Share a checked identity/remapping engine where useful, not private-set policy. |
| Public `ModuleCompiler.CreateReferenceSet(IReadOnlyList<CompilationReference>)` overload | Credible only with the same checked Standard associations and a schema-specific composition path. It would leave the existing byte-array/private/native operations unchanged, but give one name materially different count, identity and failure contracts. Merely exposing the internal helper is not this alternative. |

The earlier implementation snapshot demonstrated a provider-owned TypeDef
Import chain for a direct Pair-field query but not composition with a
separately selected Leaf field. A provisional implementation now covers that
bounded case: `StandardLayoutQueryBodyUsesImportedProviderLayout` composes
Pair and Leaf selections, emits and source-free loads the
`offsetof(Pair, Inner.Value)` query, and executes it through Windows x64
NativeJit. The adjacent
`StandardReferenceSetKeepsDistinctModulesAndRejectsConflictingSnapshots`
regression covers explicit alias order, equal-name distinct-module ambiguity,
same-Module snapshot conflicts in both input orders, and unchanged parent
bytes with a changed dependency snapshot. These cases are implementation
evidence only; this proposal remains unapproved and its full acceptance matrix
is not complete.

## Proposed host operation

This is an **UNAPPROVED C# API candidate** on
`Terrum.CodeAnalysis.Modules.TrRmExecutableCompiler`:

```csharp
public static CompilationReference CreateStandardReferenceSet(
    IReadOnlyList<CompilationReference> references);
```

* Require at least two entries. Repeated
  references and selections from the same Module are allowed. There is no
  16-provider or arbitrary graph-depth limit; existing checked capacities
  and resource failures still apply.
* Accept only complete query-only references produced by the exact
  LayoutQueries type-key or type+field-key `CreateStandardReference` overload,
  or by this operation. Every retained artifact must have that exact selector,
  compared ordinally without trimming, case folding or normalization. Other
  schemas/private/native references do not become eligible through matching
  shapes, empty function tables, `RuntimePolicy` or common version bytes.
  Preflight every retained selector before any artifact reader, including
  disconnected inputs.
  Do not merge unrelated host/Core registrations or source grants.
* Capture the caller's list before processing; it must not be concurrently
  mutated during capture. Do not modify inputs. Reuse their compiler-owned
  immutable bytes/associations; neither byte-object identity nor a new copy is
  required. Later caller list/backing mutation cannot change the result.
  No new artifact, key, provider resolver or default is supplied here.
* Publish one new immutable reference only after complete reconciliation and
  verification. A failed standalone reference cannot be repaired by this
  operation; the single-reference factories must already have admitted their
  supplied provider closures.

### Snapshot reconciliation

1. Retain the complete admitted artifact/Module directories of all inputs,
   including co-located, disconnected and unqueried supplied Modules and
   their requirements/edges. Union disjoint directories. For shared edges,
   references must agree on the same admitted provider closure; swapping an
   artifact's root/provider role or using another selected Module in that
   artifact is not a conflict.
2. Across completed references, coalesce a repeated Module identity **only**
   when its exact selected schema, whole-artifact bytes, Module location and
   provider binding/rule/target/runtime context agree. Independently captured
   byte-identical artifacts qualify; compare bytes, not just digests.
   The same Module in different bytes rejects, even if only packing, row order
   or unrelated co-located content differs, or contracts appear compatible.
   Equal parent bytes with different selected dependency snapshots also
   reject. Composition performs no replacement, repacking-equivalence proof
   or first/last-input selection.
3. This is cross-reference snapshot reuse, **not** relaxation of the
   single-reference factory's duplicate-input rule: duplicate Module
   identities within one submitted artifact/provider set still reject even
   for equal bytes. Preserve each retained Module's own manifest and
   ModuleRef target/runtime expectations; do not synthesize a union manifest
   or replace them with the composite's fail-fast policy. Exact schema
   selection does not force Feature 13 on Modules with no retained use.

Cached admission is reusable only for the same complete context above.
Composition neither runs provider Bodies/initializers nor resolves native
symbols. Snapshots identify selected revisions, not authenticated producers
or new nominal type constructors.

### Actual identity, expected views and lookup

After snapshot agreement, resolve every type view to its actual provider-local
TypeDef. Intern one destination nominal type per **selected actual Module and
actual TypeDef in that snapshot**. Resolve aliases/Imports before interning.
Names, ExportKey text alone, equal layout and coincident local numeric handles
are not deduplication keys. Field identity likewise uses the actual owning
TypeDef and actual provider FieldDef, not a name, offset or ordinal alone.

Keep actual declarations separate from all original expected graphs. A shared
actual node can satisfy several views; retain each original using/owner
context, Module/key/kind, ModuleRef tuple, complete declaration/ordered fields/
Layout variants, dependency edges, aliases/equalities and stronger relied-on
facts. Check each view against the selected actual provider under the existing
directed relation. Do not require the views to equal one another, choose a
weakest view, or overwrite them with actual declarations or a newly selected
root view. Deduplicate obligations only when their complete comparison
contexts are identical.

Lookup-root status is the union of **explicit type-root selections**.
A hidden closure view becomes a root only if another input explicitly selected
that same actual type. Otherwise it remains hidden but usable for required
type/layout traversal. Private metadata grants no access. Expose original
provider declaration names, not keys or generated renamings. Distinct actual
types remain distinct: equal visible lookup names produce ordinary source
ambiguity when used, not a composition winner or a host dictionary exception.
An equal-named hidden closure node contributes no lookup candidate.

### Field selection and aliases

The selected field set is the union of checked explicit FieldDef selections,
keyed by actual owner/field. A key was selected only in its declaring Module,
with its owner among that reference's explicit type roots. Composition cannot
select keys absent from those inputs. Full public field metadata, provider
presence, type deduplication or root promotion alone grants no field-path use.
Keep the boundary owner's existing allowance for an exact retained FieldDef
Import translated from its original context; it is a particular checked
binding, not permission to discover other keys or fields.

Consequently, a Pair reference carrying hidden Leaf layout does not select
Leaf.Value. A separate Leaf reference selecting that owner and field enables
the nested path after their actual Leaf identities reconcile; every preceding
external field step still needs its own selected binding and ordinary access.
Removing the Leaf field selection must reject the affected query even though
size/alignment/identity use and full layout checking can still succeed.

Repeated keys across inputs are idempotent selections, unlike duplicates
inside one factory key list. Distinct selected aliases to one actual type or
field produce one identity and retain every key/view and same-definition
obligation. Existing Imports keep their original keys and contexts.
For **new** uses only, use the first explicitly selected alias in left-to-right
flattened input order after equality is established, as the boundary owner
already permits. Retain this ordered selection history when composing
composites. Without an explicit selection of that declaration, retain its
already-admitted closure/import binding; do not reselect closure keys.
Input order chooses neither a conflicting snapshot nor a lookup candidate.

## Remapping and downstream handoff

1. Build complete maps from each input type arena and each original-view arena
   to destination handles. Map fields through checked actual-owner/FieldDef
   correspondence and ordered membership. Colliding source handle values may
   map to different destination types; different handles may map to one actual
   type. Keep snapshot-local rows in their original namespaces, not as output
   handles. Destination numbering is not identity.
2. Rewrite all affected declaration/field types and origin/expected-view
   associations using those maps. Preserve complete query roots, ordered
   owner/field steps, access/authorization and target/observation dependencies.
   Derive each next immediate owner from the previous complete field type.
   Never transplant a field ordinal into a same-named owner or drop a hidden
   node needed by an expectation.
3. Carry the complete association through source binding, `BoundProgram`,
   shared bound-tree traversal/rewriting, mandatory verification and lowering.
   Preserve query-only restrictions; no construction, Copy or value access
   follows from merged fields. A carrier limited to one replaceable expected
   declaration or lacking the complete selected directory must be extended
   internally before this operation can succeed; no public trust descriptor
   or layout-shaped fallback is introduced.
4. Consumer emission retains TypeDef expectations and FieldDef stubs in their
   checked owner ranges, Imports/ModuleRefs and every independent alias/view
   obligation. Preserve separate stubs/contexts where one cannot carry the
   original views. Apply the boundary owner's existing full table/TypeSpec/
   Layout/FieldPath, Body TypeMap/FieldMap and `0045`/`004A` remapping rules.
   Retain Imports/stubs when direct references would erase obligations;
   never substitute bytes or keep only a representative alias's expectation.
   Source-free `LoadStandard` must check the same selected providers and
   original edges before query reconstruction/lowering. Reuse in another
   compilation creates fresh local maps, not transplanted type numbers.

## Failure boundary

| Condition | Proposed result |
|---|---|
| Null `references` | `ArgumentNullException`. |
| Fewer than two entries or null entry | `ArgumentException` naming `references`. |
| Non-LayoutQueries/non-query-only input, unsupported retained schema/contract/target or unavailable preservation carrier | `NotSupportedException`. |
| Conflicting Module snapshots/binding contexts, malformed graph, wrong owner/kind/reference or incompatible original view/equality | `InvalidDataException`. |
| Distinct visible types with equal lookup spelling; missing selected field or unauthorized query/value use | Ordinary source diagnostics at affected binding/use, not an arbitrary provider choice. |
| Checked capacity/resource exhaustion | Existing explicit overflow/resource failure; no truncation or partial reference. |

All required structural, bounds, schema/support and binding checks complete
before affected use. Producer-published semantic truth is still not
mandatorily re-proved. No partial result, ignored obligation, alternate schema
retry, provider search or new Terrum runtime failure category is permitted.

## Required conformance cases

These cases define the full acceptance gate. The bounded nested-selection,
alias, name-ambiguity and snapshot-conflict cases noted above now have
regressions; the broader graph, remapping, replacement, ownership and resource
cases remain uncompleted.

| Class | Case and required result |
|---|---|
| Positive: nested selection | PairProvider imports LeafProvider's type; separate references select Pair's path field and Leaf.Value with the same provider closure. One actual Leaf, retained Pair expectation, correctly bound nested offset and original TypeIdentity. Use unrelated synthetic names/keys too. |
| Positive: type-only | Compose disjoint declaration-only providers; size/alignment/identity queries succeed without field selection. Providers retaining already-admitted query Bodies need no dummy callable or new execution admission. |
| Edge: repeated closure | Chain/diamond, co-located Modules, separately captured equal bytes, duplicate references and nested composites reuse actual nodes without losing any incoming view. Explicit roots promote only the matching hidden nodes. More than 16 inputs has no set-specific rejection. |
| Edge: names and keys | Hidden equal-named types do not collide with roots. Distinct visible equal-named roots stay distinct and diagnose source ambiguity. Equal keys in different Modules do not coalesce identities. Reversing inputs changes no permissions or conflict result. |
| Edge: aliases/views | Different explicit type/field aliases to the same actual definitions yield one identity, all original views and same-definition checks. Flattened alias order governs only new-use representatives; a stricter original view cannot disappear. |
| Negative: snapshot conflict | Same Module in different bytes, including compatible row relocation/repacking; or identical parent bytes with different dependency snapshots. Reject in either order. Duplicate Modules inside one factory input remain invalid even for equal bytes. |
| Negative: selection/access | Omit Leaf.Value, select only its owner, or rely on public closure metadata: reject the affected field path. Wrong owner, private/static field, wrong-kind key, alias splitting or an incompatible retained view never succeeds by name/offset or a weaker view. |
| Negative: exact scope | Reject other/mixed schemas and missing required Feature/target/runtime support; no F/A2/Development fallback. Type-only metadata does not acquire Copy/value operations or automatically require Feature 13. |
| Transformation | Under unchanged provider snapshots, nontrivially permute consumer type/field/map rows, re-emit and source-free load. Preserve every expected owner/key/view, FieldPath step, identity and fixed/coupled observation; verify cumulative versus terminal offsets. |
| Ownership/resources | Later caller list/byte mutation cannot affect retained results; independent compilations remap independently. Null/empty/singleton inputs fail as above. Finite dependency cycles do not authorize inline-storage cycles; exhaustion fails without treating unvisited edges as satisfied. |

Implementation must supply the complete immutable multi-view association,
actual-identity maps, root ambiguity handling and downstream preservation
before claiming this API. Final-input, headless supervised Windows x64
execution remains a separate required gate for runtime claims; neither this
document nor successful composition is that evidence.

No in-scope merge policy is left to implementation. Formal approval of the
recommended API and strict same-snapshot policy remains outstanding.
Compatible-revision reconciliation, mixed-schema composition and any new
source disambiguation surface remain outside this decision, not implicit
fallbacks.
