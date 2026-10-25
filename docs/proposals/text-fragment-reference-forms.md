# Generated text-fragment reference forms

> [!IMPORTANT]
> **Unapproved proposal.** These reference forms and additional validity rules
> are candidates for provisional implementation under the user's task
> authorization, not accepted schema assignments. This document changes no
> specification, registry, compiler, capability registration or support claim.

## Decision and evidence

**Recommend ordinary artifact-local typed row references for every reference
field of `TextFragmentShape`: TextContentShape for its owner, TextCaptureShape
for its three captures, InterfaceWitness or Constraint for mutually exclusive
formatter evidence, and MethodDef for the formatter and writer requirements.**
Imported methods use their existing consumer-owned expected MethodDef rows;
these fields do not gain a DefId/Import-token union.

The [specification index](../specs/README.md#semantic-interchange) distinguishes
accepted contracts, physical allocations and implementation evidence. The
[logical construction records](../specs/representation/logical-records.md#builder-type-recipes-and-construction-handlers-encoding-pending)
approve the generated-text row shapes, capture modes, fragment kinds and
module-local logical shape identity, but explicitly leave typed-reference
encodings gated. The [registry](../specs/representation/trrm-1.registry.json)
assigns the 20-, 16- and 40-byte rows in sections `0x002D..0x002F`.
Neither that allocation nor `kind=0/1` determines these missing reference
namespaces.

This candidate completes that physical edge contract and its immediate
ownership rules. It preserves the approved layouts, literal/formatted tags,
copy/transfer modes, [TextTarget formation](../specs/language/construction-handlers.md#typed-eager-content)
and [standard text operations](../specs/language/construction-handlers.md#standard-text-operations).
It replaces no accepted alternative reference codec and does not assign
WitnessProjection, a general witness-reference union, generic-call encoding,
new conformance authority, Feature ID, Body operation or runtime carrier.

The separate [associated-relation payload](constraint-associated-relation-payload.md)
and [associated-type identity](associated-type-definition-identity.md)
proposals illustrate distinct edge and definition-identity decisions; both
remain unapproved. Their proposed encodings are not prerequisites for a
fragment whose retained closure uses neither feature, nor permission to
invent adjacent witness or constraint codecs.

## Candidate wire contract

All rules below are **proposed**, including rules beyond the assigned row
layout. Multibyte fields are little-endian. Preserve the 40-byte stride:

| Offset | Field | Proposed namespace or value |
|---:|---|---|
| 0 | `u32 shapeRow` | Required TextContentShape row, section `0x002D` |
| 4 | `u32 ordinal` | Zero-based fragment position in that shape's fragment range |
| 8 | `u8 kind` | Exactly `0=literal segment` or `1=formatted value` |
| 9 | `u8 flags` | Zero |
| 10 | `u16 reserved` | Zero |
| 12 | `u32 valueCapture` | TextCaptureShape row, section `0x002E`; conditional below |
| 16 | `u32 alignmentCapture` | TextCaptureShape row; conditional below |
| 20 | `u32 formatCapture` | TextCaptureShape row; conditional below |
| 24 | `u32 formatterWitness` | InterfaceWitness row, section `0x0028`; conditional below |
| 28 | `u32 formatterConstraint` | Constraint row, section `0x001A`; conditional below |
| 32 | `u32 formatterMethodDef` | MethodDef row, section `0x0016`; conditional below |
| 36 | `u32 writerRequirementMethodDef` | Required MethodDef row in both kinds |

Each nonzero reference is the unsigned word `r`, with
`1 <= r <= targetTable.count <= 0x00FF_FFFF`; its high byte is reserved zero.
“Local” means local to the artifact, **not** relative to a Module, shape,
range start or dependency artifact. A capture word is an actual
TextCaptureShape row, not its zero-based ordinal plus one.
`ordinal` is an integer position, not a reference.

There is only this row spelling. For example, MethodDef row 1 is
`0x0000_0001`, not `0x1600_0001` or `0x1C00_0001`. Section-derived words
such as `0x2800_0001` and `0x2E00_0001`, TypeSpecRef F2/F3 words,
Body-map indexes and `0xFF` escapes are not alternatives. Equal low bits in
another table do not change a field's target; do not mask away a high byte
or retry another namespace after validation fails.

### Conditional fields

| Fragment form | Three capture fields | `formatterWitness` | `formatterConstraint` | `formatterMethodDef` | `writerRequirementMethodDef` |
|---|---|---|---|---|---|
| Literal, `kind=0` | All zero | Zero | Zero | Zero | Selected Core ITextWriter.TryWrite requirement |
| Formatted, closed evidence, `kind=1` | All nonzero | Nonzero | Zero | Selected Core IFormattable.TryFormat requirement | Selected Core ITextWriter.TryWriteFormatted requirement |
| Formatted, open evidence, `kind=1` | All nonzero | Zero | Nonzero | Same formatter requirement role | Same formatted-writer requirement role |

These are the complete alternatives. A formatted row has exactly one evidence
field, not both and not neither. A literal's six capture/formatter fields
are zero even when its text is nonempty; its writer requirement remains
nonzero even when its text is empty. The `?` in the existing layout means
conditional absence by fragment/evidence form, not arbitrary optionality.

Omitted alignment and format still have nonzero logical capture references
with effective zero and empty instance data. They do not select an absent
capture, another overload, a default witness or a second source evaluation.
Literal bytes and capture values are not stored in this row. Empty literal
nodes remain present, although the accepted source graph makes no writer
call for their empty data. This does not erase an explicit user call with
empty text.

### Shapes, ranges and counts

Use the existing companion layouts without new owner or environment fields:

* TextContentShape's `generatedTypeDef` is a required typed TypeDef row.
  One shape describes one originating generated TypeDef, with at most one
  shape row for that declaration. It is not attached to a consumer's
  imported TypeDef expectation as a newly authored generated type.
* `captureFirst/captureCount` selects TextCaptureShape and
  `fragmentFirst/fragmentCount` selects TextFragmentShape. Empty ranges
  are exactly `(0,0)`. A nonempty range starts at least at 1 and its
  checked last row is within the selected table's actual count.
  These are ranges, not Module partition cursors.
* Every emitted capture/fragment belongs to exactly one matching shape
  range. Child ranges cannot overlap or leave orphan children; each child's
  `shapeRow` equals that owner. Each `ordinal` equals its zero-based position
  in its owner's range. Equal children of different shapes are not shared
  by overlapping ranges.
* A capture's `typeSpec` is the existing complete
  [TypeSpecRef union](../specs/il-metadata.md#table-stream-and-token-encoding),
  not another row-only word. Its mode is `1=copy` or `2=ownership transfer`,
  and its flags/reserved fields are zero. Neither mode changes reference,
  pointer or access-qualifier placement or carries backing provenance.
* For the standard interpolation graph with `H` holes, retain `2*H+1`
  alternating literal/formatted fragments, starting and ending with a
  literal, including empty segments. Retain `3*H` logical captures in
  hole order, each triple ordered value, alignment, format. Hole `i`
  uses capture rows `captureFirst+3*i`, `+1`, `+2`. No hole reuses another
  hole's captures, and no extra capture role is introduced here.
  Alignment has exact normalized `int32` type and format has the resolved
  validated `TextSpan` type contract; the value keeps its complete
  established type. Acquisition modes must be truthful under their ordinary
  source contracts.

In particular, a no-hole shape has no captures and exactly one literal
fragment, not an empty fragment range. The logical capture counts do not
require three physical stored fields per hole: static data and permitted
lowering optimizations still obey the accepted shape/evaluation contract.
No literal-data capture or physical layout rule is added.

Each present semantic section keeps required policy `0x0000`; its byte
length is exactly count times the assigned stride. Absent tables have zero
rows and empty tables are omitted. Check all range endpoints, `3*H`,
`2*H+1`, products and extents without wraparound before indexing/allocation.
The per-table 24-bit count and artifact `u32` extent bounds both apply;
they are representational limits, not portable resource guarantees.

## Formatter evidence and requirement identity

The two method fields name **interface requirements**, never the selected
private implementation, inherited default body, generated thunk, vtable slot
or function-pointer contract anchor. Their owners and signatures must bind
to the resolved Core identities in
[standard text operations](../specs/language/construction-handlers.md#standard-text-operations).
Each requirement is a directly declared instance MethodDef represented in
its declaring InterfaceDef's InterfaceMethod range, with its full generic
signature and contracts. A same-named interface or same-shaped method is
insufficient.
The current Core interfaces themselves have no interface type arguments,
so their resolved owner and MethodDef identify these declaring instances.
This bounded contract is not a general codec for arbitrary constructed
interface-member references.

For closed evidence, the implementing/interface terms must be closed under
the represented substitution; a complete TypeSpec alone need not be closed.
The selected InterfaceWitness must retain the exact complete value-capture
type as implementing type and supply the selected
IFormattable requirement through its published nominal interface closure.
An inherited requirement keeps its actual declaring interface instance and
declaration. Its unique applicable WitnessMember mapping identifies that
same requirement and its selected target/default; the fragment does not
perform a fresh method lookup. Ordinary nominal inheritance is permitted,
not an implicit access-qualified self projection.

For open evidence, the selected Constraint must be a `kind=1` interface
instance constraint, with zero flags and a valid nonzero kind-correct payload,
whose interface closure supplies that same formatter requirement.
`ownerGenericParam` must identify the captured type formal, not merely a
formal with equal name, ordinal or one equal closed substitution.
The subject is a type formal, not a constant formal. A property or
associated-relation constraint is not formatter evidence, even if it is
true for the captured type.

**Candidate scope rule:** because TextContentShape has no lexical MethodDef
or closure-environment reference, open evidence here must belong to the
generated TypeDef's own published formal environment. Its capture type
names that formal, interpreted under the ordinary declared substitution.
An additional constructor or qualification does not automatically inherit
the underlying formal's evidence.
The row cannot reach into an originating method's unrelated formal scope
or implicitly lift/re-own its Constraint. Serialization needing a method
environment, open constructed witness, associated projection or per-value
existential evidence remains gated on its independently complete mapping/
evidence representation. This bounds this physical form, not the accepted
source language; it does not approve a generic lifting policy.

An open Constraint records an obligation, not a concrete witness or a
runtime dictionary slot. New instantiation must supply the ordinary
supported conformance evidence. This proposal chooses only the fragment's
Constraint-row edge; it does not complete the generic feature, all
Constraint payload codecs or its execution representation. Likewise it
chooses the InterfaceWitness-row edge, not the unfinished witness member/
origin/reference codecs. Selected use must support those dependencies.

No field accepts a WitnessProjection row. The
[projection contract](../specs/language/interface-witnesses.md#publication-and-independent-use)
retains an origin, requested self/interface, adaptations and rule version;
changing an implementing TypeSpec or treating a projection as an ordinary
InterfaceWitness would erase those obligations. A required projection cannot
be hidden in either evidence field, optional data, a private formatter body
or an invented tag. A later proposal must explicitly complete its carrier
and revise the fragment reference form if needed.

At a reached formatted operation, the current writer supplies the ordinary
writer evidence. The writer requirement's `TValue` is the exact capture type;
the formatter requirement's `TWriter` is that reached writer type.
Map generic method formals by kind and ordinal after owner substitution,
with their complete constraints and contracts, as required by
[nominal witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity).
There is no closed writer selected at content formation and no implicit
dereference, boxing, qualifier loss or argument permutation.

## Module, import and replacement context

The originating Module is obtained from the generated TypeDef's Module
partition, not a source path, generated name or fragment-table adjacency.
An imported generated type retains the provider's identity and shape
dependency; copying its descriptor into a consumer-owned expected TypeDef
must not change the origin. Packing may retain the provider Module and
its shape in the destination artifact.

All reference words select rows in the artifact containing the shape.
An external method is represented by an ordinary bodyless expected MethodDef
in the using Module, with its existing Import binding to the selected
ModuleRef/export key. The method's expected owner TypeDef binds independently;
the selected provider member must belong to that exact selected provider
interface. Importing an interface alone does not select a member by name or
range position. Naming the raw expected MethodDef row does not bypass its
Import or grant private access.

A source producer uses its own expected declarations for cross-module
requirements. A merger may instead write a raw row pointing directly to
the resolved provider MethodDef in the packed artifact after satisfying
and preserving the original expectation. This is still the one typed-row
form, not a second token codec or authorization to use arbitrary foreign rows.
No reference word directly addresses a row in another artifact.

InterfaceWitness has no independent Module cursor or new Import/Export kind.
An external witness must be retained through the supported witness contract,
with its exact implementing/interface identities, original conformance
authority, selected members and imported dependencies. A local description
does not author a new conformance for an imported type. Its structural rows
can be shared only when their complete meaning is unchanged in every using
shape's Module/formal context; otherwise clone/remap them. An unavailable
required external witness codec blocks affected use.

The shape key remains the accepted originating Module, ordered normalized
complete capture types/modes, ordered fragment structure and exact selected
formatter/writer requirements and evidence. Compare resolved witness identity
or owner-qualified formal Constraint identity and retained obligations, not
their physical row numbers. Literal bytes, captured values, zero/empty
defaults, source coordinates, parameter ordinals, consumer/writer instances
and generated names do not distinguish shapes. Equal closed shapes in one
Module share a logical generated type, not captured data; equal shapes in
different Modules do not merge. No additional physical interning table or
cross-module nominal alias is created.

Apply [directed binding and replacement](../specs/representation/binding-and-replacement.md)
to the complete retained graph: exact requirement owners, signatures,
formals, selected evidence, access, safety/effects, output/failure,
ownership/backing, cleanup and applicable target/runtime requirements.
A same-named replacement, equal output or unchanged layout does not suffice.
Preserve stronger facts actually used by specialization/devirtualization.
An empty segment, omitted format/alignment or eliminated machine call
does not by itself discharge its retained shape, witness or binding edge.
Trimming may remove only genuinely unretained or discharged dependencies;
each retained Module keeps its own manifest and selected revision.

## Production, consumption and runtime boundaries

Follow [consumer verification](../specs/representation/reader-trust.md);
different obligations must not be collapsed into “the row validates.”

| Boundary | Candidate obligation |
|---|---|
| Source producer and writer | Check authored formation, exact operation/evidence selection, types/domains, once-only eager capture, defaults, copy/transfer, access, backing, lifetime and cleanup. Publish valid rows and truthful shape/Body facts, including order and actual provenance. |
| Mandatory structural safe consumption | Before affected use, validate exact schema, section/count/extent limits, row namespaces/high bytes, conditional zeros, ownership/exclusive ranges, contiguous ordinals, graph/triple correspondence, capture TypeSpecRefs, constraint kind/subject scope and supported requirement/witness reference structures. Check required support and selected import/binding relationships. Do not accept arbitrary nonzero words. |
| New binding or instantiation | Resolve the exact declarations and full interface instances, correspond owner/method formals, check declared contracts and required conformance using supported published facts, and retain their dependencies. Unsupported comparison, missing evidence or an incompatible provider cannot count as success. |
| Producer truth versus audit | Consumers need not reconstruct source, re-prove capture order, body effects/ownership flow or external provider truth. Those are producer obligations; optional semantic audits are separate. A structurally consumable but false published fact is a producer violation, not a required consumer semantic-proof pass. |
| Lowering and execution | Require the actual supported Body/call, generic/witness dispatch, value, dependency, cleanup, failure, target and runtime carriers. A decoded shape neither supplies captured data nor proves an executable formatter. Runtime evidence is separate from parsing, binding and emission evidence. |

Mandatory checks cover the selected closure, not a preliminary semantic
scan of unrelated Bodies. Whole-artifact producer validity still applies.

The accepted runtime behavior is unchanged: eager capture precedes the
ordinary call, drain preserves source order and the current writer, each
formatted operation invokes its value formatter at most once, and normal
false/exception stops later fragments without replay or rollback.
Unicode-scalar alignment, including `int32` minimum's mathematical magnitude,
validated TextSpan format, empty-only outer format and declared refusal/
failure behavior remain owned by construction-handlers. No culture,
stringification, allocation/staging ABI or native text layout is supplied.
Reference/pointer/unique captures retain their real safety, loan, backing,
retention and async-completion obligations; this metadata grants none.

### Current implementation and status

The standard C# `TrRmTextFragmentShapeTableReader` and
`TrRmTextFragmentShapeTableWriter` are committed, together with the standard
structural-validator integration that invokes the reader. This is a
user-authorized provisional implementation of this **unapproved candidate**.
Its structural checks cover:

* Kind `0` literal versus kind `1` formatted field presence.
* Distinct, same-shape TextCaptureShape row references and their bounds.
* MethodDef row bounds and locality to the generated TypeDef's Module.
* Direct InterfaceWitness row bounds.
* Kind-1 Constraint evidence owned by a type formal of the generated TypeDef.

The raw MethodDef rows may name imported expected stubs, whose Import
structures are separately validated.

Focused Release tests passed **4/4**; the combined active TRRM/metadata/CLI
tests passed **1,729/1,729**. The normal analyzer build still reports
**758 repository-wide diagnostics**, with none in the new TextFragmentShape files.
These results do not establish a clean repository-wide analyzer checkpoint.

Exact formatter/witness/interface compatibility, semantic import/replacement
binding, remapping/packing, producer equivalence, runtime/source construction
and complete capability closure remain incomplete. General generic evidence
and WitnessProjection remain separately gated. The structural implementation does not
establish generated text-graph reconstruction, source-free execution or
runtime support. The [current inventory](../implementation.md#persistent-and-self-hosted-components)
and [construction gate](../implementation/conformance-gaps.md#cg-007) retain
their separate roles. This proposal remains unapproved; normative owners
are unchanged, and no milestone completion is claimed.

## Remapping and packing

Re-encoding must decode the selected schema, not substitute matching words:

| Component | Required remapping/preservation |
|---|---|
| Parent and children | Map `generatedTypeDef` through TypeDef and preserve its originating Module. Map both children's `shapeRow` through TextContentShape. Rebuild exclusive child ranges, counts and ordinals after ordering; keep empty literal nodes. |
| Capture edges | Map the three fragment references through TextCaptureShape, not TypeSpec, GenericParam or an ordinal map. Rebuild the per-hole triple relation. Remap capture TypeSpecRefs through their row/F2/F3 forms, nested arguments, formals and constants without erasing qualification or acquisition mode. |
| Formatter evidence | Map the selected word through InterfaceWitness or Constraint according to its field. Preserve the other's zero. Remap witness types, child mappings/ranges and selected declarations through their own supported codecs; remap Constraint owner, its formal range and typed payload in the represented context. Do not translate open evidence into a guessed closed witness. |
| Method requirements | Map both MethodDef words through MethodDef. Retain/remap signatures, owner interfaces, generic constraints, Import expected definitions, ModuleRefs/keys and every independently retained expectation. Clone shared contextual rows rather than rebinding another Module's uses in place. |
| Instance data and operations | Preserve the association of literal instance data and logical capture slots with their construction/materialization and drain operations. Rebuild any affected Body maps/records, indexes, offsets, section extents and retained source/debug reference slots under their own schemas. Shape rows are not a value or source-data codec. |
| Closure and identity | Retain required witness/member, generic, ownership, backing, cleanup, code/load, target and Module-manifest dependencies even when storage or machine calls are optimized away. Do not merge originating nominal types by layout or take a maximum of Modules' semantic editions/revisions. |

## Alternatives and admission implications

| Alternative | Assessment |
|---|---|
| MethodDef DefId/Import union in the two method fields | Explicit import tokens would align with Body MethodMap spelling, but add reference alternatives without reducing these four-byte fields. Existing expected MethodDef stubs already carry complete imports. Recommend row-only fields; no raw-row/token aliases or heuristic reader. |
| Shape-relative one-based capture slots | Packing could preserve slot numbers without rewriting these words, but would introduce a different implicit-base namespace next to ordinary typed rows. Artifact-wide capture rows plus checked same-shape membership reuse existing reference rules. Neither interpretation follows from the approved “one-based captures” wording alone. |
| One tagged evidence reference replacing the two optional fields | Could provide a coherent future Constraint/InterfaceWitness/WitnessProjection union, potentially at the same total width. It needs an explicit allocation and complete projection/evidence rules. Do not reserve or implement a guessed projection tag here; revisit the row deliberately if that broader codec is selected. |
| Redesign fragments as generic-call records or add declaring-interface/substitution fields | Materially useful for arbitrary constructed requirements or richer open evidence, but requires their still-missing exact generic/member/carrier contracts. The fixed nongeneric Core interface owners make that additional indirection unnecessary for this bounded decision. Existing draft numbers/layouts are mutable, not the reason to reject redesign. |

This is common schema structure, **not** registration of an executable
generated-text capability. It allocates no feature bit or numeric Feature
identity and does not reuse a native Feature or assigned proof/value Feature
for generic/text semantics. Logical rule names such as
`text-target-parameters/1`, `construction-operations/1` and
`eager-construction/1` are not Feature IDs.

[Manifest/admission rules](../specs/representation/semantic-manifests.md#4-complete-closure-and-before-use-support)
still require complete retained semantics and prerequisite closure. Governing
edition `1`, known row shapes or an empty explicit Feature range do not enable
an incomplete generic/witness/construction capability. Unsupported required
forms block affected binding/lowering/execution, not just optional audits.
Constant formatting would additionally need its own admitted programs,
values, deterministic resource recipes and capabilities.

Under [format versioning](../specs/representation/format-versioning.md),
provisional exchange must select this candidate's exact draft schema out of
band. Matching `(1,0)` or the unchanged registry edition/digest cannot
distinguish it from an earlier incomplete interpretation. No legacy alias,
fallback reader, version-only migration or unpublished compatibility promise
is proposed.

## Candidate conformance and integration gates

These are required proposed cases, not tests run or support claims. Use
independent small artifact vectors and unrelated synthetic implementers/
writers as well as compiler-produced round trips.

| Class | Case and required outcome |
|---|---|
| Positive: literal boundary | No-hole shape has `(captureFirst,captureCount)=(0,0)` and one literal with only its writer requirement nonzero. Both empty and nonempty literal data retain that shape; only nonempty segments make the source-graph writer call. |
| Positive: defaults and types | One hole has three distinct logical captures even with omitted alignment/format. Equivalent row/F2/F3 capture TypeSpecRefs preserve complete identity; zero/empty values and different literal bytes do not change the shape. |
| Positive/negative: range base | A one-hole shape starts its capture range at row 10 and references 10, 11, 12. Accept those rows; 1, 2, 3 are not relative slots and fail same-shape membership even if those earlier rows exist. |
| Positive: evidence | Closed evidence supplies the exact captured type/requirement through a supported direct or inherited nominal witness. Open evidence names the exact generated-type formal's kind-1 Constraint; later supported instantiation supplies evidence rather than requiring a closed witness in the open row. |
| Positive: imports | Raw MethodDef rows name consumer-local expected methods with separately bound interface owners. A checked pack can remap them to provider rows while retaining all relied-on contracts; row-number differences alone do not change identity. |
| Malformed: namespace | Any nonzero reference high byte, including MethodDef/Import/section-kind lookalikes or F2/F3 words, rejects in these row-only positions. A same-numbered valid row in another table cannot rescue it. |
| Malformed: extent/bounds | Missing required tables, row zero where required, a row above its target count, wrong stride, truncated extent, overflowing range/product or a count above the 24-bit table limit rejects before indexing. Resource exhaustion is a distinct refusal, not successful partial checking. |
| Boundary: reference capacity | Row `0x00FF_FFFF` is representable when its target table and checked artifact extents actually contain it; `0x0100_0000` is not. A last child at the table end is valid, while a one-row overrun is not; neither permits wrapped allocation or range arithmetic. |
| Malformed: conditional form | Literal has any capture/formatter word; formatted row lacks one of its three captures or either method; both/neither evidence words are nonzero; kind/flags/reserved is unassigned. Reject without inferring defaults. |
| Malformed: ownership/graph | Child owner/range disagreement, overlapping/orphan children, wrong ordinal, a capture from another shape, reused/skipped capture, missing empty segment or a nonstandard graph/count rejects this physical form. Lowered storage optimization does not excuse a false logical descriptor. |
| Binding: members | Same-named requirement on another interface, a selected implementation substituted for a requirement, a method owned by the wrong bound interface or an incompatible/unresolved Import fails affected binding. Valid shapes cannot bypass the writer's typed hook. |
| Binding: generic subject | Constraint is property/associated-relation kind, belongs to a constant or unrelated formal, reaches an originating method scope without a carrier, or applies only after losing a qualifier. Reject invalid kind/scope or refuse unsupported mapping; do not invent evidence. |
| Cross-feature: projection | Formatting requires qualified-self projection or existential/open-constructed evidence. A coincident InterfaceWitness/Constraint row number is not that evidence. Without its complete selected representation, report unsupported affected use. |
| Cross-feature: ownership | Equal-layout captures differing in inner/outer access, pointer/reference structure or copy/transfer remain distinct. Borrowed TextSpan backing, explicit unique transfer, unsafe obligations and retained/async content remain independently checked; a shape is not a lifetime extension. |
| Replacement | Provider changes required member/evidence identity, narrows caller domain, changes backing/refusal/cleanup or removes a relied-on guarantee. Complete directed comparison fails where the retained contract is not preserved, even if one output sample matches. |
| Remapping | Permute shape/capture/witness/Constraint/MethodDef rows independently, move Module partitions and clone a shared import-dependent type/witness description. Rewrite each namespace and nested closure, preserving logical type/origin and distinct retained expectations. |
| Admission and trust | Well-framed rows with unsupported generic/witness/capability prerequisites cannot execute. Separately, false source-order/effect facts remain producer violations; mandatory structural acceptance is not a claim to have re-proved arbitrary body truth. |
| Runtime qualification | A later executable implementation separately exercises capture-before-call, refusal without later work/replay, once-only formatter invocation, scalar alignment extremes, invalid format failure and cleanup/retention. Emission or structural round trips alone do not establish these outcomes. |

If adopted, amend only the appropriate owners in a separate integration:

| Owner | Required integration |
|---|---|
| [Logical records](../specs/representation/logical-records.md#builder-type-recipes-and-construction-handlers-encoding-pending) | Complete the typed-reference and conditional-form rules; retain the source logical key and distinguish remaining carrier gates. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) and [registry](../specs/representation/trrm-1.registry.json) | Record exact table targets, nullability matrix, row-only words, immediate ownership/range and standard graph rules without changing strides or allocating generic/projection reference tags. |
| [Verification](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) and [representation conformance](../specs/conformance/README.md) | Add independent namespace, range, conditional, binding, rewrite, support and producer-truth cases with their distinct obligations. |

No in-scope field interpretation is left to implementation inference by this
recommendation. Approval remains required for the candidate itself.
The unresolved external decisions are the complete generic/witness/member
reference and mapping codecs, any future projection-inclusive fragment form,
and coherent capability registrations/runtime carriers. They are not silently
settled here.
