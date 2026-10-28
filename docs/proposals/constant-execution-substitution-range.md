# ConstantExecution substitution range

> [!IMPORTANT]
> **Unapproved proposal; provisional direction under the authorized
> missing-contract workflow.** This document changes no normative
> specification, registry or source code and asserts no compiler support.
> Completing this range's representation does not complete Feature 12,
> intrinsic admission or a resource-recipe schema.

## Decision and scope

**Recommend required section `0x005E=ConstantExecutionSubstitution`, with
four-byte rows containing one required `TypeSpecRef typeSpec`.**
`ConstantExecution.substitutionFirst/substitutionCount` selects an exclusive,
ordered range of these rows, not TypeSpec rows, bytes, GenericParam rows or
Body-map entries. Keep section `0042` and its **80-byte** row unchanged.

This completes the target and interpretation of the existing reference
range for **type-only** substitutions. Every selected source formal must
be a type formal for this candidate's admitted request subset. Direct
constant-formal bindings need a separately selected complete representation;
they are not silently omitted or encoded as type references. Constructed
type actuals may contain constant arguments only through their independently
admitted TypeSpec and ConstantSource schemas.

Preserve the companion [IntrinsicRegistration identity
candidate](intrinsic-registration-identity.md): section `005D`, stride 16,
DefId kind `26`, exact origin/key/revision binding, and row-only execution
references. Its identity row supplies neither a formal inventory nor a recipe.
The [static candidate](immutable-static-field-persistence.md) retains
`0055..005C` and its distinct OwnerRef/publication contracts.

## Missing contract and alternatives

