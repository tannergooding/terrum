# Witness associated-type binding origin

> [!IMPORTANT]
> **Unapproved proposal.** The user's missing-contract instruction permits
> this recommendation to guide provisional implementation; it does not
> approve the design. This document changes no normative specification,
> registry assignment, compiler implementation or support claim.

## Decision and scope

**Recommend a closed `u32` selection-kind enum: `1=ExplicitBinding` and
`2=DeclarationDefault`. Materialize the selected complete type in every
WitnessAssociatedType row, including inherited selections.** Retain the
assigned 20-byte row. The origin word is not a reference, bit mask,
inheritance-path identifier or instruction to select a binding later.

The [source witness contract](../specs/language/interface-witnesses.md#associated-types-and-constants)
already selects one binding for an exact declaring-interface/member
identity. Explicit associated bindings override replaceable defaults;
inheritance must resolve without path/import-order preferences.
[Logical witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain)
retain those identities and selected bindings. The
[physical row](../specs/il-metadata.md#section-kinds-and-row-layouts) and
[registry](../specs/representation/trrm-1.registry.json) assign
`u32 bindingOrigin` but neither its values nor a reference namespace.
This is a missing physical contract, not permission to accept any nonzero
word or reconstruct source selection.

This candidate fills only that gap. It adopts neither the separate
[associated-declaration identity candidate](associated-type-definition-identity.md)
nor the [Constraint kind-3 payload candidate](constraint-associated-relation-payload.md).
In particular, it does not assign DefId kind `0x20`, change
`associatedTypeDefId`, reinterpret `Constraint.payload`, add a Feature ID,
introduce source syntax or select a runtime witness carrier.

## Candidate physical contract

All additions below are **unapproved**. Words are little-endian.
WitnessAssociatedType remains required section `0x002A`, policy `0x0000`,
with this exact ordered payload:

| Offset | Existing field | Interpretation retained or proposed |
|---:|---|---|
| 0 | `u32 witnessRow` | Required table-specific InterfaceWitness row in section `0x0028`; not a DefId or a per-interface ordinal. |
| 4 | `u32 declaringInterfaceTypeSpec` | Required complete TypeSpecRef for the actual declaring interface instance in this witness's inherited closure. |
| 8 | `u32 associatedTypeDefId` | Required typed identity of the associated-type declaration directly owned by that interface's nominal definition. Its separate physical identity/Import codec remains a prerequisite, not an allocation made here. |
| 12 | `u32 valueTypeSpec` | Required complete TypeSpecRef for the already selected binding after the applicable substitution. |
| 16 | `u32 bindingOrigin` | Exactly one of the two enum words below; no payload or reference bits. |

| Word | Little-endian bytes | Candidate meaning |
|---|---|---|
| `0x0000_0001` | `01 00 00 00` | `ExplicitBinding`: the effective selection comes from an authored associated-type binding or checked explicit replacement, whether ordinary or interface-qualified, directly selected or inherited. |
| `0x0000_0002` | `02 00 00 00` | `DeclarationDefault`: the effective selection uses the `defaultTypeSpec` of the exact AssociatedTypeDef identified by this row, instantiated at this row's declaring interface instance. |

Only the complete words `1` and `2` are valid. The upper 24 bits are zero.
Zero, `3..0xFFFF_FFFF`, combined flags, section-derived words, DefIds,
Import tokens and TypeSpecRef F2/F3 words are invalid in this position.
There is no `None`, `Unknown`, `Imported`, `Inherited`, unresolved or
legacy-default form. Words `1` and `2` still mean enum tags when another
table has rows with those numbers; no row is dereferenced.

Ordinary [row and range bounds](../specs/il-metadata.md#table-stream-and-token-encoding)
remain: `1 <= witnessRow <= InterfaceWitness.count <= 0x00FF_FFFF`, with
the row word's high byte zero. The selected section extent is exactly
`20 * recordCount`, checked without overflow, with the ordinary 24-bit
record-count bound. The two type-valued fields use the existing complete
TypeSpecRef union and field-position rules, not row-only encodings.
An inline scalar/family cannot denote the declaring nominal interface;
eligible inline forms may denote the selected value.

Neither required type nor the declaration identity may be zero.
`DeclarationDefault` additionally requires the identified declaration's
`defaultTypeSpec` to be nonzero. Only that declaration field permits zero
for absence. A missing binding row cannot be synthesized from a default.
An empty witness associated range is `(0, 0)` and is complete only when its
required closure has no associated-type requirement. No zero word denotes
the unit type, an omitted argument or deferred witness evidence.

## Selection, inheritance and ownership

The **candidate binding key** is the owning witness's complete implementing
type and interface, plus the exact normalized declaring interface instance
and resolved AssociatedTypeDef identity. Declaration identity alone does
not identify an instantiated member. An inherited requirement names its
actual base declaration/interface, not the most-derived interface or a
same-named member. Resolve imported owner/member identities under their
own consumer Module contexts before claiming correspondence.

Each complete witness has exactly one binding for each associated-type key
in its required inherited closure. Each physical binding row belongs to
exactly one exclusive InterfaceWitness associated range, and `witnessRow`
agrees with that owner. Identical facts under different witness owners do
not share an exclusively owned row. This adds no global row ordering or
deduplication rule. Distinct complete interface instances and unrelated
declarations remain distinct even when their names and selected types agree.

The enum describes the **effective selection kind**, not the source
location or physical route by which it was reached:

* A selected authored binding is `ExplicitBinding`, including an inherited
  binding, a more-derived explicit replacement, or one whose type happens
  to equal the declaration default. Do not infer the tag by type equality.
* A selected declaration default is `DeclarationDefault`, including when
  reached through base-interface inheritance. Its origin is already fixed
  by the exact declaring-interface instance and declaration identity.
  A separately authored replacement is `ExplicitBinding`, even if it is
  inherited from an interface and used in the absence of a binding authored
  by the concrete implementer. It is not the original declaration's default.
* Materialize the fully substituted selected type and retain that kind.
  No source-witness row, inheritance path, alias-expansion trace or
  standalone base witness is required merely to spell this origin word.
  Where a base witness is independently retained, corresponding claims for
  the same implementing/interface identity must agree under the source
  witness rules; publication cannot conceal conflicting claims.
* Diamond paths to the same instantiated requirement share its one effective
  binding. Different surviving selections for that identity require the
  existing explicit resolution. There is no third tag for an unresolved
  conflict, and inheritance order cannot choose a value or an origin.

The producer must establish that a `DeclarationDefault` value equals the
identified default after declaring-owner substitution and
[forward normalization](../specs/language/types-and-normalization.md#forward-substitution-and-normalization).
Substitute the actual declaring interface, including self, ordinary type
arguments and constant arguments, not merely the witness's most-derived
interface. An explicit replacement may select another complete type only
if all applicable requirement/associated relations hold. Preserve nominal,
ownership and access distinctions; equal layout is insufficient.

Publication authority remains the
[implementing type's primary authority or privileged Core authority for primitives](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain).
An origin tag grants no conformance, access or export authority. Inheriting
a selection does not let an extension, adapter, consumer or packer invent a
witness for another implementing type. Preserve the actual nominal witness
origin; do not rewrite a base implementer into a derived one merely to
populate these fields. An imported interface/member stub is an expectation,
not a consumer-authored provider witness. No Import/Export target for witness
rows is added. Retained provider, owner, Module and default dependencies
still bind through their separately supported contracts.

This materialization omits historical path provenance, **not** required
semantic dependencies or original-witness identities. A retained
qualified-self projection still needs its original witness, requested types,
complete mappings and adaptations under its separate codec. This enum
cannot replace that record or erase any independently retained base,
access-principal or provider obligation.

## Generic contexts and per-value selection

The origin word carries no formal scope or substitution map.
An associated declaration default may contain its declaring interface's
formals. Check those terms in that declaration scope, and use the exact
declaring-interface arguments to instantiate the binding. An expected
import's formals correspond to the selected provider's by kind and ordinal,
not name or physical GenericParam row. A coincidental equality with self
does not make an ordinary argument a self formal.

This proposal does not admit open/template InterfaceWitness rows beyond
the [logical closed-witness contract](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain).
At a closed witness, every default/value dependency must be closed after
the applicable substitution; an out-of-scope or unbound formal is not a
closed selected type. An open generic constraint instead retains its
symbolic interface/relation obligation and in-scope terms. Neither tag
creates a closed witness, infers arguments backward or supplies a missing
template-context carrier. Any future open-witness representation must
specify that context separately; it cannot borrow a using method's formals
from a numerically matching row.

[Qualified-self projection](../specs/language/interface-witnesses.md#source-equivalent-contract-adaptation)
retains the original selected binding and origin kind, then checks the
requested relations. A `DeclarationDefault` tag is not authorization to
re-evaluate the default with the newly qualified self. Changing self cannot
retarget ordinary arguments or choose a more convenient default.
These rows describe the original nominal witness: a projection retains
that binding through its separately encoded mappings. Do not manufacture
a new nominal binding row whose declaring interface uses the requested
self while its default/value agreement refers to the original self.

[Existential generic values](../specs/representation/logical-records.md#existential-generic-views)
select their own correlated hidden type and witness evidence first; only
then is this witness's associated binding available. Two values may select
different witnesses, values and origin kinds for the same declaration.
One artifact-wide origin word or inferred hidden type cannot replace that
correlation. The enum is not a runtime dictionary, dynamic lookup service,
type-equality call or evidence slot. Property/associated-relation facts do
not become interface-witness entries through it.

## Replacement and transformation

**Candidate exact-origin rule:** a retained witness binding preserves its
binding key, normalized selected type **and origin kind**, along with every
retained contract/dependency. Switching `ExplicitBinding` and
`DeclarationDefault` is not a numeric strengthening and does not satisfy
that retained binding merely because its value is unchanged. This
conservative provenance comparison is an unapproved choice, not a claim
that source can observe an origin enum. A future relaxation would have to
define when erasing that retained distinction and its dependencies is safe.

Apply [binding and replacement](../specs/representation/binding-and-replacement.md)
to the complete selected provider/expectation graph. Declaration-default
compatibility is a separate obligation under the associated-declaration
identity proposal, not a comparison invented here. Keeping an explicit
binding's value does not waive an independently retained expected default.
Changing a historical inheritance path alone is not a change to this
enum contract when the same effective key, type, kind and all other
retained obligations survive. Names, equal row numbers, unchanged keys or
prior acceptance caches do not prove compatibility. A linker does not
rerun source selection against a replacement's current defaults.

| Transformation | Candidate preservation requirement |
|---|---|
| Packing/reordering | Map `witnessRow` through InterfaceWitness; remap both complete TypeSpecRefs and their nested dependencies; remap `associatedTypeDefId` through its separately selected declaration/Import codec. Leave `bindingOrigin` unchanged as an enum. |
| Ownership and ranges | Rebuild exclusive witness binding ranges, row owners, counts, section extents and affected typed reference/source slots. Never remap the tag through AssociatedTypeDef, InterfaceWitness, TypeSpec or Import maps. |
| Binding imported definitions | Resolve the member and owner in the correct consumer/provider contexts; retain each expected contract and stronger relied-on fact before direct binding. Materialization cannot erase a surviving Import or give one consumer another's expectation. |
| Substitution/rewriting | Carry the typed key, selected type, origin kind, authority and dependencies through binding, shared bound-tree traversal, persistence and lowering. Clone context-sensitive shared structures when necessary; equal bytes do not establish scope equivalence. |
| Trimming/specialization | Keep every declaration/default, witness/projection and provider obligation still required by the selected binding or another retained contract. Folding an associated type does not discharge replacement, hidden-value evidence or native-code dependencies. |

Canonical writers emit the selected tag and materialized value; they do not
choose a tag by smallest numeric value, type equality or absence of a local
source declaration. There is no reference-chain spelling to canonicalize.
Existing [ordinary/noncanonical rules](../specs/representation/encoding-canonicality.md)
still allow equivalent TypeSpecRef spellings and consistent row remapping;
an invalid tag is malformed, not noncanonical.

## Producer truth, consumer safety and admission

Under [reader trust](../specs/representation/reader-trust.md), distinguish:

* **Producer/source obligations:** establish truthful explicit/default and
  inherited selection, unambiguous nominal conformance, default/value
  equality, applicable relations, finite transparent expansion and all
  retained authority/dependency facts. A rewriter is a producer for its
  transformed facts; it cannot manufacture truth by changing the tag.
* **Mandatory safe consumption:** before affected use, check exact schema,
  framing/extents/counts, the closed tag, required reference forms/bounds,
  exclusive ranges and owner agreement, declared-interface/declaration
  owner correspondence, permitted formal contexts and supported binding
  dependencies. `DeclarationDefault` requires an existing nonzero,
  structurally consumable declaration default. Missing/duplicate required
  binding keys cannot be repaired by name lookup or default synthesis.
* **New binding/instantiation:** check the retained expected/provider
  relation and any newly formed substitution/default selection from the
  supported published facts. Complete-type equality is exact normalized
  identity, not assignability. An unvisited obligation, recursive expansion,
  unsupported comparison or resource cutoff is not successful comparison.
* **Semantic truth boundary:** ordinary consumption need not reconstruct
  the authored inheritance resolution, re-prove represented bodies, or
  re-establish a producer's asserted default/value equality. A structurally
  consumable but false selection/equality remains a producer violation.
  An optional audit may detect it; structural/support/binding failures
  remain mandatory refusal conditions. Normalization or comparison
  performed by the consumer must itself terminate safely, never treating
  a malformed cycle or exhaustion as success.

[Admission](../specs/representation/requirements-and-admission.md) still
requires the complete retained generic/witness/identity semantics and
target/runtime closure before affected use, not just recognized enum
bytes. No existing Feature ID is repurposed and no unregistered generic
capability is activated. Inspection may report a recognized shape with
unsupported dependencies; it cannot claim binding, lowering or execution
support. Optional metadata, manifests and private/native persistence cannot
substitute for a missing required codec.

## Alternative and adoption boundary

A typed row-reference union would be appropriate **if public lineage to a
particular inherited selection were required**. Such a design would need
distinct variants for a declaration default and a source binding, explicit
AssociatedTypeDef/WitnessAssociatedType table targets, the source row's
owning InterfaceWitness, and a complete instantiation/context rule. A
cross-artifact source would also need an admitted publication/reference
contract. It must define chain/cycle validation, remapping and replacement
of that lineage; an unqualified physical row number supplies none of them.

For this decision it adds no required selection information: the exact
default declaration is already named, and an inherited nondefault binding
is an effective checked fact published by the owning witness. The same
four bytes would add graph/context obligations without removing the need
to materialize `valueTypeSpec`. Recommend the two-tag enum and explicit
path-flattening rule instead. A third `Inherited` tag without source
identity/context would lose the explicit/default distinction; inferring
default origin from equal types would lose it too. Neither is a fallback.

If adopted, amend only these owners together; **no amendment is made here**:

| Owner | Candidate integration |
|---|---|
| [Logical witnesses](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain) | Define the two effective selection kinds and materialized inherited binding, preserving original-witness/projection and authority obligations. |
| [Physical metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) and [registry](../specs/representation/trrm-1.registry.json) | Specify offset-16 closed enum values `1`/`2`, non-reference interpretation, required/default consistency and unchanged 20-byte layout. Do not change declaration or Constraint payload codecs. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md) | Add the exact-origin comparison for retained witness bindings and preservation through remapping/materialization. |
| [Reader trust](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) and [witness conformance cases](../specs/conformance/source-interface-witnesses.md) | Record the enum versus reference distinction and add independent structural, semantic-trust and cross-feature vectors. |

Under [format versioning](../specs/representation/format-versioning.md),
this draft change may retain `(1, 0)` but requires exact out-of-band schema
agreement. Equal version bytes, table presence or plausible tag values do
not select it. Do not guess an old row-reference meaning, reinterpret zero,
or retry another codec after a failure. Any conversion selects both
schemas, decodes meaning and rebuilds typed references/ranges; byte
substitution and version-only rewrites are insufficient.

## Candidate acceptance cases

These are proposed obligations, **not executed tests or support claims**.
Use independent artifact vectors and unrelated synthetic interfaces/providers.

| Class | Case and required outcome |
|---|---|
| Positive | Authored binding with no declaration default uses `1`; with a default, it still uses `1` whether its selected type differs from or equals that default. |
| Positive | Tag `2`, present default and exact instantiated selected type retain the declaration/member identity and materialized value, including eligible value F2/F3 spellings. |
| Malformed: enum | Reject zero, `3`, `0xFFFF_FFFF`, nonzero upper bits, DefId/Import and F2/F3 words. No masking, row lookup or default fallback is permitted. |
| Namespace collision | InterfaceWitness, AssociatedTypeDef and TypeSpec each have rows 1 and 2. Origin words `1`/`2` still select only enum meanings; origin `3` rejects even when every table has row 3. |
| Malformed: extent/bounds | Reject truncated/overlong 20-byte rows, oversized counts, zero/high-byte/out-of-range witness rows, absent required tables and overflowed ranges. Exercise row 1 and `0x00FF_FFFF` with valid bounds; these bounds are not portable capacity guarantees. |
| Malformed: ownership | Reject orphaned/shared binding rows, overlapping ranges, wrong `witnessRow`, missing/duplicate required member keys, wrong declaration owner or a declaring interface outside the required closure. |
| Malformed: absence | Reject zero required declaration/value/interface references and tag `2` with an absent default; never fill an omitted binding from a present default. |
| Identity | One declaration under two constructed interface instances, and unrelated same-named declarations with equal values, remain distinct binding keys. Import/member ownership must resolve to the exact selected provider interface. |
| Inheritance | Diamond paths to one unchanged selection deduplicate; inherited explicit replacement stays `1`, inherited original declaration default stays `2`. Competing unresolved selections reject authored formation; no `Inherited` tag or list order resolves them. |
| Generics | Default depends on self, an ordinary type argument and a constant argument. Substitute the actual declaring base instance; preserve formal kind/ordinal and reject out-of-scope terms. Equality in one closed instance does not prove open equivalence. |
| Open context | Preserve an open Constraint/relation symbolically. Do not emit a fabricated closed witness, accept unbound selected types, borrow a using method's scope or claim that this enum admits template-witness persistence. |
| Projection | Changing self qualification retains the original nominal binding through the separate projection mapping. A failed requested relation rejects that projection; do not re-instantiate a default or emit an inconsistent new nominal default-binding row. |
| Per-value evidence | Two existential values select different hidden-type witnesses for one declaration. Project each selected binding independently; an artifact-wide origin/default or shared inferred hidden type is insufficient. |
| Replacement | Row permutations and alias-equivalent values preserve identity. Changed selected types/declarations or a `1`/`2` switch fail the retained candidate comparison. An unchanged path is not proof; a changed path alone is not failure when all retained facts/obligations survive. |
| Dependency binding | A direct reference to an expected stub still follows its Import. Wrong provider owner, another consumer's formal/import scope, or a changed retained declaration default cannot be rescued by the origin tag. |
| Remapping | Independently reorder witness, associated-declaration, Import, interface, type and formal rows. Remap their typed edges and rebuild ranges/slots while leaving the enum unchanged; preserve every retained expectation and projection dependency. |
| Truth boundary | Well-formed tag `2` with a falsely asserted selected type/default equality is a producer violation; ordinary reading need not re-prove it. Optional audit/source checking detects it, and a newly required incompatible comparison must not pass. |
| Termination/support | Source rejects recursive/growing transparent defaults while permitting finite nominal anchors. Consumer normalization/comparison cannot accept cycles/cutoffs as success. Unsupported declaration, witness, projection or evidence codecs refuse affected use despite recognized tags. |
| Schema | Equal `(1, 0)` without exact candidate agreement does not admit these values or any legacy interpretation; changing only version bytes is not conversion. |

## Current boundary and remaining decisions

The C# [InterfaceWitness/WitnessMember readers](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceWitnessTables.cs)
check those rows and the associated range counts/partitioning; they do not
decode WitnessAssociatedType fields or `bindingOrigin`. The accepted row
allocation and this proposal establish no associated-binding implementation,
source-free exchange or execution evidence.

Review still must approve, reject or amend the **two tags, effective-selection
materialization and exact-origin replacement policy** together. No tag,
source-row namespace or inherited substitution rule is delegated to an
implementer within this recommendation. The associated-declaration
identity/Import codec and kind-3 relation payload remain separately
unapproved decisions. Complete generic/witness capability registration,
provider/default binding, open-witness template contexts, qualified-self
projection codecs, per-value existential evidence/carriers and dispatch
ABI remain outside this slice. No source, library, runtime or target
contract is broadened by recognizing the origin enum.
