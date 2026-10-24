# Associated-relation constraint payload

> [!IMPORTANT]
> **Unapproved proposal.** This recommendation may guide provisional
> implementation under the user's task authorization; it is not normative
> approval. The accepted specifications and registry remain unchanged.
> Recognizing this payload does not establish generic, witness, execution or
> TRRM conformance.

## Decision and boundary

**Recommend interpreting `Constraint.payload` for `kind=3` as one required
table-specific row reference to `AssociatedRelation`, section `0x0036`.**
Keep the assigned 12-byte Constraint and 12-byte AssociatedRelation rows.
There is no additional tag, inline relation, definition-kind allocation,
TypeSpec form, Feature ID or runtime carrier.

The [logical-record owner](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending)
assigns kinds `1=interface instance`, `2=intrinsic property` and
`3=associated relation`, each with a nonzero kind-correct payload, but does
not specify the third payload's physical target. The
[metadata chapter](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [registry](../specs/representation/trrm-1.registry.json) already assign
AssociatedRelation's three fields. The logical-record
[dependency identity rule](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
already compares associated relations by that complete triple. The missing
decision is the reference connecting these records, not a new relation
language or record family.

This candidate completes the unspecified kind-3 payload interpretation and
supersedes treating an arbitrary nonzero word as sufficient validation. It
does not replace an accepted alternative kind-3 codec. Kinds 1 and 2,
property codes, formal domains, ownership ranges, the complete TypeSpecRef
union and all other table/DefId namespaces remain unchanged.

## Candidate physical contract

The requirements below are proposed text, not accepted schema rules.
All multibyte words are little-endian.

| Constraint offset | Existing field | Candidate kind-3 requirement |
|---:|---|---|
| 0 | `u32 ownerGenericParam` | Required GenericParam row; the Constraint belongs to that formal's exact constraint range. The formal is a type formal, not a constant formal. |
| 4 | `u16 kind` | Exactly `3`. |
| 6 | `u16 flags` | Exactly zero; no payload-mode bits are introduced. |
| 8 | `u32 payload` | Required one-based AssociatedRelation row in this artifact. |

For `n` AssociatedRelation rows, the only payload form is the unsigned word
`r` with `1 <= r <= n <= 0x00FF_FFFF`. Its high byte is reserved zero.
Zero is invalid, including when the relation table is absent or empty; it
does not mean an omitted relation, unresolved equality or default binding.
The selected section uses required policy `0x0000`, the assigned 12-byte
stride and the common checked count/extent rules.

The payload is **not** a TypeSpecRef, DefId, Import token, witness row,
associated-declaration row, Body-local index, byte offset or chained
reference. In particular, `0x3600_0001`, `0x1100_0001`,
`0x1C00_0001`, `0xF200_0008` and every `0xF3` word are invalid payloads,
not alternate spellings of row 1. Section kind `0x0036` is not a
definition/token kind. When both TypeSpec row 1 and AssociatedRelation row 1
exist, payload `1` selects only the latter; equal numeric indexes convey
no cross-table identity.

The referenced row retains its existing fields:

| AssociatedRelation offset | Assigned field | Required interpretation |
|---:|---|---|
| 0 | `u32 declaringInterfaceTypeSpec` | Nonzero complete TypeSpecRef resolving to the exact declaring interface instance under the using formal/substitution context. It may contain in-scope formals; complete does not mean closed. |
| 4 | `u32 associatedTypeDefId` | Nonzero first-class identity of the associated-type declaration directly owned by that declaring interface, not a name, ordinal, physical slot or associated constant. |
| 8 | `u32 requiredTypeSpec` | Nonzero complete TypeSpecRef for the required exact type, including all qualification, constructors and type/constant arguments. |

The two complete-type fields use the
[existing TypeSpecRef union](../specs/il-metadata.md#table-stream-and-token-encoding),
including eligible `0xF2`/`0xF3` forms. Those forms are not admitted in
Constraint.payload merely because they are admitted inside the relation.
An inline scalar/family still cannot serve as the declaring interface.
Zero, unassigned type codes, reserved qualifier bits, invalid field-position
qualification and malformed nested references remain invalid.

This proposal does **not** complete `associatedTypeDefId`'s independent
first-class identity/Import/Export codec. The
[witness record owner](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain)
still identifies DefId kind `0x20` as an unassigned candidate, and the
registry's current Import/Export target sets do not admit AssociatedTypeDef.
Neither `0x20`, section kind `0x0027`, a raw AssociatedTypeDef row nor an
Import token becomes a permitted substitute through this proposal.
Affected use remains unsupported until the selected schema supplies that
codec and the consumer supports it.

### Ownership and resolution context

The subject is the type formal selected by Constraint.ownerGenericParam.
Follow its existing TypeDef/MethodDef owner to obtain the using Module and
lexical formal context. Do not infer the subject from the required type or
from whichever witness happens to occupy the same numeric row.

AssociatedRelation is a structural signature, not another declaration,
owner, value or witness. It has no Module cursor or independent subject.
A row may be shared when its triple and referenced formals/imports have the
same meaning in every using context. Two different subject formals can
therefore share a subject-independent triple without sharing their
Constraint identities or evidence. Clone/remap the relation when contexts
give any referenced component different meaning. Separate equivalent rows
are permitted; this amendment adds no global ordering or deduplication rule.

## Meaning, validation and trust

The existing accepted identity clause
`where T.via<I>.Item is U` supplies the source meaning; this is not new
syntax or an implemented-support example. Under the
[witness rules](../specs/language/interface-witnesses.md#associated-types-and-constants),
resolve the selected witness for the subject and exact interface, select
the identified associated declaration, substitute its binding and compare
it with the required type by exact
[normalized complete identity](../specs/language/types-and-normalization.md#forward-substitution-and-normalization).
This is neither assignability nor equality of layout or spelling, and it
does not infer an otherwise unknown subject or argument backward.
At a generic declaration it restricts admitted substitutions; it does not
assert that every type in the formal's ownership/nullability domain
satisfies the relation.

Kind 3 does not create conformance or supply a closed witness reference.
Witness availability remains governed by the declaration's ordinary
conformance evidence, including inherited interface closure. An inherited
member names its actual declaring interface instance and declaration, not
a new member of the most-derived interface. Bind an interface's explicit
self formal according to the witness contract; do not retarget an ordinary
argument merely because one instantiation equals self.
At an open generic definition, retain the symbolic relation rather than
requiring a closed witness row.

Candidate obligations are deliberately separated:

* **Producer/source checking:** publish well-formed scoped references and
  truthful declarations, selected bindings and relied-on equality facts.
  Establish constraint satisfaction for authored uses after ordinary
  argument selection and substitution. Honor explicit/default/inherited
  binding selection, reject ambiguity and nonfinite transparent expansion,
  and preserve complete access, ownership and constant-domain distinctions.
* **Mandatory consumer safe consumption:** before affected use, validate
  the selected exact schema, section framing/counts/extents, owner/range
  correspondence, kind/zero flags, payload's reserved high byte and bound
  against AssociatedRelation, and the selected row's nested reference
  namespaces, permitted forms, formal scope and declaration-owner
  correspondence. Resolve required imports and check supported binding and
  replacement contracts. A kind-3 row cannot be accepted by checking only
  the TypeSpec count or only `payload != 0`.
* **New binding/instantiation:** a consumer selecting arguments or creating
  a new binding checks the declared relation using supported published
  facts under that substitution. Missing evidence, incompatible bindings
  or unsupported comparison cannot count as successful satisfaction.
  This is not a requirement to re-prove a producer's represented bodies.
* **Trust/support boundary:** follow
  [consumer verification](../specs/representation/reader-trust.md) and
  [admission](../specs/representation/requirements-and-admission.md).
  Reject malformed selected structures and refuse unsupported required
  identity, generic, witness or relation semantics before affected use.
  Do not silently ignore the constraint, downgrade it to a property,
  reconstruct it from private bodies or invoke a provider to discover it.
  Consumers may rely on structurally consumable producer-published semantic
  facts without re-proving their truth; false facts remain producer
  violations. Any deeper semantic audit is optional and separately scoped.

An inspector may display a recognized row while explicitly reporting its
unsupported dependencies. Numeric decoding alone cannot authorize binding,
lowering or execution. No new diagnostic wording or verification-pass
organization is prescribed.

## Preservation across compiler and dependency boundaries

| Boundary | Candidate obligation |
|---|---|
| Binding and replacement | Retain the subject formal's owner/ordinal, exact declaring interface, associated declaration and required complete type. Apply the existing [directed satisfaction rules](../specs/representation/binding-and-replacement.md); a replacement cannot strengthen caller constraints or change a retained associated binding merely because names/layout agree. Preserve any stronger relation actually relied upon by specialized code. |
| Generic substitution | Substitute complete owner arguments first, then corresponding method formals by kind/ordinal. Substitute both relation TypeSpecRefs, including nested constant arguments, and the interface context of the declaration identity. Normalize forward and validate formation/satisfaction without argument permutation, qualifier loss or default reselection. Remain symbolic where permitted; unavailable execution evidence is not fabricated. |
| Bound trees and rewriting | Carry a typed relation and its Constraint owner/evidence through shared traversal and replacement. Copying, inlining or specializing a node must not retain an obsolete formal row or detach the relation from the subject. Intern only context-equivalent triples; retain distinct owner-qualified Constraint identities. |
| Persistence, packing and dependency remapping | Decode before rewriting. Map Constraint.ownerGenericParam through the GenericParam map and payload through the AssociatedRelation map, not the TypeSpec map. Remap the relation's two TypeSpecRefs, its associated-declaration identity and all nested formal/import/constant dependencies through their own namespaces and contexts. Preserve required import expectations when replacing proxies with selected definitions; clone shared rows when necessary. Rebuild affected ranges, counts, section extents and any references/slots naming moved rows. |
| Dependency identity | Preserve the existing semantic tuple: Constraint identity is generic declaration identity, formal ordinal, kind and typed payload identity. For kind 3 the payload identity is declaring interface, resolved associated declaration and required complete type. Row permutations and equivalent TypeSpecRef spellings do not change it. Retain the logical owner's finite MethodDef comparator rule; do not introduce recursively owner-qualified sort keys or sort by raw row/DefId bits. |
| Verification and lowering | Verify supported structure, bindings and retained relation context before affected use. Open generics retain their declared obligation; a closed specialization may use checked/published satisfaction without erasing surviving replacement or dependency obligations. No new dispatch, dictionary, witness-selection algorithm, runtime equality check or native ABI follows from this word. |

Source syntax, library conformance authority, targets and runtime behavior
are unchanged. Associated equality does not confer Copy, write authority,
initialization, pinning, ownership transfer, cleanup or native-call safety.
It does not turn property/associated facts into interface-witness slots for
an existential value; those separate carriers and admission gates remain.
All handling is identity/contract-based, with no sample or API recognizers.

## Alternative and integration boundary

Reusing the assigned relation row is the smallest coherent amendment.
Inlining the three-word relation in a redesigned 20-byte fixed Constraint
could remove an indirection and save space for unshared kind-3-only tables,
but would add eight bytes to every kind-1/2 row and duplicate shared triples.
A variable-row redesign would instead add indexing/framing. Neither improves
the ownership or equality contract needed here, so this proposal does not
develop those redesigns or retain a second codec. Existing assignments are
not immutable; the recommendation is based on this decision's needs.

If adopted, integrate only these owner amendments:

| Governing owner | Proposed amendment |
|---|---|
| [Logical records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) | Specify the nonzero AssociatedRelation row target for kind 3, its implicit subject/context and the unchanged semantic identity triple. |
| [Physical metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Add the per-kind reference rule and relation context/validation rules; preserve both row strides and the complete-type versus row-only distinction. |
| [Machine registry](../specs/representation/trrm-1.registry.json) | Record kind 3's typed target `AssociatedRelation/0x0036` and standard row bounds/reserved high byte alongside the existing constraint-kind domain. Do not change sections, DefId/Import/Export target sets or Feature assignments. |
| [Reference verification](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) and [representation cases](../specs/conformance/il-reader-trust.md) | Add the schema-owned Constraint-to-AssociatedRelation edge and independent malformed, remapping and support cases below. |

Under [format versioning](../specs/representation/format-versioning.md),
the amended draft requires exact out-of-band schema agreement even if its
format pair remains `(1, 0)`. The word cannot be guessed from its value or
retried as a type, old opaque payload or token after row validation fails.
No historical reader, compatibility alias, version-only conversion or
automatic migration is proposed. The separate
[scalar frozen-value proposal](scalar-frozen-value-registration.md) neither
approves nor supplies this generic/associated identity contract.

## Candidate acceptance cases

These are proposed checks, not tests run or claims of current conformance.
Use unrelated synthetic declarations/providers and independent small
artifact vectors, not only a compiler writer/reader round trip.
Semantic producer failures below are distinct from mandatory structural
consumer rejection.

| Class | Case | Candidate result |
|---|---|---|
| Positive | Kind 3 names relation row 1, whose exact selected binding and required type agree | Decode the row; establish satisfaction using the selected supported evidence, not a witness encoded in payload. |
| Positive | Required `int32` uses `0xF200_0008` inside the relation, or an equivalent TypeSpec row | Same complete required identity; respect existing [canonical writer rules](../specs/representation/encoding-canonicality.md#4-body-dependency-maps-and-typespecref-values). The inline word remains invalid as Constraint.payload. |
| Positive | An eligible complete family type uses F3 inside requiredTypeSpec | Preserve the full family/argument/qualifiers; do not reinterpret it as a relation-row token. |
| Positive | Two type formals share one subject-independent triple; equivalent triples also occupy separate rows | Both representations preserve distinct owner-qualified obligations; sharing is not shared witness evidence. |
| Negative: framing/reference | Payload is zero, the relation table is absent/empty, selected row exceeds its count, or a selected relation row is truncated | Reject before affected use, even if a same-numbered TypeSpec exists. |
| Negative: reserved/namespace | Payload has any nonzero high byte, including section/DefId/Import/F2/F3 lookalikes; flags are nonzero; kind is zero/unknown | Reject, with no masking, fallback or alternate decoding. |
| Negative: owner | Constraint owner disagrees with its formal's range, is out of bounds, or is a constant formal | Reject the structural owner/range violation. |
| Negative: nested structure | A required relation field is zero; TypeSpecRef is malformed; a formal is out of scope; declaration identity has the wrong supported kind/owner | Reject the selected invalid structure/reference; do not resolve by name or equal ordinal. |
| Negative: binding | A required import is unresolved/incompatible, or a same-named member from an unrelated interface is substituted | Refuse the incompatible binding; distinct declaration identities do not merge. |
| Negative: semantic | Required and bound types differ by signedness, nominal wrapper, optionality or inner/outer qualification despite equal size | Producer/source or new-instantiation satisfaction fails; representation/assignability cannot establish equality. |
| Negative: semantic | No selected witness, conflicting inherited defaults, or recursive transparent associated binding | Reject authored formation/selection under existing witness rules; preserve independent consumer structural/trust boundaries. |
| Boundary | Payload `0x00FF_FFFF` with a sufficiently large valid table, versus `0x0100_0000` or `0xFFFF_FFFF` | First is in range; latter words always reject. Exercise checked count/extent arithmetic without treating a count limit as permission to wrap allocation or slicing. |
| Boundary | TypeSpec row 1 exists without AssociatedRelation row 1, and then both exist | Payload 1 fails in the first artifact and selects AssociatedRelation 1 in the second; numeric coincidence is not a namespace detector. |
| Cross-feature: substitution | Relation contains enclosing type, method and nested type/constant arguments, with renamed formals or coincident row indexes | Owner substitution then kind/ordinal mapping preserves the same relation; wrong formal mapping cannot pass by name/index coincidence. |
| Cross-feature: inheritance | A derived-interface requirement reaches the same closed base declaration through a diamond, versus different constructed base instances | Preserve one exact declaring identity for repeated equivalent paths; do not merge distinct instances or choose a default by path order. |
| Cross-feature: persistence | Permute GenericParam, Constraint, AssociatedRelation, TypeSpec and declaration/import rows independently during packing | Rebuild all typed references/ranges; semantic identity and dependency ordering/duplicate decisions are unchanged. Split context-dependent shared rows. |
| Cross-feature: replacement | Provider retains versus changes a relied-on associated binding; another provider has equal names/layout | Apply complete directed compatibility and retained stronger dependencies; equal names/layout do not permit retargeting. |
| Cross-feature: trust | Structurally valid producer metadata falsely asserts a relied-on equality | Producer violates truth obligations; ordinary consumption need not detect the lie by body re-proof. An optional audit reports only its checked scope. |
| Cross-feature: support | Row is structurally recognized but associated-declaration identity, generic evidence or required capability is unavailable | Inspector may report recognition; affected binding/lowering/execution is refused as unsupported. Kind-3 framing does not close those gates. |
| Cross-feature: ownership | Relation occurs beside Unique/Write/Blittable constraints or an existential hidden argument | Preserve each fact and its own evidence/ownership dependencies; equality neither creates permissions nor supplies an existential witness carrier. |
| Cross-feature: schema | Same `(1, 0)` bytes are supplied without agreement on this amended draft, or an old arbitrary nonzero payload is encountered | Do not infer this codec or recover by trying multiple interpretations. Select the exact source schema explicitly before any meaning-preserving conversion. |

## Current implementation and remaining risks

The active C#
[TrRmConstraintTableReader](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstraintTableReader.cs)
and [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstraintTableWriter.cs)
now consume/emit the 12-byte row. The reader checks owner correspondence,
zero flags and nonzero payload; kind 1 is bounded by the TypeSpec row count,
kind 2 by property codes `1..3`, while kind 3 currently has **only the
nonzero payload check**. It receives no AssociatedRelation table/count.
The writer invokes that reader, so it inherits the same gap.
This bounded prototype does not implement the candidate or establish
accepted-contract conformance; kind 1's complete TypeSpecRef handling is
also separate unfinished work.

The [structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs)
currently constructs that reader from the formal and TypeSpec tables, not a
decoded AssociatedRelation table. The existing
[reader fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericParamTableReaderTests.cs)
are not evidence of associated-relation binding or execution. Implementation
and qualification remain under the separate
[CG-001](../implementation/conformance-gaps.md#cg-001) and
[CG-006](../implementation/conformance-gaps.md#cg-006) inventories; this
document changes neither their status nor their acceptance gates.

The payload choice is concrete; review still needs to approve this candidate.
The consequential remaining risks are the independently incomplete
AssociatedTypeDef identity/Import/Export codec, generic/witness capability
registration and evidence, and context-sensitive substitution/remapping.
Those prerequisites can block semantic use even after row-bound validation
is implemented. This **Unapproved proposal** does not choose their designs
or authorize treating them as complete.
