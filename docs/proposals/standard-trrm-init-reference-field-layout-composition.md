# Standard TRRM initialization-reference local-field layout composition

**Status: unapproved; provisionally actionable under the user's explicit
missing-contract direction.** Formal approval, normative adoption,
implementation, qualification and released stability remain separate.

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.”

## Gap and recommendation

The [B+I fixture](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardInitializationReferenceBodyTests.cs)
already covers local FieldDef-backed DD formation, selected DA readback and
exact field/referent mismatch rejection. However,
[`EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
rejects explicit source TypeDef exports under B+I; the
[L1 layout/publication route](standard-trrm-local-aggregate-layout-execution.md)
does not select B+I initialization-reference Bodies. Consumer admission must
support the exact combined schema/catalog. This is a composition and
publication gap, not a missing initialization-reference semantic rule.

Recommend a **new exact B+I+local-field-layout composition**, identified out
of band as **`trrm/1-draft-init-reference-bodies-local-field-layout-1`**.
It carries the containing declaration/layout and its checked provider origin
alongside the existing one-level field output Body. It adds no source syntax,
API shape, ABI word, runtime policy, opcode or table.

| Alternative | Compatibility and capability consequence |
|---|---|
| Reuse L1 unchanged | Insufficient: L1 does not select I, and its semantic MethodDef-provider subset does not admit I's checked formals/contracts. Adding I only to a dispatch predicate is not exact composition. |
| Expand L1 through a separately identified L1+I successor | Coherent, but inherits A2R1+Repeat, foreign/storage-address, scalar, CopyOrigin/ReturnCoverage and aggregate-construction applicability that this field case does not need. Reusing L1's old identity would change its selected contract. Not recommended here. |
| **New bounded B+I+layout identity** | Recommended: reuse the relevant layout, Copy-evidence and explicit-publication mechanisms without importing L1's complete catalog or aggregate Body extensions. Requires explicit producer/consumer support for this new selection; neither old identity rolls forward. |

Keep **`trrm/1-draft-init-reference-bodies-1`** and
**`trrm/1-draft-local-aggregate-layout-execution-1`** unchanged and
unapproved. This proposal supersedes neither. Equal `(1,0)` bytes establish
no compatibility; there are no schema aliases, fallback readers or automatic
conversion.

## Exact composition and Feature closure

The new selection contains precisely:

* **B+I unchanged:** the assigned Standard framing/register-Body schema,
  [shared initialization inputs/preparation/publication](standard-trrm-initialization-destination-callsite.md),
  [DD flags](function-pointer-checked-output-parameters.md#initialization-capability-formation)
  and its [bounded predicate attachments](standard-trrm-predicate-graph-attachments.md).
  Preserve complete reference types (TypeSpec `0x0E`, immediate-inner init
  qualification `0x04`), Param mode `00`, formal bindings, saved-result
  correlations and existing failure/provenance obligations.
* **Local-field layout:** closed nongeneric rootless Copy Blittable
  Sequential/Transparent struct declarations with complete instance-field
  inventories and field types already supported by B+I. Retain the existing
  executable case: a vacant role-3 local Home, eligible one-level instance
  field, DD output formation and selected DA readback. Whole-aggregate
  construction, reads/stores, parameters/results, nested/array paths and
  L1's empty-Sequential store are not added.
* **Publication/provider retention:** apply the existing
  [explicit source Copy type mappings and reference-key selections](standard-trrm-source-copy-type-identity.md)
  to this destination, including no-entry libraries containing supported
  B+I Methods as well as type-only libraries. Reuse its checked local
  declaration selection, immutable original/actual origins and key rules;
  introduce no overload, mapping format, field-key default or Core role.

Reuse the unapproved **`TypeDef.flags=DeclaredCopy` bit `0x20`**
and its exact applicability/mask from
[rootless Copy evidence](standard-trrm-rootless-copy-type-evidence.md#exact-unapproved-field-and-capability-amendments):
mask `0x2F`, bit `0x20` permitted only on struct declarations/expectations.
Clear is not positive Copy evidence; layout, size or absent Cleanup cannot
replace it. Derive rootlessness from complete admitted storage. Select only
this nominal carrier, not A2's TypeSpec `0x11`, FixedArrayLayout or array Bodies.
No numeric allocation or Feature revision is invented.

Use governing edition `1`. The exact additional closure is:

| Retained use in a Module | Effective Feature pairs and immediate prerequisites |
|---|---|
| The local-field layout/Copy carrier, an expected Copy TypeDef, or reliance on it | `2/1=RootlessNativeValues -> {1/1=NativeBoundaryContracts}`; `1/1 -> {}`. Applies also to declaration-only providers. Reuse A2's bounded nominal Copy meaning within 2/1. |
| I's already-selected bounded normal-result selectors, including Boolean defaults | `10/1=PublishedPredicates -> {}`. Unconditional outputs without predicates do not add 10/1. |
| A scalar-only importing Module with no retained type/layout or selector use | No additional pair merely because its provider requires 2/1. Its own retained expectations determine its closure; the provider retains its separate requirements. |

These are least effective requirements, not mandatory duplicate physical
rows. Preserve the B+I catalog otherwise; the field-backed operation gains
support for exactly this nominal 2/1 composition, not unrelated capabilities.
No NativeImport/native call is required, and 1/2 grant no unsafe authority.

DD/DA use a direct FieldDef row, **not a FieldMap**. Merely carrying
TypeDef/FieldDef/Layout/LayoutField does not require `13/1`. Nonempty
FieldMap, FieldPath, LayoutQueries or independent FieldDef Import/Export
retain their existing 13/1 obligation and are outside this addition.
General predicates 17, arrays 20, E1 21, RootPin, NativeOutput/F/S,
scalar-frozen, CopyOrigin and raw-Union coverage compositions are not selected.

Keep `(1,0)`, `implementationKind=01`, codec `01`, the existing
`17 + 4*popcount(mapMask)` prefix, index/count domains and record lengths.
One artifact selects this entire schema/catalog before decoding; neither
Features nor the root's selection selects a provider's decoder. Add no
serialized schema discriminator.
Use Windows x64 TargetProfile `(1,1)`, Module target requirement `1` and
Standard `runtimePolicy=0`; retain the existing FailFast implementation
selection. No failure transport, catch/unwind admission or new ABI is chosen.

## Reused records, ownership and binding

The [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
[instruction](../specs/il-instruction-set.md#typed-operand-records),
[manifest](../specs/representation/semantic-manifests.md) and
[registry](../specs/representation/registry.md) owners retain their records.
Preserve [Windows x64 layout](../specs/targets/windows-x64-data-layout.md)
rules for declared inputs, checked geometry and actual field alignment.
The Copy bit and composition applicability above remain proposed amendments;
no normative owner is edited here.

| Existing carrier | Required use |
|---|---|
| Module/Feature, ModuleRef/Import/Export, TypeDef/TypeSpec, FieldDef/Layout/LayoutField/TargetProfile | Preserve exact Module/key origins, contiguous owner ranges and complete default/profile layout variants. Layout carries declared Packing/Alignment and Blittable commitment, not authoritative derived offsets. Explicit TypeDef exports name public local declarations; public visibility alone creates no key. |
| MethodDef/MethodSig/Param, Effects `0001`, OutputState `0002`, Dependency `0004`, bounded attachment `000B` | Retain B+I's complete expected/actual call contracts, owner ranges, formal ordinals, predicates and normal states; do not apply L1's contract-free provider restriction or erase rows to satisfy it. |
| Body Type/Home/Method maps and BlockEntryState `01` | Keep distinct local storage, complete types, original call edges and reconstructed reservation/state lineage. A live descriptor entry does not prove referent vacancy. |
| PlaceSelection `04` (24 bytes), SemanticStore `05` (20 bytes), CallSite `02` and preparation `03` | DD uses `targetKind=2`, `targetRefKind=1`, a one-based FieldDef **row**, exact T referent map, captured local-Home base, `accessKind=3` and payload flags `0001/0003` under I. Call input remains source-use `5`. DA uses its separate ordinary read selection with zero payload flags; DD-only flags cannot be reused on DA. |

1. The producer checks accepted
   [reference formation](../specs/language/references.md#initialization-references)
   and [field state](../specs/language/initialization-and-flow.md#states-reads-and-initialization).
   Capture the field once in its actual local storage generation; preserve
   vacancy, reservation, actual extent/alignment and supporting backing.
   Initializing this child changes neither sibling state nor padding into
   initialized payload. A selected read requires that child's complete state,
   not fabricated whole-parent completion. Preserve I's normal-only
   publication and postcompletion access; add no copying, escaping or rights.
2. In this extension the DD field is locally defined in the **Body's owning
   Module**. Importing its method does not import that field independently.
   Private implementation types/fields remain closure-only with their Body;
   no synthetic exports or lookup access are needed. An explicitly selected
   type root or type crossing a published contract instead retains its exact
   public TypeDef key and complete expected layout under the existing mapping
   rules. Imported-field DD targets are not selected by this proposal.
3. Capture every artifact's exact selection before indexing. For this initial
   field-backed binding/re-emission operation, require the new identity for
   the root and **every supplied provider**, including unused/type-only ones.
   Admit each Module's own closure and every retained Import. Resolve exact
   Module/key/kind/owner before reconstructing provider Bodies; retain
   bodyless expectations separately from public provider-owned implementations.
   Co-located Modules keep the same obligations. Do not flatten provider
   ownership, search by spelling or route authored Methods through F native
   symbol binding.
4. Apply [directed replacement](../specs/representation/binding-and-replacement.md)
   independently to every original method/type expectation: complete types,
   modes, output states/conditions, Effects and dependencies; retained Copy,
   access, Blittable, Packing/Alignment, ordered field/type/layout
   correspondence and derived target geometry. Keep stronger actual facts
   relied on separately. Equal size, names, machine addresses or rewritten
   private Bodies cannot erase a retained obligation; unrelated private
   implementation changes need not be byte-identical.

Consumers perform exact-schema/support, bounds, namespace, owner/range,
flag/type correspondence, complete layout and binding checks before affected
use. Keep shared compiler verification/traversal enabled on reconstruction
and meaning-changing rewrites. Under
[reader trust](../specs/representation/reader-trust.md), producer truth is
distinct from these checks: no arbitrary semantic re-proof of provider Bodies
or serialized authorization is introduced.

## Remapping and re-emission

Select the destination separately; this operation re-emits into the same
new exact composition. Rebuild, rather than concatenate or byte-patch:

* Module partitions/entries/manifests, ModuleRefs, original Imports/Exports
  and separate expected/actual definition correspondences;
* complete TypeSpecs, TypeDef/FieldDef/Layout/LayoutField/profile references,
  declared layout inputs, original type roots and closure-only declarations;
* MethodDef/Body/signature/contract/predicate references and formal bindings;
  Type/Home/Method maps, DD's actual provider-local FieldDef row, separate DA
  selections, preparation and saved-result/destination correlations;
* records, Blocks/entry states/edges, shortest eligible instruction forms,
  offsets/counts/extents and retained SourceMap/origin references, or
  legitimately omitted optional debug data.

Use typed context-aware remappers and existing canonical writers; ordinary
valid consistently remapped noncanonical input remains readable. Never intern
distinct Homes, remap geometry/payload bits as indexes, restamp provider
Modules as the root, drop expectations or hide a missing requirement in debug
data. Regenerate caller-local witnesses and native fixups from the retained
program/origins. Preserve real destination alignment and lifetime through
joint JIT/AOT lowering; no copy-back temporary, extra output word or
independently replaceable precompiled-provider policy is added.

## Focused acceptance gates

These are required future cases, **not new test results or milestone acceptance**.

| Area | Cases |
|---|---|
| Local positive | Unrelated synthetic Sequential and Transparent Copy types, unconditional scalar output into one vacant field and DA readback; preserve sibling vacancy and exact layout. A correctly aligned field needs no import, fnptr, NativeAddress or aggregate constructor. |
| Provider round-trip | Explicit type-key publication in a B+I body-bearing library; separate method/type consumer and type-only dependency; source-free bind, rewrite, re-emit co-located Modules, reload and supervised headless JIT/AOT write/readback. Keep provider-local fields with their real Module, and closure-only fields out of source lookup. |
| Structure/state negatives | Wrong FieldDef namespace/row/owner/referent, absent Copy/Blittable or incomplete layout, bad flags and header bits, ordinary value Home as input 5; source/verifier cases for live/partial/overlapping destinations, lost backing, readonly upgrade, double initialization and premature reads. Distinguish malformed consumption from false-producer semantic audits. |
| Binding/replacement | Missing/wrong-kind keys, imported proxy as implementation, duplicate/conflicting origins, changed retained field type/order/access, Pack/Alignment or Copy, incompatible OutputState/dependencies; include an uncalled failing expectation. Compatible private Body/row rearrangement succeeds without losing the original view. |
| Remapping/boundaries | Independently permute global field/type/layout/Module rows, maps, records and Blocks; cross short/long and supported u16/u24 boundaries; reject overflow/truncation. Inspect DD/DA remapped references, owner ranges, distinct Homes and per-Module least closure, not only returned values. |
| Cross-feature isolation | Boolean true/complete and false/vacant add only 10/1 to the field carrier's 2/1→1/1 closure. Underaligned packed fields reject checked-reference formation despite eligible ordinary unaligned DA reads. Missing/conflicting requirements, old B+I/L1 providers with equal version bytes, general predicates, nonempty FieldMap/independent field imports, arrays, E1 and L1 nullary aggregate stores do not gain admission. |

Reuse existing public compiler/Standard operations and headless supervision.
Keep representation, binding, emission and execution evidence separate, with
final compiler/configuration/schema/input/provider identity and actual matched
test outcomes. No build/test-policy changes are part of this proposal.

## Remaining decisions

No in-scope encoding, Feature, key-selection or provider policy is left open
after this provisional choice. **Formal normative adoption and released-schema
stability cannot be claimed from this direction.** Mixed old-schema provider
interoperability, imported-field DD, a full L1+I composition or richer
aggregate/ABI/failure support would require separate decisions; none is
provisionally selected here. Implementation and focused qualification remain
gates, not approval evidence.