The [logical owner](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
assigns `0042`, the four first/count ranges, and the sentence:

> The substitution range contains TypeSpec references in owner-formal then
> method-formal order, or in intrinsic registration order.

Neither the [metadata tables](../specs/il-metadata.md#section-kinds-and-row-layouts)
nor the [machine registry](../specs/representation/trrm-1.registry.json)
names the table containing those reference words. TypeSpec `0011` contains
variable-length **type recipes**, not a packed sequence of four-byte
TypeSpecRef entries. This is a missing field-target contract, not evidence
that variable tables cannot be addressed by row ranges.

| Interpretation | Assessment |
|---|---|
| Range directly over TypeSpec rows | Coherent **if explicitly selected**: slot i would mean the complete type described by TypeSpec row `first+i`, using its offset index, not four bytes at a computed stride. The current text does not unambiguously select this interpretation. Arbitrary argument order and repeated actuals would require arranging or duplicating recipe rows; an inline F2/F3 actual would need a recipe row. Exclusive request ownership would also need to be reconciled with TypeSpec sharing. Earlier-child constraints still apply to every duplicated recipe. This is a substantive amendment to sharing/packing, not an already specified reference-word table. |
| **Dedicated packed TypeSpecRef rows** | **Recommended.** Separates ordered request slots from shared type recipes. Repeated actuals, arbitrary formal order and existing inline complete-type forms work without rearranging TypeSpec rows. Reuses the ordinary fixed-table/range machinery, like the separate `004A` layout-path reference sequence. Costs four bytes per slot and one directory entry when present. |
| Put the list inline in a variable ConstantExecution row | Also coherent, but replaces the assigned fixed 80-byte record and its range instead of completing it. No demonstrated benefit warrants that broader change here. |

Section numbers below are hexadecimal; Feature IDs are decimal. The accepted
registry assigns semantic sections through `004B`, plus `7F00` SourceMap.
Companion candidates use `004C` for foreign-interface identity,
`004D..0052` for general predicates, `0053/0054` for enums,
`0055..005C` for statics and `005D` for intrinsic registration. Select
**`005E`** for this sequence. It adds **no DefId kind, Body token, TypeSpec
form, clause, opcode, Feature ID or prerequisite edge**.

These are mutable draft allocations. Reusing the retired TypeArg candidate's
unassigned `0012`, or repacking the entire family, is permissible with
coordinated schema changes. Neither saves bytes in fixed-width section
selectors nor simplifies this contract. `005E` avoids unrelated changes;
it is not chosen to preserve unpublished compatibility. No old number
becomes an alias.

## Exact candidate wire contract

All multibyte fields are little-endian. `R(T)` means a required one-based
`u32` typed row reference in `1..0x00FF_FFFF`, high byte zero, bounded by
table T's actual count. The field fixes T; an R(T) is not a DefId,
Import token, TypeSpecRef, address or cross-artifact row number.

| Location | Exact encoding and meaning |
|---|---|
| `005E` directory entry | Policy `0000` required semantics; `recordCount=N` in `1..0x00FF_FFFF`; byte length exactly `4*N`, within section/artifact bounds under nonwrapping arithmetic. |
| Each `005E` row, offset 0 | Four-byte **required `TypeSpecRef typeSpec`**. No owner word, ordinal, tag, flags, padding, offset index or count prefix. No additional alignment requirement. |
| `0042` row, offset 16 | `u32 substitutionFirst`: zero only for the empty range; otherwise R(ConstantExecutionSubstitution), **not R(TypeSpec)**. |
| `0042` row, offset 20 | `u32 substitutionCount`: number of ordered entries, not bytes or recursively referenced recipe nodes. |
| `0042.resultTypeSpec` | Unchanged required complete TypeSpecRef, not a row-only field. |
| `0042(sourceKind=1).selectedSourceRef` | Unchanged R(Body); its unique owning MethodDef supplies the selected declaration. |
| `0042(sourceKind=2).selectedSourceRef` and `0044(kind=5).targetRef` | R(IntrinsicRegistration), selecting `005D` under the companion candidate. Import/Export use its `26` DefId separately. |

Each `typeSpec` accepts exactly the existing complete-type forms:

* `0x00_000001..0x00_FFFFFF`: a bounded TypeSpec row.
* `0xF2_0000_cc`: an assigned well-known type, with the reserved payload
  bits zero.
* `0xF3_qq_ff_pp`: an eligible assigned built-in family/atomic argument and
  valid qualifiers, with the existing complete-type identity and use rules.

**Zero is invalid in an entry.** A nonzero word denoting unit is not null.
Body TypeMap row tokens (`11`), direct TypeDef/Import tokens, Body-local
F0/F1 pseudo types, DefIds and ConstantSource row references are not
alternative branches of this field. A coincident numeric row does not
change its schema-owned namespace.

The range is `(0,0)` **iff empty**. Otherwise first is nonzero with a zero
high byte, and count is positive. Check first against the actual `005E`
count, then require `count <= N-first+1`; the inclusive last row must also
fit the 24-bit row domain. Counts remain `u32` integers, not typed
references. Check products, indexes, offsets and allocations without
wrapping. An absent section has zero rows; omit it when empty, never emit
an empty directory entry. A missing section cannot satisfy a nonempty range.

## Ordering, arity and selected-source checks

The slot's zero-based position is its formal coordinate; no ordinal is
serialized. Defaults and inference have already selected actuals. Include
unused and representation-irrelevant formals; do not infer missing entries
from result/argument types, substitute defaults during decoding, sort by
type identity or remove repeated types.

### Checked Body

1. Resolve the exact Body and the unique MethodDef whose `bodyRow` names
   it. Validate its signature and ownership. An imported bodyless
   expectation, signature anchor, MethodDef row or MethodMap token cannot
   stand in for the selected Body.
2. Obtain applicable owner formals from that MethodDef's declaring
   TypeDef's checked GenericParam range, or none for a module-level
   callable. Then obtain method formals from its MethodSig's generic range;
   those GenericParams must name this **MethodDef**, not MethodSig, as
   their DefId owner. Preserve each range's declaration ordinal.
3. For this type-only candidate require all these formals to have
   `formalKind=1`. Require count to equal **owner-formal count plus
   method-formal count**. The first block binds owner formals, the second
   method formals. A type-scoped static method still has applicable owner
   formals; absence of a receiver does not erase them. A nongeneric owner
   adds no placeholder slot.
4. Normalize each supplied complete actual in the request's using context.
   Match its formal's ownership/nullability domain, constraints,
   qualification, formation and use eligibility under the
   [ordinary substitution rules](../specs/language/types-and-normalization.md#forward-substitution-and-normalization).
   Preserve nominal/formal/witness identities and every admitted qualifier;
   equal carriers or layout are not equal complete actuals.
5. Apply that exact substitution consistently to receiver, parameters,
   result and retained contracts. `resultTypeSpec` must match the selected
   substituted result. Validate transformed argument count/order and
   source-use/state forms against the substituted signature: receiver iff
   present, then every logical parameter, including unit parameters.
6. Required execution additionally needs the exact admitted ConstEval form
   and program, not Pure alone, a same-named attribute or a body-shaped
   payload. Check the actual reached forms, effects, values and
   dependencies before execution; retaining this list supplies no new
   effect, unsafe, initialization, backing or publication authority.

Symbolic type terms are inspectable only in an independently admitted,
exact formal/binding context. All actuals and witnesses needed for concrete
execution must be resolved first; a bare existential or unknown formal is
not a supplied concrete actual. This proposal supplies no open-generic
execution/evidence codec.

If any selected owner/method formal needs a direct constant binding
(`formalKind=2`), this candidate alone **cannot admit the request**, including
a constant formal with a default. Do not silently use the type-formal
projection of a mixed list, reinterpret a TypeSpecRef as ConstantSource or
invent an empty substitution. A separately reviewed mixed-binding
representation remains necessary. This does not forbid independently
admitted constant arguments nested inside a supplied constructed type.

### Intrinsic

Resolve `005D` and any expected-row Import using the companion identity
rules. The exact **origin Module, registrationId, ruleRevision** selects
the admitted registration contract. That contract must specify its complete
ordered formal inventory, actual domains, input transformations, forms and
result. For a type-only inventory, count equals its exact arity and entries
follow **registration order**, not an inferred MethodDef order.

A known identity with no admitted formal/input contract does not imply
arity zero. Missing/unknown contracts refuse affected binding/execution;
they do not authorize a similarly named method, another revision, a guessed
signature or a checked-Body fallback. A registration requiring direct
constant bindings has the same separate representation gate as above.
No formal-inventory or transformation recipe bytes are added to `005D`.

### OwnerRef is a different field contract

The [static candidate's OwnerRef](immutable-static-field-persistence.md#physical-conventions-and-exact-records)
is that proposal's complete-owner TypeSpecRef shorthand: zero **exactly** when
that declaration has no applicable formals; otherwise the exact complete
declaring-owner construction with every ordered type/constant argument.
Zero means no owner substitution, not no owner. Preserve that rule.

This proposal adds **no OwnerRef field** to ConstantExecution. Its owner
prefix contains individual actuals, not one owner-construction entry.
For a checked source with type-only owner formals, that prefix must
reconstruct the exact selected declaring owner. Method-only substitution
can be nonempty while a related nongeneric OwnerRef is zero.

OwnerRefs reached through ImmutableData/static dependencies still name
**their own** exact declaration owners, not automatically the executed
method's owner. Apply any admitted symbolic context consistently and check
their separate binding/publication requirements. Never put their permitted
zero absence marker in a `005E` slot or treat a count-zero list as a
generic owner's missing construction.

## Ownership, adjoining records and per-Module requirements

Every `005E` row belongs to exactly one ConstantExecution range. Different
request rows have disjoint ranges: no shared whole lists, partial overlap
or range interning, even for equal sequences. Equal types in several slots
are valid and require separate **entry rows**; their TypeSpec recipes may
remain shared. Unowned sequence rows are invalid producer output. This
does not add permission to share request occurrences.

Keep the other ranges and reference families unchanged:

* `0043` arguments remain transformed **values/views/destinations**, not
  generic actuals. Kind 3 retains its zero source reference only for the
  admitted vacant destination form; it is not a missing type argument.
* Kind-4 arguments select a distinct nested request with the existing
  matching return-type/profile, by-value-only and acyclic-request rules.
  Each nested request supplies its own complete source selection and
  exclusive substitution range. No implicit parent-list inheritance.
* `0044` remains the request's direct actually reached dependency set,
  omitting the selected source, argument-nested requests and separately
  owned layout dependencies. Kind 5 is registration identity, not a
  generic call-site substitution. Actual substitutions of calls within a
  Body or intrinsic remain owned by their respective admitted schemas.
* Kind 6 still selects ImmutableData, and layout queries/views keep their
  own target/path/backing obligations. A type substitution does not
  initialize static storage, materialize a value or establish publication.

`005E` has no Module cursor or owner word. The retained request attachment
supplies **using Module U**, formal/binding context and target. The selected
Body's Module, an intrinsic's physical owner, and its resolved provider/origin
are distinct contexts and do not replace U.

Require `12/1` for the retained request and its substitution use in U; add
no new Feature or unconditional prerequisite. Under the **unapproved
companion composition**, `12 -> {11,17}`, `17 -> {10}`, and scalar 11,
selector 10 and ImmutableData 19 have no unconditional prerequisites.
This preserves that composition, not the current registry's different
incomplete prerequisite lists, and does not complete registration of 12.

Discover requirements structurally through **every retained slot**, its
nested TypeSpec/ConstantSource references, arguments, views, nested requests
and dependency/layout ranges. Preserve all supported actual-use requirements:
for example, retained ImmutableData requires 19 in U, and a layout-query
use needs its applicable 13/target support. A type's mere spelling does not
impose unrelated features. A selected provider retains its own manifest;
its 19 cannot repair U's missing 19, and U does not inherit provider-private
uses merely from selecting that provider.

Inspect retained unselected branches/expectations without executing them.
This does not fabricate an executed dependency for an unreached call.
Memoize by artifact/exact schema, U, formal/binding and target context,
not solely the source, registration or TypeSpec row. If another retained
using context is permitted, validate it independently; clone helpers when
their meanings diverge. Substitution resolution through ConstantSource,
requests or views cannot introduce a forbidden cycle or infinitely
expanding type dependency.

## Verification, canonicality and re-emission

Producers must publish valid references, complete actuals, requirements and
truthful selected forms/dependencies. Before affected use, consumers check
framing/policy/stride/extents, required words, range bounds/ownership,
TypeSpecRef forms and use positions, selected-source identity/ownership,
formal kinds/count/order, substituted input/result compatibility, manifests
and binding support. An ownership index can use request range headers;
this is not a requirement to scan unrelated Body payloads or re-prove them.
If a producer swaps two actuals and the resulting request is still well
typed, positions select a different substitution; a consumer cannot recover
the author's intended ordering from equal-shaped types. Producer truth
obligations remain separate from detecting structural incompatibility.

Preserve the [consumer-trust boundary](../specs/representation/reader-trust.md):
these mandatory structural/support/binding checks do not semantically
re-prove arbitrary producer-published body, effect or dependency facts.
Invalid structures reject before use. Missing independently required
schemas/catalogs/evidence refuse the affected operation; resource exhaustion
is a separate explicit refusal, not malformed bytes or a successful empty
list. No case substitutes unit, a runtime call, false proof or another
overload.

For a fixed ConstantExecution table order, canonical writers concatenate
its nonempty substitution ranges in that order, preserving formal order
within each. Ordinary readers accept a different physical range placement
with consistently remapped first fields and valid exclusive ownership.
Sorting or deduplicating **slots** changes their meaning; repeated actual
identities are not duplicate dependency entries.

Apply the existing non-Body complete-TypeSpecRef writer rule independently
of slot placement: minimize complete artifact size, considering all
remaining recipe uses, and prefer an eligible F2/F3 form on equal-size
ties. Otherwise valid equivalent row/inline spellings remain ordinary
noncanonical input. Audit only with the relevant complete use inventory;
no unique whole-artifact byte image or global TypeSpec deduplication is
introduced.

A packer, trimmer or rewriter must decode and rebuild:

1. Resolve source/destination exact schemas and every retained using
   context. Retain the complete selected Body or registration contract,
   actuals and original binding/replacement obligations.
2. Remap ConstantExecution rows and all references to them; reconstruct
   their exclusive `005E` ranges and first/count fields. Remap row-form
   TypeSpecRefs through the TypeSpec map; F2/F3 use their own schema,
   not a row-number rewrite. Rebuild changed TypeSpec recipes, nested
   ConstantSource references and offset indexes in valid dependency order.
3. Remap selected Body/registration rows, GenericParam/owner references,
   result/input/view/dependency/layout references and conditional
   OwnerRefs according to each field's namespace. Map corresponding
   expected/provider formals by kind/ordinal; formal ordinals are not
   artifact row references. Registration `26` DefIds remain separate from
   its two row-only uses; `005E` introduces no DefId mapping.
4. Preserve selected origins, export keys, complete expectations and
   stronger relied-on facts when replacing Imports with packed direct
   references. Clone shared structural types instead of changing another
   Module's meaning. Removing a request removes its owned sequence rows;
   shared type recipes survive while otherwise required.
5. Recheck arity, normalized input/result/owner agreement, exclusive
   ownership, cycle rules and each Module's complete retained closure.
   Rebuild section counts/extents and any affected Body maps/offsets and
   optional SourceMap slots; regenerate or drop optional data that cannot
   be preserved truthfully.

This is schema-aware re-emission, never substitution of matching bytes.
Packing retains distinct Module identities and one destination schema
capable of representing every retained obligation.

## Required candidate cases

These are future conformance tests, **not executed tests or support
claims**. Positive execution cases require all independently needed
program/value/recipe/target schemas to be selected and supported.

| Case | Required outcome |
|---|---|
| Nongeneric checked source or admitted zero-arity intrinsic | Exactly `(0,0)`; no placeholder entry. Omit `005E` if all requests are empty. Ordinary unit parameters still have their argument slots. |
| Type-only owner and method formals with actual sequence A, B, A; nonadjacent variable TypeSpec recipes | Retain three slots in exact owner-then-method order; repeated A is valid and does not force duplicate recipes or a contiguous TypeSpec slice. |
| Eligible row/F2/F3 representations of the same actual; all-inline request requiring no TypeSpec recipes | Same complete identity and binding; ordinary reads accept valid equivalents. `0011` may be absent when otherwise unused while `005E` remains nonempty. Writers/audits use the complete-size rule. |
| Intrinsic expected row imported from another Module, then packed direct reference | Same origin/key/revision, catalog arity/order and original replacement obligations; neither row order nor `26` DefId bits select formal order. |
| Two requests with equal lists; whole-range reorder with repaired first fields | Separate disjoint entry rows; valid reorder is ordinary noncanonical input. Canonical output concatenates by current request order. |
| First row 1, last row `0x00FF_FFFF`, one-entry endpoint range, largest representable in-bounds range | Accept within actual table/section and supported envelopes. The row maximum is not a portable resource guarantee. |
| Empty pair `(1,0)` or `(0,1)`; high-byte first; one-past-table; `u32` count/extent overflow; truncation or absent nonempty section | Reject invalid framing/ranges without wraparound or byte/TypeSpec-row reinterpretation. |
| Wrong policy/stride, extra trailing row bytes, present zero-count section, unowned entries or shared/overlapping ranges | Reject structural/ownership violations; equal lists do not justify interning. |
| Zero entry; `11` Body token, TypeDef/Import token, F0/F1, reserved token, malformed F2/F3, out-of-bounds TypeSpec row | Reject by the exact entry namespace and formation/use rules. A numerically equal ConstantSource row is not a constant binding. |
| Too few/many actuals, skipped default/unused formal, swapped owner/method blocks incompatible with formal/result/use contracts, wrong GenericParam owner/domain/qualifier | Reject incompatible selected-source substitution; do not infer or reorder bindings to make the result match. |
| Permuted actuals remain well typed under same-shaped formals | Decode their positional binding as a different substitution, not an equivalent encoding. Do not promise to detect a producer's false claim about intended selection. |
| Generic owner on static source; method-only generic source on nongeneric owner; related OwnerRefs | Retain the former's owner block; omit the latter's empty block. Enforce each separate OwnerRef's zero-or-exact-construction rule, with no zero substitution slot. |
| Missing generic-owner construction, redundant nonzero nongeneric OwnerRef, wrong constructor in a field requiring the selected declaring owner | Reject the applicable owner mismatch; neither complete-type equivalence nor zero absence changes a field's role. |
| Selected source has direct constant formals, or execution needs unresolved generic/witness evidence | Refuse affected support until the separate complete representation/evidence contract exists; never silently project to type formals or guess constants. |
| Typed Body reference replaced by MethodDef/token; bodyless anchor/import, Pure-only source, incompatible result or transformed input form | Reject invalid source/reference or incompatible binding/form; no implicit ConstEval or runtime fallback. |
| `005D` identity known but formal/input/recipe schema missing; same key under a different origin/revision | No guessed zero arity or method fallback. Exact identity mismatch fails; incomplete semantics refuse execution independently of range validity. |
| Kind-4 nested request with its own substitution; wrong result/profile, shared ranges, request/view/substitution cycle | Accept only the independently valid by-value acyclic case; reject the mismatches/cycles without inheriting parent slots. |
| Separate requests in two using Modules share source/type recipes; only one has a required actual-use Feature | Validate both contexts and reject the deficient Module's affected use. A cache or provider manifest cannot supply its missing requirements. |
| Type actual contains an admitted constructed constant source or retained immutable/view dependency | Traverse its requirements in the correct using context without running initialization. Preserve conditional 19 and other actual-use requirements even after folding. |
| Reorder/delete TypeSpec, request, registration and dependency rows; replace an Import; retain optional source mappings | Rebuild typed references, exclusive ranges, offsets/maps and manifests; preserve nominal/owner identity, bindings and source-data truth. No byte substitution. |

## Intended supersession and integration owners

These are precise **future** edits if this direction is adopted; none is
made by this proposal:

| Owner | Text or schema to add/replace; preserved boundary |
|---|---|
| [Logical records: published predicates and evaluation forms](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Replace “The substitution range contains TypeSpec references ...” with an explicit exclusive `005E` range, entry schema, type-only applicability, selected-source ordering and checks above. Narrow the open “reference kinds and allocations” deferral only for this carrier. Preserve the 80-byte request, other ranges/tags, intrinsic resource ownership and recipe deferral. |
| [Metadata: table stream/token encoding and row semantics](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Add the `005E` section/stride/row projection and the exact `0042` range target, ownership and arity rules. Classify `ConstantExecutionSubstitution.typeSpec` as a required complete-TypeSpecRef field; its first/count remain row/range integers. Preserve `0011` variable recipes, retired TypeArg status and other reference grammars. |
| [Registry owner](../specs/representation/registry.md), [machine registry](../specs/representation/trrm-1.registry.json) | Add `metadata.sections` entry `005E`, policy `0000`, stride 4, field `u32 typeSpec`; add `substitution_range_section: "005E"` to `0042` and this entry field to `metadata.type_reference.complete_type_fields`. Record exclusive range ownership. Do not add a definition/token kind or mark Feature 12 registered. |
| [Reference namespaces](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) | Add `0042.substitutionFirst/count -> 005E`, its required complete-type entries and context-aware remapping. Preserve selected-source row-only fields, conditional OwnerRef semantics and producer truth versus consumer checking. |
| [Canonicality: complete types and re-encoding](../specs/representation/encoding-canonicality.md#4-body-dependency-maps-and-typespecref-values) | Apply existing complete-TypeSpecRef spelling rules; add slot-preserving range placement and decoded remapping. Do not apply dependency-set sorting/deduplication to formal slots. Preserve ordinary equivalent noncanonical acceptance and scoped audits. |
| [Requirements](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md), [binding/replacement](../specs/representation/binding-and-replacement.md) | State the retaining/using-Module traversal through all actuals and exact source contracts, without a new Feature edge or artifact-wide Feature union. Preserve complete original bindings and exact schema selection. |
| [Intrinsic identity proposal](intrinsic-registration-identity.md#refusal-boundaries-and-remaining-decisions) | Supersede its remaining “substitution ... codecs” deferral **only for this type-only sequence carrier**. Preserve `005D/26`, exact identity/binding, missing formal/input catalog and compact recipe schemas. Static/ImmutableData OwnerRefs retain their separate owners unchanged. |

Source syntax, library registration policy, runtime storage, target ABI and
Body instruction semantics do not change. Later binding/verification/lowering
would carry these exact typed actuals through the ordinary selected-source
pipeline, not a second evaluator or source/API-specific recognizer.

## Separate decisions and approval boundary

The section, stride, range target, slot order, null/bounds/ownership rules
and type-only arity behavior are fully selected **as a proposal**. Remaining
decisions are separate:

* A complete direct constant-formal/mixed substitution carrier, if requests
  selecting those formals are to be admitted; this four-byte type sequence
  must not be widened by undocumented position guessing.
* Intrinsic formal/input-transformation catalog schemas and individual
  registrations; compact resource-recipe grammar, versioned allocations,
  recipe-owned dependencies and exact work/peak-storage computation.
  **Recipe schemas remain separate.**
* Completion of Feature 12 and independently missing program/value/view,
  generic/witness, target/publication and execution representations/support.
  Known row shapes and complete range decoding do not close those gates.

Exchange must select the complete amended draft/catalog **out of band**
under [format versioning](../specs/representation/format-versioning.md).
Equal `(1,0)` bytes, Feature 12, section presence or a plausible stride
cannot choose this decoder. No version-pair bump, historical reader,
compatibility alias or draft migration is required. Provisional work under
the user's authorization is not normative adoption, stabilized allocation
or evidence that any compiler implements this design.
