# Standard TRRM nested-field initialization destinations

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This document selects a recommendation for later
user review, not normative adoption, compiler support, qualification or
released-schema stability. No accepted specification is changed.

## Finding and recommendation

The accepted source contract requires eligible nested-field initialization
destinations. [Initialization references](../specs/language/references.md#initialization-references)
form a capability for one exact vacant place, without a field-depth limit.
[Structural state and construction](../specs/language/ownership-and-cleanup.md#construction-and-publication)
permit initialization of eligible subplaces and retain nested partial children.
[Grammar](../specs/grammar-spec.md#51-initialization-references-at-call-sites)
supplies `ref init place`, and repeated member selection supplies the path.
For example, `Fill(ref init storage.Inner.Value)` is an accepted-target
expression fragment **if** its declarations, access, vacancy and backing meet
those contracts; it is not a standalone or compiler-validated example.
No new source syntax or general permission to initialize arbitrary members
is needed.

The missing decision is the **selected Standard Body representation and
profile extension**, alongside implementation gaps:

| Evidence | Boundary |
|---|---|
| [Assigned PlaceSelection](../specs/il-instruction-set.md#typed-operand-records) and [machine registry](../specs/representation/trrm-1.registry.json) | Field target kind `2` requires reference kind `1`, an exact FieldDef row. Reference kind `2` is a MethodMap index for an element operation, not a path. |
| [Initialization Body producer/reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs), especially `TryGetInitializationReferenceCall`, `EncodeInitializationReferenceCallBody` and `DecodeStandardInitializationReferenceBody` | Source admission/emission selects a direct Home or one BoundFieldRead whose receiver is the output Home; DD reconstruction rebuilds that one-level form. This does not establish nested-field support. |
| [FieldPath metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) and [table codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmFieldPathTable.cs) | Section `0x004B` already carries a complete root TypeSpecRef and ordered FieldDef/Import steps. The table codec checks framing and typed references; that alone does not establish an executable owner chain or initialization authority. FieldMap's `0x1D` token is not a PlaceSelection reference kind. |
| [Selected initialization direction](standard-trrm-initialization-destination-callsite.md) and [local-field composition](standard-trrm-init-reference-field-layout-composition.md) | Both remain unapproved. The latter explicitly bounds executable field formation to one level and excludes nested paths. |

Recommend one **unapproved PlaceSelection amendment**:
`targetRefKind=4=FieldPathRow`, with `targetKind=2=Field` and a plain
one-based `0x004B` row in `targetRef`. Reuse the complete existing path codec,
not a FieldMap index, tagged token, terminal field ordinal or byte offset.
Select it only in the exact successor below. A path row selects no storage
by itself and grants no access.

### Allocation and alternatives

The allocation review includes the accepted reference-kind domain `0..2`,
the initialization proposal's DD-only payload flags, and the
[fixed-array owner](standard-trrm-fixed-array-identity-shape.md#exact-schema-allocations-and-capability)
and its [A2](standard-trrm-rootless-copy-type-evidence.md),
[empty](standard-trrm-empty-fixed-array-initialization.md),
[unit](standard-trrm-unit-fixed-array-initialization.md) and
[A3](standard-trrm-closed-constructed-array-elements.md) successors.
Those candidates retain **`3=FixedArrayLayout` for Element targets**.
Choose globally distinct kind **4** for FieldPath, rather than overloading 3
by target kind. This is a proposed coordinated assignment, not a claim that
an unused byte is available without review. Compact implementation kind
`04=field reference` and record kind `04=PlaceSelection` are separate
namespaces; neither assigns this operand value.

| Alternative | Assessment |
|---|---|
| **Kind 4 plus existing FieldPath — recommended** | One complete root/ordered signature, one captured destination, unchanged 24-byte record and DD instruction. Reuses existing path binding/remapping instead of introducing intermediate place-flow requirements into the bounded initialization profile. Adds conditional Feature 13 retention. |
| Chain existing kind-1 field selections, then form the reference | Coherent zero-new-tag alternative, not inherently unrepresentable in register IL. [L1](standard-trrm-local-aggregate-layout-execution.md#reused-operations) and [Repeat field writes](standard-native-fixed-array-repeat-initialization.md#standard-composition-no-successor-amendment) already describe captured PlaceHandle chains in their own profiles. An initialization composition must still specify intermediate Initialize authority, full root/generation lineage, reconstruction and merges; the old local-field profile does not select it. This is not a reason to require those broader profiles or to flatten the chain to its last field. |
| Replace field targetRef with the general FieldMap token union, including rewriting kind 1 | A valid draft redesign that unifies direct/import/path operands, but migrates all field consumers and remappers, changes existing kind-1 meaning and needs a new exact agreement too. It saves no PlaceSelection bytes and is broader than this gate. Draft compatibility is not the reason to reject it. |

The evidence establishes a missing selected contract/profile, **not** an
inability to express nested addresses using any existing instruction sequence.

## Exact successor and scope

Select the exact, case-sensitive, out-of-band identity
**`trrm/1-draft-init-reference-bodies-local-field-layout-2`**.
It consists of the complete
[`...local-field-layout-1` composition](standard-trrm-init-reference-field-layout-composition.md)
plus precisely these amendments:

* Permit transitive closed, nongeneric, rootless Copy Blittable
  Sequential/Transparent inline struct declaration/layout closure along the
  selected field path, retaining complete field inventories, not only the
  selected leaves. Preserve the inherited Copy evidence and explicit TypeDef
  publication/origin rules. Do not infer Copy from layout or absent Cleanup.
* Add the kind-4 field selection below for DD initialization formation and
  ordinary DA readback. The terminal T must already be an admitted
  initialization referent/read type in the inherited profile; no new leaf
  type family, aggregate value ABI, statement/CFG form or output relation is
  selected. Existing direct Home/kind-1 field cases remain available.
* Admit the existing FieldPath codec and its checked FieldDef/Import binding
  for those selections. Extend only the applicability of the
  [existing explicit type/field-key mappings and checked declaration projection](standard-trrm-layout-queries-source-provider-boundary.md#recommended-producer-api-and-exact-mapping)
  to this scoped closure under the new identity; retain this successor's
  Copy/layout and provider-selection rules. Do not select the LQ query-Body
  profile or a new authoring API. Fresh source can emit an imported step only
  from those checked origins and explicit keys. A source route without that
  admitted handoff must decline, not derive keys or invent an Import. The
  separate [source type/field CLI transport](standard-trrm-source-type-cli-transport.md)
  only transports existing explicit provider mappings; it adds no
  compile-trrm source-provider input.

Every Module retaining a path use requires **`13/1`**, with no immediate
prerequisites. Retained Copy/layout facts still require **`2/1 -> 1/1`**;
bounded Boolean output selectors conditionally require the inherited
provisional **`10/1`** with no prerequisites. FieldDef Import/Export also
retains its existing 13/1 requirement, including declaration-only providers.
Derive closure per using Module; a scalar-only method importer does not
inherit a provider's entire list. Add no Feature or revision.

Keep `(1,0)`, implementation kind `01`, codec `01`, mapMask framing, record
sizes, Windows x64 TargetProfile `(1,1)`, target requirement `1` and inherited
runtime-policy/FailFast boundaries. For this bounded binding/re-emission
operation, the root and every supplied provider explicitly select the new
identity, preserving the predecessor's uniform-selection policy.
Select before schema-dependent decoding, production or binding.
Old B+I, local-field-layout-1, B+I+G, L1 and array identities remain unchanged;
none is an alias or fallback. Feature lists and equal version bytes do not
compose decoders. Other compositions require their own exact agreement under
[format versioning](../specs/representation/format-versioning.md).

### Proposed record meaning

The complete record remains 24 little-endian bytes. Domain values below are
decimal; the `0x` forms identify hexadecimal record/section/opcode values.

```text
u8 recordKind=0x04, u8 flags=0, u16 reserved=0
u8 targetKind=2, u8 accessKind=3, u8 destinationKind=1, u8 baseKind=2
u16 destinationIndex=R, u16 baseIndex=H
u8 indexKind=0, u8 targetRefKind=4, u16 indexIndex=0
u32 targetRef=fieldPathRow
u16 referentTypeMapIndex=T, u16 flags=0x0001 or 0x0003
```

H is one actual role-3 local Home, not a loaded value or descriptor's
referent. Its complete type must equal the path's normalized complete root
in the Body-owning Module context. R is R0..R13 and must match DD's encoded
destination. DD retains its four-byte encoding and the
[existing candidate flags](function-pointer-checked-output-parameters.md#initialization-capability-formation):
`0001` forms `ref<init T>`; `0003` additionally retains independently
established whole-place write authority. Header flags/reserved remain zero;
`0002` alone, other payload bits and flagless short `67` are not init forms.

For **DA readback**, use the same path/base/type and register correspondence,
but `accessKind=1=Read` and payload flags zero; require the selected leaf
complete and its ordinary read/Copy/dependency contract satisfied.
No other operation/access combination for reference kind 4 is selected by
this successor. In particular it does not add D8/short-91, take/retire or
ordinary-reference formation variants merely because they share record 04.

`fieldPathRow` is `1..0xFF_FFFF`, high byte zero, bounded by `0x004B`;
it is **not** `0x1D00_0000 | row`. Keep the existing path row
`u32 rootTypeSpecRef, u32 fieldCount>0, fieldCount ordered u32 FieldReferences`
and exact extent `8 + 4*fieldCount`. Steps remain FieldDef DefIds `0x14` or
FieldDef Import tokens `0x1C`, never nested FieldPath tokens. Counts, products,
offsets and extents require checked bounds; no u16 path truncation or arbitrary
source-depth limit is introduced. Body-local indexes retain all 65,536 u16
values. A nonempty FieldMap is unnecessary for this direct global reference.

One-step path rows are valid, but canonical producers use the inherited
direct kind-1 form when its exact local non-generic owner semantics suffice.
Nested paths use kind 4. Intern FieldPaths only by complete root and ordered
field identities/contracts, preserving distinct Import expectations and every
using context; do not intern Homes or invent a new path-table ordering rule.

## Path, authority and consumer obligations

Resolve from the **complete root**, retaining qualifications and selected
nominal origins. For every step, resolve its FieldDef or checked Import in
the Body's own Module context, require exact immediate owner/range membership,
apply the current owner's admitted complete type context, and derive the next
type from that field's complete stored type. Every intermediate transition
must remain inline in the scoped layouts; the final stored type must equal
TypeMap[T] exactly. Equal size, terminal type or field spelling is insufficient.
Destination identity is the captured root storage generation plus the ordered
resolved field chain, not the Home alone or the final FieldDef alone.

The following obligations are part of the recommendation, not current
support claims:

| Layer | Obligation |
|---|---|
| Source producer / shared verification | Capture the actual local root generation and ordered field identities once at the source argument position, before later arguments. Project storage without reading/copying vacant ancestors. Check source visibility, initialization access, vacancy of the **leaf**, all enclosing validity/loan/backing restrictions, and real final alignment. A partial ancestor is not whole vacancy; a partial/live/maybe-live/reserved leaf is not an eligible fresh destination. Formation reserves only the selected destination under I's existing reborrow rules. |
| Structural reader / consumer | Before affected use, check exact schema/Features, table/record extents, reserved fields, selector/register/index domains, the plain row namespace, nonempty path, complete root/type correspondence, every ordered owner/range/instance-field link and supported layout/type chain. Check required binding and geometry without trusting an in-range terminal row as proof of the path. Do not silently drop unsupported steps, qualifiers or state/loan relationships. |
| Imports and provider replacement | A FieldDef Import must expect a FieldDef stub in the expected owner's range; resolve its exact Module/key/kind and independently bound owner to the same actual provider owner. Compare complete field type, static/mutable/access role, retained Copy/Blittable/layout/profile/ordered-field obligations under [directed replacement](../specs/representation/binding-and-replacement.md#fielddef-importexport-and-layout-path-obligations). A direct DefId naming an expected stub cannot bypass its Import. Preserve every original expectation and stronger relied-on actual fact, even after co-location or replacement by a direct provider row. Private provider-local path closure need not acquire synthetic exports. |
| Source-free reconstruction | Rebuild one typed field-projection chain over the captured Home, with each intermediate complete type, checked field identity/ordinal, Module origin, generation, authority and dependencies. Feed that chain to the ordinary BoundReferenceFormation/DA read machinery; do not rebuild only the final BoundFieldRead or read an intermediate aggregate value. Keep source declaration origins and destination/result correlation, not an ordinal-only or offset-only surrogate. |
| Shared traversal / rewriting | Visit the formation's whole place chain and all root/field/type/provider references, including nested receivers, without treating projection as a value read. Remapping, substitution and replacement must preserve path order, qualifications, original/actual origins, loan lineage and distinct descriptor/referent places. Meaning-changing rewrites retain the ordinary shared verification obligations. |
| Lowering | Derive the cumulative leaf location with checked target-size/offset arithmetic from the selected complete layout chain and actual captured root. Validate actual leaf alignment/extent; aligned aggregate storage does not make an underaligned packed leaf reference-safe. Write the original selected leaf, never an aggregate copy or copy-back temporary. Preserve normal-only publication, saved results, partial-parent validity and existing failure/cleanup behavior through JIT/AOT. No new ABI word, root/pin, allocation, marshalling or lifetime extension is supplied. |

Initialization access is not ordinary postpublication write permission.
The descriptor slot, the selected stored T and any referent carried inside T
are separate identities. A pointer-valued leaf is a pointer **cell**, not its
pointee; a checked-reference-valued leaf has the same slot distinction only
when its independent representation is admitted. No path step dereferences
either descriptor. Initialization completes that exact leaf under the shared
[CallSite source-use-5/publication contract](standard-trrm-initialization-destination-callsite.md);
it neither completes siblings nor bypasses affected parent validity gates.
A Home containing T cannot substitute for the live init descriptor input.

Producer truth and consumer checks retain the
[accepted trust boundary](../specs/representation/reader-trust.md).
Source production must establish vacancy, authority and correct state facts.
Consumers perform safe structural/type/owner/support/binding checks and retain
the relationships needed by their reconstruction/lowering; this amendment does
not require arbitrary re-proof of authored Bodies, source accessibility or
foreign promises. False semantic facts remain producer defects, not serialized
authority or a guaranteed consumer diagnostic.

### Re-emission and explicit exclusions

Decode and rebuild Module partitions/manifests, expected/actual Imports and
Exports, complete TypeSpecs, TypeDef/FieldDef and layout/profile ranges,
FieldPath roots and **every ordered step**, Method/Body references,
all Body maps and other retained consumers of shared path rows,
kind-4 global rows, records/Blocks/entry states,
preparation/result/destination correlations, instruction forms/offsets/extents
and retained source/debug references. Include path-only dependencies in
retained closure and regenerate caller-local witnesses/native fixups.
Optional debug data may be remapped or legitimately omitted, never used to
carry the missing path semantics. Clone shared structural rows when their
using contexts cease to agree. Byte substitution, shortening the root,
dropping Imports after resolution and restamping provider ownership are not
meaning-preserving conversions.

This successor adds **no** StaticField/native-data target, array/element/range
step, property/indexer/getter, arbitrary register/PlaceHandle root, pointer or
checked-reference dereference, class/GC/own/box transition, optional traversal,
raw-Union overlap, unique/Auto layout or generic owner execution.
These are scope exclusions, not rejection of independently accepted source
forms or another profile's existing operations. A pointer may be the admitted
terminal stored type without authorizing traversal through it.
Whole-Span formation, hidden-output preparation, general CFG/phase-3 predicate
composition, failure transport, stored reference layouts and independent
precompiled-provider ABI remain under their existing separate gates.
Inhabited `void` still has no exposable payload address.

## Intended owners and remaining review

On later adoption, amend only PlaceSelection's reference domain/validity and
DD/DA field-path semantics in [the instruction owner](../specs/il-instruction-set.md#typed-operand-records),
the corresponding `record_domains.TargetReferenceKind`/constraints and
canonical-writer descriptions in the
[registry](../specs/representation/registry.md), and the metadata/reader-trust
cross-references identifying a direct `0x004B` row use.
Retain the existing FieldPath codec and binding owner. This proposal replaces
the predecessor's one-level/no-FieldPath restriction **only under the new
identity**; it supersedes neither the predecessor identity nor the shared
initialization, fixed-array or runtime-publication proposals.

The user must later review the **kind-4 amendment versus the existing-chain
alternative**, the exact successor composition and scoped imported-path
admission, before normative integration. The recommendation leaves no
in-scope operand, root/owner, flag or state meaning to invent.
Additional root/step/type/profile compositions are excluded rather than
implicitly selected. Compiler implementation and qualification remain open.

### Required future acceptance cases

These are design gates, **not tests run or support evidence**:

| Family | Required cases |
|---|---|
| Positive source/state | Two- and deeper-level unrelated Sequential/Transparent paths, several same-typed sibling leaves and scalar/pointer-cell outputs; retain exact local generation and partial ancestors, normal publication and selected readback without initializing siblings or pointees. Check independent outer-write retention and duplicate/overlapping reservation rejection under existing reborrow rules. |
| Structural negatives | Zero/out-of-range/tagged-as-row path, empty/truncated/overflowing extent, recursive path token, wrong root or middle owner, reordered steps, static/intermediate-indirection/array step, wrong final type, index garbage, flags/header misuse, unavailable destination register and absent Feature 13. Include equal-layout/different-nominal and equal-leaf/different-root paths. |
| Source truth / authority | Reject nonvacant leaf, whole-partial-as-vacant, premature ancestor/leaf reads, readonly-to-write upgrade, expired/rebound backing and unsafe-only authority substitution. Keep false-producer semantic audit cases distinct from required structural-consumption failures. |
| Providers / transformations | Local closure-only paths; imported root/field stubs with exact owner binding; missing/wrong-kind keys and changed intermediate field/type/layout/Copy/access or OutputState reject. Compatible private row/Body changes succeed. Source-free load, shared rewrite, provider replacement, re-emit and reload preserve every original/actual origin and path-only dependency, including an uncalled retained expectation. |
| Boundaries / lowering | Permute all relevant global rows, maps, records and Blocks across short/long and u16/u24 boundaries; check overflow and packed intermediate/leaf alignment. Subsequently qualify actual selected-leaf writes/readback through source-free JIT and supervised AOT with exact compiler/schema/target/provider inputs. Old/equal-version and array/G/L1 selections must not gain admission by retry or Feature sniffing. |
