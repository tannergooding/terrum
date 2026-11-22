# WitnessProjection predicate dependency codec

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow.** Every schema, number and integration choice
below is an **unapproved candidate**, not adoption, implementation support or
qualification evidence.

## Decision and owners

Recommend **one required, variable table `0068=WitnessProjection`**, with
closed original/requested interface-view pairs and selected-member mappings.
Keep `003A` kind **4** as a typed reference to this table, distinct from
kind 3's `0028=InterfaceWitness`. Retain the original nominal witness rather
than publishing a new conformance for the qualified self.

The [specification index](../specs/README.md) establishes these owners:

| Owner | Preserved contract; proposed integration |
|---|---|
| [Qualified-self witnesses](../specs/language/interface-witnesses.md#qualified-self-witness-projection), [complete types](../specs/language/types-and-normalization.md) | Preserve declared-self-only forward substitution, complete qualification, all-or-nothing adaptation and original selected bindings. Add no source rule. |
| [Logical witnesses](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain), [metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [registry](../specs/representation/registry.md) | Replace only the projection target/reference/mapping deferral with the candidate row below. Preserve assigned witness and associated-binding rows. |
| [General graphs](general-predicate-graph-schema.md#records-references-and-ownership), [standalone closure](standalone-published-predicate-graph-closure.md) | Complete their independently gated kind-4 target and typed copy/packing contract. Preserve graph occurrences, footprints and original binding contexts. |
| [Binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Apply complete retained-contract comparison, mandatory safe-consumption checks and ordinary noncanonical admission. No source/body reconstruction or serialized authority. |

`0067` already has a separate
[contextual binding-equality candidate](standard-trrm-c1-contextual-type-binding-views.md).
Select `0068`, not another interpretation of `0067`. Numbers remain mutable
draft candidates; no width benefit justifies relocating unrelated families.
Add no DefId, Import/Export target, clause, Body record, opcode or Feature ID.
Other projection consumers, including GenericCall and ConstantExecution, are
not enabled by this graph-only integration.

| Alternative | Assessment |
|---|---|
| **One indexed row with inline view/member records — recommended** | Keeps the entire checked projection together and reuses the original witness's declarations, bindings and contracts. No separately owned mapping tables or duplicated adapted MethodDefs. |
| Only source witness, requested types and rule version; derive every selection on read | Smaller, but leaves the effective selected default-owner instantiation unstated. A complete derivation design would need to choose that policy and preserve its original contexts; ordinary consumers must not redo witness/default selection. |
| Replace direct witnesses and projections with one tagged witness/evidence family | A substantive alternative, not prohibited by draft compatibility. It changes all witness references, ownership and evidence consumers. No such generalization is needed for this dependency target. |

## Exact candidate record

All words are little-endian. Omit the `0068` directory entry and section
when it has zero rows: [standard section framing](../specs/il-metadata.md#header-and-directory)
requires every present section to have a positive record count. A present
`0068` section uses required policy `0000`, the existing
`u32 offsets[recordCount+1]` framing and exact nonempty rows.

`0068` is a core typed table. Its cross-table fields hold direct typed
row/TypeSpecRef references, not section-local ReferenceSlot numbers.
The `0x0002` [optional reference-slotted policy](../specs/il-metadata.md#optional-section-reference-slots)
does not apply; there is no slot-count prelude or ReferenceSlot array.
Its row prefix is **28 bytes**, in this order:

| Field | Meaning |
|---|---|
| `u32 ownerModuleRow` | Required Module row identifying the original publishing/binding context of this checked projection. Not the graph's current owner or witness publication authority. |
| `u32 sourceWitnessRef` | Required `0028` InterfaceWitness row, never another projection or an Import token. |
| `TypeSpecRef requestedSelfTypeSpec` | Required closed complete requested self. |
| `TypeSpecRef requestedInterfaceTypeSpec` | Required closed complete root interface instance. |
| `u32 projectionRuleVersion` | Exactly **1**, selecting the retained-target/ordinary-attenuation rules below. Zero and all other versions have no candidate meaning. |
| `u32 viewCount` | Number of following interface-view records; at least one. |
| `u32 memberCount` | Number of following selected-member records; zero is valid only for an empty callable-requirement closure. |

After the prefix, concatenate all views, then all members:

| Inline record | Exact payload |
|---|---|
| Interface view, **8 bytes** | `TypeSpecRef sourceInterfaceTypeSpec, TypeSpecRef requestedInterfaceTypeSpec` |
| Selected member, **16 bytes** | `u32 sourceWitnessMemberRef, u32 viewOrdinal, TypeSpecRef selectedOwnerTypeSpec, u8 targetPolicy, u8 flags=0, u16 reserved=0` |

Extent is exactly **`28 + 8*viewCount + 16*memberCount`**. Counts, lengths,
offsets and reservations use nonwrapping arithmetic and existing resource
accounting; no new numerical budget is selected. Table counts remain bounded
by `00FF_FFFF`; inline counts/ordinals are `u32`, not Body-local `u16` indexes.

Row references are nonzero `u32` words in `1..00FF_FFFF`, high byte zero,
checked against their schema-selected tables. `sourceWitnessMemberRef`
selects `0029`; `viewOrdinal` instead is a **zero-based numeric index** in
this row's view array. All complete type fields use the accepted TypeSpecRef
union and position rules, including equivalent row/F2/F3 spellings where
eligible. Only `selectedOwnerTypeSpec` permits zero, and only for an originally
module-level selected MethodDef. No other zero denotes absence or deferred
evidence.

## Closed views and forwarding contract

**Rule 1** consumes a complete, checked source witness and its published
declaration/contract closure. It does not fill gaps in that witness:

1. The source root interface identifies its declared self formal, bound to
   the source witness's complete implementing type. Requested self preserves
   that declaration, closed arguments, representation, ownership, pin and
   other qualifications; only the source-permitted outer access adjustment
   is allowed. The requested root retains every ordinary type/constant
   argument. A coincidentally equal ordinary argument is not a self formal.
2. Expand the original and requested inherited interface instances **in
   parallel from the same InterfaceBase declarations**, substituting formal
   terms before closing types. The view array is exactly the unique set of
   resulting `(source instance, requested instance)` pairs, including the
   root pair. Declaration inheritance must be acyclic and normalization must
   terminate; no inheritance path or name is an identity.
3. For each pair, include exactly one member record for every source
   WitnessMember whose declaring instance is that pair's source instance.
   Its owner must be `sourceWitnessRef`; its requirement must belong to that
   exact InterfaceMethod declaration. The member key is
   `(viewOrdinal, sourceWitnessMemberRef)`. Private interface helpers are not
   requirements. Associated types/constants reuse the original witness's
   bindings keyed by the same source instance and exact declaration; no new
   binding/default row is manufactured.
4. Self substitution can **split** formerly identical base instances or
   **merge** distinct instances. Repeating one source member under different
   view pairs is therefore valid. All correspondences survive. Merged
   requested requirements must have agreeing selected mappings, associated
   bindings and complete contracts; disagreement rejects rather than choosing
   a path. Merely deduplicating by source or requested type loses obligations.
5. Each member retains its source WitnessMember's exact requirement and
   selected MethodDef/default definition. `targetPolicy=1=RetainedOwner`
   requires `selectedOwnerTypeSpec` equal to that source member's complete
   selected owner. **`2=SelectedDefaultAtRequestedOwner`** is allowed only
   for its already selected interface default, with the same default
   definition: the recorded owner must be a requested view paired with that
   source member's selected owner, and must own the selected default's
   InterfaceMethod declaration. Check its published generic constraints and
   instantiation; zero owner and an unrelated same-shaped default reject.
   It cannot bypass an authored override, select another default or expose
   a private target. Other tags reject. These records retain the producer's
   checked selection; serialization, reading and replacement cannot choose
   another policy merely because it also appears applicable.
6. Derive the requested forwarding contract from the same requirement
   declaration at its requested view. The selected target contract comes
   from its unchanged selected MethodDef at the recorded owner. Owner
   substitution precedes kind/ordinal correspondence of method formals.
   Receiver, logical parameter and result positions map identically, including
   unit positions. Inputs adapt **requested to selected**; results adapt
   **selected to requested**. Only identity and already permitted ordinary
   access attenuation are admitted. This pair of complete contracts is the
   adaptation recipe, not an extra MethodDef or executable thunk.

Check the **whole requested closure**, not only the called member. Preserve
exact init-destination types/modes, associated values/origins and relations,
receiver access, caller safety, Pure/read bounds, NoAlias, output phases,
dependencies, transfers, captures, backing, roots, invalidation and cleanup.
No inner-type rewrite, authority upgrade, boxing, result discard, new owner,
loan or operand evaluation is implied. Default failure recipes retain their
original declarations and principals. A permitted physical adapter elimination
does not erase these contracts.

A kind-4 PredicateCall remains `targetRole=2`: its dependency entry belongs
to its own footprint; its requirement MethodDef and `ownerTypeSpec` select
the **requested declaring view**, not the selected implementation. Resolve
the matching member correspondence before following its recorded target.
Multiple agreeing inherited correspondences provide one effective call,
not extra evaluations. Associated/default lookup is never rerun.

## Identity, binding and ownership

Use the existing logical dependency key:
**`(source witness identity, requested complete root interface, rule version)`**.
Source witness identity retains complete implementing/root-interface types,
selected nominal/Core authority and original binding context, not its row.
Validate requested self against the root's declared self slot; the redundant
self field cannot create a second identity. View/member records and their
complete adapted contracts are retained obligations of that identity:
conflicting records with the same resolved key are invalid, not alternatives.

The projection belongs to `ownerModuleRow`; its inline records belong only
to it. It may serve several graph footprints without sharing footprint-entry
rows. Share a projection only when its complete meanings, original expected
declarations, selected origins and retained equality obligations agree in all
using contexts. Independent expectation views are not interned merely because
they currently select one actual provider. The owner field grants no nominal
conformance, private access or privileged primitive authority.

Bind every referenced declaration under the projection's **original**
Module/formal context, following its retained Imports before comparing.
Check each original expectation and the projected obligation separately.
Replacement must preserve selected origins, policies, complete owner/types,
view/member correspondence and all relied-on contracts. Satisfying only the
unqualified witness is insufficient. Compatible body changes and physical
relocation need not change identity; weakened access/output/read/associated
guarantees cannot be repaired by rebuilding expectations from the replacement.

Mandatory consumers check framing, namespaces, ownership, complete view/member
coverage, supported substitutions/adaptations, contract correspondence and
required binding before affected use. Producers establish witness authority,
legal selection and semantic truth; consumers need not re-prove provider
bodies. A published fact is not permission to ignore missing reference,
principal, contract or evidence codecs.

## Exact selection and admission

Recommend the distinct candidate successor **B+I+G+WP1**:
**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-witness-projections-1`**.
It is the existing complete B+I+G agreement plus this table/rule and its
kind-4 integration, nothing else. This intentionally supersedes only the
standalone closure proposal's **selection literal** for the full-closure
operation. The old B+I+G agreement remains unchanged; its independent
projection gate is not silently reinterpreted.

Both standalone producer and source-free consumer, every supplied artifact
(including unused inputs), and the packed output explicitly select WP1
before schema-dependent indexing. Preserve the standalone raw-input agreement,
source surface and Module/key roots. No mixed-input fallback, old-token alias,
new header field or conversion by relabeling `(1,0)` is selected. This follows
[exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts);
a new selection is not released-schema stabilization.

Preserve G's complete catalog and exact prerequisite edges. Require **17/1**
for retained projections in their owning Modules and for each using Module
whose retained G dependency closure references one; derive **10/1** as before.
Feature 10 alone cannot admit kind 4. No new Feature is necessary for this
required target codec of the explicitly selected G successor. Conditional
scalar, layout, constant, ownership and target requirements remain with their
owners, including uncalled expectations and guarded branches. Neither 17 nor
WP1 supplies missing witness authority/default/access/evidence or runtime
dispatch support. Another Module's requirements cannot repair a missing list.

## Writers, source-free copying and packing

* Canonical writers omit unused projection rows and order retained rows by
  the logical dependency key above; ties between distinct retained contexts
  use owning Module and expected-declaration identities. Sort views by
  complete source/requested type identity, then members by view identity and
  source requirement identity. Helper interning is optional under the
  context-preservation rules above. Rebuild `viewOrdinal` after sorting;
  never normalize a target policy by choosing a cheaper thunk.
* Ordinary readers accept consistent projection/view/member permutations
  and equivalent complete-type spellings, including identical duplicate
  helper rows. Duplicate view/member keys **within a row**, conflicting
  same-identity obligations and duplicate semantic dependencies within a
  footprint remain invalid. Canonicality is a separate audit.
* Decode and remap owner Module, source witness/member rows, all complete
  TypeSpecs and their Imports/formals, original associated/declaration/value
  and selected target/contract dependencies. Rebuild source witness exclusive
  ranges through their own codecs. Rule versions, policy tags and numeric
  counts are not references; array ordinals need explicit permutation maps.
* Preserve original snapshots, owner/using contexts and expected equality
  groups before deduplication. Clone context-sensitive rows where required.
  Pack unkeyed projection/witness/member/selected-target closure in its
  original logical Modules using existing multi-Module framing and direct
  packed references. Invent no export keys, lookup roots, external row tokens
  or nominal witness proxies; preserve provider-owned Imports there.
* Rebuild graph dependency references, variable offsets/extents, directory
  counts, Module partitions and per-Module manifests together. Body/source-map
  repairs remain with G's remapper when affected. Metadata-only copying
  neither requires source/debug nor executes provider initialization/code.
  Retain actual programs when the selected execution promise requires them.

Malformed records, unsupported required rule/carrier, incompatible binding
and resource exhaustion remain distinct failures. Complete checks and
reservations precede publication; failure preserves prior output and publishes
no partial closure, false predicate, empty dependency set or replacement stub.

## Focused candidate cases

These are required cases, **not executed results**:

| Case | Required outcome |
|---|---|
| Closed outer-access projection; methodless associated-only interface | Retain root/base views, exact bindings and legal requested-to-selected attenuation; zero members only for the empty callable closure. |
| Inherited generic default and authored override | Preserve selected definition/owner/policy; requested-default instantiation succeeds only for the selected default. No override bypass or missing-default-origin inference. |
| Ordinary argument equals original self; split/merged base paths | Change only declared-self-dependent terms; keep all view pairs. Accept agreeing convergence, reject conflicting mappings/bindings and a missing unused requirement. |
| References, init outputs, associated relation and Pure bound | Reject inner-type/output covariance, readonly-result upgrade, changed associated selection or weakened mapped bound even when the motivating called member is compatible. |
| Framing and identity negatives | Reject zero/wrong-table/high-byte refs, unknown version/tag, reserved bits, bad ordinal/extent, unclosed self, wrong Module/default/member and conflicting same-key rows. Distinguish valid noncanonical permutations/duplicate helpers. |
| Relocation and source-free packing | Reorder artifacts/Modules/witnesses/TypeSpecs/members/views independently; preserve numeric tags and rebuild ordinals. Copy an unkeyed target with a provider-owned Import, two independent expected views and a shared equality group without source or invented keys. |
| Selection, requirements and failure | Reject old/mixed selections, missing owner/using 17 closure, unavailable required carrier and combined-capacity exhaustion; no partial output or fallback success. |

## Remaining boundary

Source inspection confirms the gap:
[hosted dependency validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs)
passes `witnessProjectionCount: 0`; the
[Terrum reader](../../sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum)
declines nonempty dependencies, and its
[writer](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceWriter.terrum)
still copies leaf templates. None establishes projection support.

Formal adoption and the exact WP1 selection remain unapproved decisions;
this recommendation leaves no field, target, mapping or packing policy open
within the closed codec. Existing
[associated-origin](witness-associated-type-binding-origin.md) and
[private witness](source-interface-witness-private-persistence.md) proposals
do not automatically supply Standard authority/default/principal codecs.
An obligation needing an unrepresented relation, recursive evidence edge,
open/per-value witness, richer access principal or executable dispatch remains
a genuine independent gate: refuse that use rather than invent its carrier.
This document selects none of those designs and claims no full-G milestone
or runtime acceptance.
