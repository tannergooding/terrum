# ConstantExecution mixed checked-Body substitutions

> [!IMPORTANT]
> **UNAPPROVED proposal; provisional direction only under the user's
> authorized missing-contract workflow.** This document changes no code,
> normative specification or registry and claims no implementation or
> execution support. Completing this carrier does not complete Feature 12,
> constant-domain/value codecs, intrinsic registration catalogs or recipes.

## Decision and boundary

**Recommend replacing candidate `005E`'s four-byte type-only entries with
one eight-byte tagged `ConstantExecutionSubstitution` sequence.** Its two
closed tags are `1=TypeActual` and `2=ConstantActual`. They carry a complete
`TypeSpecRef` and a row-only `ConstantSource` reference, respectively.
Keep `0042`'s 80-byte row and its existing substitution first/count fields.
For a checked Body, include **every owner formal, then every method formal**,
without projecting out constant formals.

This is one successor schema for type-only, constant-only and mixed
**checked-Body** requests, not a second mixed-only codec or a widening of
`TypeSpecRef`. In that successor, type-only slots also have eight-byte rows.
The [earlier proposal](constant-execution-substitution-range.md) remains
unchanged; its proposed supersession is specified below. Neither proposal
has normative approval.

Preserve [intrinsic identity](intrinsic-registration-identity.md)'s separate
candidate `005D`, 16-byte row, DefId `26`, exact
origin/registrationId/ruleRevision binding and row-only execution references.
**It provides no formal catalog.**
This proposal does not define intrinsic constant-formal binding, intrinsic
input transformations or an execution/resource recipe. Knowing a
registration identity cannot supply those missing contracts.

No source syntax, library API, runtime initialization protocol, target ABI,
Body instruction, Feature ID or prerequisite edge is added. Independently
missing generic/witness, nominal constant-domain and value representations
remain gates, not implementation-selected defaults.

## Baseline and allocation choice

The [specification index](../specs/README.md) leads to three relevant owners:

* [Logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
  assigns the request and says its substitution range contains TypeSpec
  references in owner-formal then method-formal order, or intrinsic
  registration order. That sentence has no direct constant-formal branch.
* [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
  assigns `GenericParam.formalKind=1` for type and `2` for constant.
  Constructed TypeSpec `05` already uses complete TypeSpecRefs for type
  actuals and ConstantSource rows for constant actuals, in formal order.
  GenericParam defaults have the same kind-directed namespaces.
* [Complete types](../specs/language/types-and-normalization.md#forward-substitution-and-normalization)
  and [constant generics](../specs/language/constant-generics.md) require
  exact complete terms and typed constant values, not erased carriers.

The earlier `005E` proposal deliberately selects four-byte TypeSpecRef-only
rows and refuses selected `formalKind=2` formals. That is a bounded candidate,
not an implementation bug or an accepted prohibition on mixed requests.
This proposal intentionally replaces that limitation; it does not claim the
accepted range sentence already specifies tagged entries.

Codes below are hexadecimal except decimal Feature IDs. The accepted
registry assigns semantic sections through `004B`, plus `7F00` SourceMap.
Companion candidates use `004C` for foreign-interface identity,
`004D..0052` for general predicates, `0053/0054` for enums,
`0055..005C` for static persistence and `005D` for intrinsic identity.
**Amend candidate `005E` in place; allocate no `005F`.** These unpublished
assignments may be redesigned; preserving their old bytes is not a goal.
Moving unrelated families saves no bytes in fixed-width section selectors.

| Coherent alternative | Assessment |
|---|---|
| **One tagged eight-byte successor at `005E`** | **Recommended.** Keeps one range and one payload decoder, makes the payload namespace explicit before formal binding, and permits an independent kind/formal mismatch check. Reuses the `u8 kind, u8 flags, u16 reserved, u32 reference` convention of arguments/dependencies. Costs four more bytes per slot than the current candidate; no size or throughput improvement is claimed. |
| Formal-directed four-byte union at `005E`, with no new slot tag | Coherent and byte-smaller: formal kind alone could select TypeSpecRef versus ConstantSource, as in constructed TypeSpecs. It requires the complete selected formal inventory before interpreting/remapping any entry, and supplies no independent encoded-kind check. This is a legitimate amendment, not forbidden position guessing, but is not selected for this standalone request sequence. Constructed TypeSpec/default encodings remain unchanged. |
| Pack a constant discriminator into the four-byte reference word | Requires a new field-specific token grammar or reallocating a reserved complete-type discriminator. It can preserve four-byte slots but creates another reference namespace instead of reusing the two existing payload namespaces. Not selected; no F4 or other token is allocated. |
| Keep four-byte `005E` and add a mixed sequence at `005F` | Needs an explicit table-selection rule, such as a new request discriminator or source-kind/formal-dependent target rule; stride plausibility cannot select it. Splits readers, writers and remapping only to preserve unpublished type-only bytes. Not selected. |
| Separate type and constant ranges, or inline variable request payloads | Possible redesigns, but add ranges/selection rules or replace the assigned fixed request row. Neither is needed to represent the existing ordered actual list. |

## Exact successor wire contract

All multibyte fields are little-endian. `R(T)` means a required one-based
`u32` reference to table T in `1..0x00FF_FFFF`, high byte zero, bounded by
its actual row count. It is not a DefId, Import token, TypeSpecRef, address
or cross-artifact row number.

Required section **`005E=ConstantExecutionSubstitution`**, policy **`0000`**,
concatenates these fixed **eight-byte** rows:

| Offset | Bytes | Field | Closed meaning |
|---:|---:|---|---|
| 0 | 1 | `u8 actualKind` | Exactly `1=TypeActual` or `2=ConstantActual`. |
| 1 | 1 | `u8 flags=0` | No default, inference, trust or evaluation-mode flags. |
| 2 | 2 | `u16 reserved=0` | Required zero, not unspecified padding. |
| 4 | 4 | `u32 actualRef` | Required complete TypeSpecRef for kind 1; R(ConstantSource), section `0030`, for kind 2. |

There is no owner word, ordinal, offset array, count prefix or trailer.
The tag values correspond to GenericParam's kinds for matching purposes,
but are a separate one-byte field, not GenericParam references. Zero and
other tags, nonzero flags/reserved and zero `actualRef` are invalid.

For **kind 1**, accept the existing complete-type grammar only:

* `00_000001..00_FFFFFF`: bounded TypeSpec row;
* `F2_0000_cc`: assigned well-known type, upper payload bits zero;
* `F3_qq_ff_pp`: eligible assigned built-in family/atomic argument with
  valid qualifiers and use position.

For **kind 2**, the entire word is a high-byte-zero ConstantSource row.
The **separate ConstantSource row** retains its existing eight-byte
`u32 kind, u32 payloadRef` and tags
`1 -> FrozenValueNode`, `2 -> ConstantExecution`,
`3 -> TargetValueBinding`; other source kinds are invalid.
Its `payloadRef` is required R of the tag-selected table, not a TypeSpecRef.
No inline integer, frozen-node shortcut,
direct nested-request reference or new ConstantSource tag is introduced.
The selected source record supplies the constant's complete type.

Body TypeMap tokens (`11`, F0/F1), TypeDef/Import tokens, DefIds and
unassigned complete-type forms are not entry alternatives. A valid F2/F3
word is never a ConstantSource row. A high-byte-zero word such as 1 can
name either table numerically; **the slot tag alone fixes its namespace**,
then the formal must agree. Nonzero unit TypeSpecRefs are not absence
markers; unit is not a constant-formal domain.

`0042.substitutionFirst`, at offset 16, selects R of **`005E`**, not a
TypeSpec/ConstantSource/GenericParam row or byte offset.
`substitutionCount`, at offset 20, counts slots. Empty is exactly `(0,0)`;
otherwise first and count are positive, first has a zero high byte, and
`count <= N-first+1` after checking `first <= N`. The inclusive last row
must fit the 24-bit row domain. Counts are integers, not reference words.

A present `005E` has `recordCount=N` in `1..00FF_FFFF` and byte extent
**exactly `8*N`**, checked without overflow against section/artifact bounds.
Omit it if empty; an absent table has zero rows and cannot satisfy a
nonempty range. There is no additional alignment requirement. Capacity
bounds are not portable resource guarantees.

## Checked-Body binding

### Source and complete formal coordinates

For `0042.sourceKind=1`, `selectedSourceRef` remains **R(Body)**.
Resolve its unique owning MethodDef through `bodyRow`, validate its
MethodSig and declaring TypeDef, and use that exact checked source.
A MethodDef/Import/MethodMap token, bodyless expectation or signature anchor
cannot substitute for the Body. Import binding may make a provider Body
available, but does not put an Import token in this field.

Build the formal coordinate list as follows:

1. The declaring TypeDef's complete `genericFirst/genericCount` range, or
   no owner formals for a module-level method.
2. The selected MethodSig's complete generic range. Its GenericParams are
   owned by the **MethodDef DefId**, not the structural MethodSig.

Validate exclusive formal ownership and contiguous zero-based ordinals
within each block. Formal identity is the resolved declaration plus ordinal;
owner ordinal 0 and method ordinal 0 are distinct. A type-scoped static
method still has its declaring owner's formals. No receiver does not mean
no owner substitution.

Require exactly `ownerCount + methodCount` entries, with kind 1 for each
`formalKind=1` and kind 2 for each `formalKind=2`. Retain unused,
defaulted and representation-irrelevant formals, including constant-only
blocks. Defaults/inference were resolved before this request: there are no
holes, omitted defaults, inferred trailing entries or zero-value shortcuts.
Never sort by kind or serialize all type actuals before all constant actuals.

For example, formal coordinate blocks `(type, constant, type)` and
`(constant, type)` require slot tags **`1,2,1,2,1`**. This is metadata
notation, not new source syntax.

The owner prefix reconstructs the exact complete declaring-owner instance;
the suffix binds that instance's method formals. No OwnerRef field or
single owner-construction slot is added. Separate static/ImmutableData
OwnerRefs keep their own zero-if-no-formals/exact-construction contract and
their own declaration owner; none supplies a missing request slot.

### Actuals, domains and constraints

Let U be the request's retaining/using Module and supplied outer binding
context; let S be the selected source's declaration context. Resolve actuals
in U and declaration terms in S, preserving their resolved identities.
Binding imports does not replace U with S or reinterpret U's imports in S.
The admitted retaining declaration/Body/constant/contract attachment supplies
U; neither `0042` nor `005E` gains a Module cursor. If the required using
context cannot be recovered, affected use is unsupported, not assigned to
S or a Module chosen by physical table placement.
Build the full positional substitution, then apply ordinary forward
substitution/normalization to its dependent terms; dependency order need
not be physical slot order. No inverse inference or overload rebinding occurs.

**Type actual:** preserve its complete nominal/constructor identity,
ordered type/constant arguments, representation and every qualification.
Check the formal's ownership domain (`1=default non-unique Copy-capable`,
`2=unique-only`, `3=default plus unique`), nullability domain
(`1=non-null`, `2=nullable allowed`), and all applicable interface,
intrinsic-property and associated-relation constraints after substitution.
Check formation, qualification and use eligibility, with independently
admitted witness/evidence when required. Equal layout/carriers do not match
complete types. A type actual does not itself need a frozen value merely
because it is a substitution, nor does it admit values of that type to
constant execution.

**Constant actual:** the formal retains zero type-domain tags, no type
Constraint range and a required complete `constantTypeSpec`. Compute
`D = Normalize(Substitute(formal.constantTypeSpec))` in the exact formal
context. Resolve the ConstantSource's selected record and its complete
result type in its own using/binding context. That normalized type must
**equal D**, not merely convert to it or have the same carrier.

| ConstantSource kind | Type and availability obligation for a constant actual |
|---|---|
| `1=FrozenTypedValue` | FrozenValueNode root's complete type equals D; its value/codec and selected target are admitted and its encoded validity checks succeed. Reading it does not rerun its constructor. |
| `2=RequiredTargetEvaluation` | Selected ConstantExecution's substituted `resultTypeSpec` equals D; resolve its own complete source, substitution, inputs, target and envelope. It must finish successfully and establish a valid D value before the consuming request uses that constant. A result-type declaration alone is not a value. |
| `3=CompletedTargetDependentValue` | TargetValueBinding's root type equals D; validate its exact TargetProfile, owned layout dependencies, frozen value and surviving obligations. It is a completed value, not a fallback recipe. |

D must satisfy the accepted **constant-formal catalog**, not the wider
ordinary constant-execution value domain: fixed-width signed/unsigned
integers, Boolean, admitted nominal enums over those integers, or eligible
Transparent integer/Boolean wrappers with their exact published commitments.
Normalize aliases, but retain enum/wrapper nominal identity and complete
generic arguments. Native-width integers, characters, floating/decimal,
unit, optionals, tuples, general aggregates and reference/resource types do
not become constant-formal domains because a frozen codec can encode them.
No numeric promotion, carrier extraction, raw wrapper fabrication or
implicit conversion supplies a binding.

Validate mathematical/logical range and encoded validity. Canonical constant
identity is exact normalized domain plus valid integer/Boolean carrier
value: enum member aliases may agree, different nominal domains do not.
Comparison/hashing never calls user equality, constructors or formatters.
Producer-published semantic validity remains a trust fact under the
[consumer boundary](../specs/representation/reader-trust.md); consumers do
not re-prove arbitrary wrapper invariants by replaying producer bodies.
Missing enum/wrapper/domain codecs are unsupported requirements, not
permission to erase D to an admitted scalar.

Symbolic terms can be inspected only in an independently admitted formal
context. Before concrete execution or forming a constant-dependent complete
type/layout, resolve every needed type, constant value and witness.
This carrier adds no open-generic execution/evidence or symbolic-constant
tag. A previously checked value can be forwarded through an admitted source
without repeating construction, while retaining its dependencies.

### Forms, result and transformed inputs

Require the exact checked **ConstEval evaluation form**, not Pure alone,
an Effects bit, a same-named annotation or a body-shaped blob. The Body
retains ordinary source/structural validity and declared form/effect
contracts. Before each reached operation/call, require its admitted constant
program, values, effects, dependencies and access/initialization/backing
authority. Unsafe authorization does not admit foreign calls, host pointers,
runtime static initialization or otherwise forbidden constant effects.
A known runtime-only call on an unselected valid mode-guarded path is not
executed; unknown instructions or malformed selected-body structures still
reject before use.

Apply the **same complete mixed substitution** to receiver, parameters,
result, constraints and retained contracts. `0042.resultTypeSpec` remains
a required complete TypeSpecRef and must equal the selected substituted
result even when the request has generic formals. Its separately published
frozen result must have that type and its admitted value/layout encoding.
The result domain may be wider than a constant-formal domain; only a use as
a constant actual imposes the latter catalog.

Keep `0043`'s eight-byte rows and tags unchanged. Require receiver iff
present, then every signature parameter, including unit parameters:
`1=FrozenValueNode`, `2=LogicalInputView`, `3=VacantInitDestination`,
`4=ConstantExecutionResult`. Match exact substituted types, modes,
source-use/state forms and reference/view access/backing. Tag 3 alone has
zero source and must match an admitted vacant initialization destination;
it cannot supply a generic constant. Tag 4 is by-value only, names a
distinct nested request and retains exact result/profile matching.
Generic actuals are not extra ordinary parameters or argument rows.

A target-dependent request or needed constant source requires its exact
concrete TargetProfile. A nested request/binding used for a constant actual
must match the consuming request's profile by semantic identity; zero is
not a wildcard or host-default target. Zero is permitted only for an
independently target-independent request/closure. A completed binding for
another profile cannot be reused merely because its observed integer agrees;
only a separately retained request selected for the requested profile can
supply reevaluation under the existing TargetValueBinding rule.
Successful evaluation does not initialize runtime static storage or erase
publication, validity, layout or load obligations.

## Ownership, dependency closure and canonicality

Every `005E` row belongs to exactly one request's range. Ranges are disjoint:
no whole-list sharing, partial overlaps, interning or orphan rows. Repeated
type or constant identities within/across requests are valid and need
distinct **slot rows**. TypeSpec/ConstantSource/value referents retain their
own sharing rules; this grants no new request-occurrence or frozen-tree
sharing permission.

Extend the mandatory request-dependency cycle check to constant actuals,
including ConstantSources inside type actuals and substituted type/domain
terms. Required edges include argument nested results, view-result backing
and `ConstantSource.kind=2` results needed to establish substitutions.
Reject self-dependence, indirect cycles and infinitely expanding structural
type/value resolution, even if a Body would not read that formal.
An unresolved target dependency can defer work to the selected target stage,
not satisfy a cycle. Ordinary recursion *inside* an eligible checked Body
remains allowed under the existing execution envelope.

Each nested request owns its own complete substitutions and ranges; no
parent-list inheritance. Traverse value/substitution edges directly rather
than inventing an intrinsic or MethodDef executed dependency. Amend `0044`'s
existing omission of argument-nested requests to omit also requests reached
through substitution ConstantSources/type terms: those are explicit typed
input-resolution edges, not direct calls executed by the selected Body.
Their own executed/layout dependencies stay on their own requests.
Keep actual execution dependencies, observation footprints, layout queries,
backing/publication and static initialization dependencies separate.

In U require retained request capability `12/1`, and structurally traverse
**every slot**, nested TypeSpec/ConstantSource, selected value/request,
argument/view and retained dependency, without executing to discover
requirements. Under the companion **unapproved** composition, preserve
`12 -> {11,17}`, `17 -> {10}` and empty unconditional prerequisites for
scalar 11, selector 10 and ImmutableData 19. Add requirements only for actual
retained uses, including 19 for ImmutableData and applicable layout/target
support; add no unconditional 19 edge. This does not register complete 12.
The [scalar](scalar-frozen-value-registration.md),
[general-predicate](general-predicate-graph-schema.md) and
[ImmutableData admission](immutable-data-feature-admission.md) proposals own
that composition; the accepted registry's different incomplete prerequisite
lists are not silently reinterpreted.

S/provider Modules retain their own manifests. Their capabilities cannot
repair U's missing requirements, and U does not inherit provider-private
uses merely by selecting the Body. Memoize by artifact/exact schema,
using Module, formal/binding and target context, not row alone.
Multiple permitted using contexts require independent checks; clone
context-sensitive helpers rather than changing another Module's meaning.

For a fixed request-table order, canonical writers concatenate nonempty
`005E` ranges in that order, preserving slot order and repetitions.
Ordinary readers accept a consistently remapped different placement with
valid exclusive ownership. A slot permutation denotes another positional
substitution, not an equivalent encoding, even if it remains well typed.
Consumers cannot recover an author's intended actuals from same-shaped
formals.

Kind-1 references follow the existing non-Body complete-TypeSpecRef
complete-artifact-size rule, with eligible F2/F3 preferred on equal-size
ties; valid equivalent row/inline spellings remain ordinary input.
Kind-2 entries always use ConstantSource rows. Do not canonicalize an
execution source into another source kind merely because its value agrees;
retain or explicitly discharge every original request/target/dependency
obligation. No new global value/source deduplication or unique whole-artifact
byte image is required.

## Binding, packing and consumer checks

Before affected use, validate selected framing/policy/stride/extents,
closed tags/zeros, reference namespaces and actual counts, ranges and
ownership, exact selected Body/formal ownership, kind/arity/order matching,
normalized domains/constraints and substituted inputs/result, complete
requirements, target support and original binding obligations.
Structural decoding can precede contextual binding; it cannot replace it.

Resolve each Import to its exact selected provider before complete
comparison. Correspond expected/provider owner and method formals by
**kind and ordinal after owner binding**, not names, GenericParam rows or
carrier equality. Preserve constant domains/values, constraints, defaults,
selected witnesses and every directed expectation/stronger relied-on fact.
A bodyless expectation alone cannot satisfy required execution.

Packing/remapping must decode and rebuild, not substitute matching bytes:

1. Select exact source/destination schemas and each retained using context.
   Preserve selected declaration origins, keys, complete expectations and
   actual canonical values before replacing Imports with direct references.
2. Rebuild exclusive `005E` ranges and `0042` first/count fields.
   For kind 1, remap row TypeSpecRefs through the TypeSpec map and preserve
   eligible F2/F3 grammar; for kind 2, use the **ConstantSource map**, then
   its tag-selected FrozenValueNode/request/TargetValueBinding map.
   Equal numeric words in the two variants need not map to equal words.
3. Rebuild dependent TypeSpec recipes, including their unchanged
   formal-directed constructed arguments, GenericParam/Constraint and
   declaration references, complete result/input/view types, targets,
   value trees and layout/dependency ranges. Preserve their own structural
   ordering, ownership and cycle rules; do not remap ordinals as rows.
   Selected Body row references remain separate from MethodDef DefIds,
   and `005D/26` retains its distinct identity maps.
4. Repair variable-table offsets, section counts/extents, affected Body
   maps/instruction offsets and manifest ranges. Regenerate or drop optional
   SourceMap/source data that cannot still be preserved truthfully.
5. **Revalidate after binding and after transformation:** kind/arity/formal
   correspondence, exact domain/value/input/result/owner agreement,
   original obligations, ownership, cycles and each using Module's closure.
   Cached checks apply only to unchanged inputs and the same exact context.

Malformed input, incompatible binding/type/domain and unsupported required
schemas/catalogs are distinct from selected-path failure or resource
exhaustion. Each prevents successful required publication; none selects
another overload, implicit conversion, default/unit/zero value, false proof
or runtime fallback. Resource limits remain the existing per-request four
ceilings and common accounting, not producer-reported work totals.
Mandatory safe-consumption checks do not semantically re-prove arbitrary
producer-published Body/effect/validity facts.

Following nested substitutions/sources during loading, binding or rewriting
does not reset that operation's aggregate structural work/state budget under
[interchange accounting](../specs/representation/resource-accounting.md#2-selection-and-scope).
Value formation/read/copy and entered callable work retain the existing
[constant normalization and charges](../specs/language/proof-and-constant-evaluation.md#semantic-actions-and-normalization):
a preformed constant is read without constructor replay, while semantically
repeated execution is not free because its source was cached. No new
resource recipe or exemption is created by a slot tag.

## Intrinsic contracts still missing

For source kind 2, `selectedSourceRef` and dependency kind 5 still name
row-only `005D` identities; Import/Export use DefId `26` separately.
There is no MethodDef signature hidden in that identity record.

In this successor schema all substitution rows have eight bytes, including
any independently admitted **type-only** intrinsic sequence (kind-1 slots,
registration order). This does not create such a catalog. **No intrinsic
constant-formal interpretation for kind 2 is selected here.** It remains
unsupported until a separate exact catalog contract supplies it; a future
catalog may reuse this carrier without reallocating its tags.

Separate decisions must supply:

* Exact versioned registration entries and complete formal inventory:
  arity/order/kinds, type domains/constraints, constant domains, binding and
  result rules, logical inputs and their transformations/forms/effects.
* The admitted operation/program and, for an opaque bulk intrinsic without
  an eligible checked Body, its owned/versioned compact resource-recipe
  grammar, dependencies and exact work/peak incremental storage computation.

A known `(origin Module, registrationId, ruleRevision)` with no complete
catalog does **not** imply zero arity, an empty input list or an available
recipe. Identity binding, carrier parsing, formal binding and execution
admission are separate claims. Unknown/incomplete catalogs refuse affected
use; no same-named callable, different revision or checked-Body fallback
is inferred.

## Proposed supersession and integration owners

These are coordinated **future** edits on adoption/provisional integration;
none is made to these owners by this documentation change.

| Owner | Exact replacement/addition and retained boundary |
|---|---|
| [Earlier substitution proposal](constant-execution-substitution-range.md) | For the successor selection, supersede its four-byte `u32 typeSpec` row, type-only checked-Body admission, unconditional complete-type field classification and direct-constant carrier deferral with this eight-byte tagged carrier and mixed binding rules. Retain its exact range target, slot order, exclusive ownership, canonical placement, using-context, binding and schema-selection principles as refined here. Intrinsic formal/catalog/recipe deferrals remain; its type-only intrinsic direction uses kind-1 eight-byte rows, not a simultaneous four-byte decoder. |
| [Logical generic and evaluation records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending), [evaluation range sentence](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Replace “The substitution range contains TypeSpec references ...” with an exclusive `005E` tagged actual range, checked-source formal matching and exact constant-domain typing above. Extend the nested-request dependency omission/cycle rule to substitution-resolution edges. Preserve the 80-byte request, argument/dependency tags, separate frozen result and remaining program/recipe gates. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Add required `005E`, stride 8 and exact variant fields, `0042`'s range target and contextual validation. Preserve GenericParam's 32 bytes, ConstantSource's eight bytes/tags and the unchanged four-byte formal-directed constructed-TypeSpec/default encodings; do not globally tag those arguments. |
| [Registry prose](../specs/representation/registry.md), [machine registry](../specs/representation/trrm-1.registry.json) | Add `metadata.sections` candidate `005E`, policy `0000`, stride 8, fields `u8 actualKind, u8 flags=0, u16 reserved=0, u32 actualRef`, tags `1=TypeActual`, `2=ConstantActual`; add `0042.substitution_range_section="005E"`. In `metadata.type_reference.complete_type_fields`, classify **only** `ConstantExecutionSubstitution.actualRef[actualKind=1]` as complete type; register the kind-2 target as required row-only `0030`. Record exclusive ownership. Add no DefId/token/TypeSpec form/Feature or completed Feature-12 registration. |
| [Reference verification](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces), [canonicality](../specs/representation/encoding-canonicality.md) | Add the conditional payload namespaces, positional/exclusive range rules, source/value identity comparison and context-aware decoded remapping. Preserve ordinary valid noncanonical acceptance and producer truth versus mandatory consumer checking. |
| [Binding](../specs/representation/binding-and-replacement.md), [requirements](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md) | Apply existing exact formal kind/ordinal/domain correspondence and original-expectation preservation to every slot and nested source. Extend per-using-Module retained traversal to direct constant actuals; no artifact-wide feature union or new unconditional prerequisites. |
| [Complete types](../specs/language/types-and-normalization.md), [constant generics](../specs/language/constant-generics.md), [constant execution](../specs/language/proof-and-constant-evaluation.md) | Cross-reference the carrier on integration, without widening source domains, changing construction/default rules or weakening ConstEval, staged-target, no-fallback and resource contracts. Constant-domain semantic/value codecs remain independently required. |
| [Binding conformance](../specs/conformance/il-binding-and-replacement.md), [execution conformance](../specs/conformance/source-proof-and-constant-evaluation.md), [registry conformance](../specs/conformance/il-registry.md) | Extend IBGR-33 and EREC-62/63 with the exact mixed range, constant typing and required-resolution cycle cases; add conditional-reference, schema-selection and re-encoding vectors. Keep constructed TypeSpec's existing formal-directed namespace cases and separate intrinsic admission gates. |
| [Intrinsic identity proposal](intrinsic-registration-identity.md), [static persistence proposal](immutable-static-field-persistence.md) | Close only the checked-Body direct-mixed carrier deferral in the composed design. Preserve `005D/26`, missing intrinsic formal/input/catalog/recipe contracts, static `0055..005C`, separate OwnerRefs and publication/runtime gates. |

Exchange must select **one complete exact successor draft/catalog out of
band** under [format versioning](../specs/representation/format-versioning.md).
The current four-byte `GeneralPredicateGraphsDraft` implementation is not
this eight-byte schema. Agree the replacement before reading rows; a shared
profile name cannot mean both layouts. Equal `(1,0)` bytes, Feature 12,
section presence, row count or plausible stride cannot choose a decoder.
No version bump, historical reader, dual-layout fallback, compatibility
alias or draft migration is required.

## Implementation and conformance consequences

The present code is evidence of a **provisional type-only structural
slice**, not this proposal's implementation:

| Current source | Consequence for implementing this successor |
|---|---|
| [Substitution table](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantExecutionSubstitutionTable.cs), [artifact index](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | Currently require four-byte TypeSpecRef rows at `005E`. Replace the selected candidate stride, typed row, reader/writer and fixtures together; decode each tag's payload before contextual binding. Do not accept both strides under one schema. |
| [Request validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantExecutionTable.cs) | Currently counts owner plus method formals and explicitly requires kind 1. Its direct result comparison covers only nongeneric sources. Replace the kind-1 gate with full kind-directed substitution/domain/constraint and substituted result/input checks, not just acceptance of tag 2. Extend cycle and using-context traversal through all slots and sources. |
| [GenericParam reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericParamTableReader.cs), [constructed-type validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstructedTypeSpecValidator.cs) | Already distinguish formal kinds and constructed argument namespaces structurally. Reuse shared declaration/formal resolution and exact normalization; their bounds checks do not prove constant-domain eligibility or executable constraints. Preserve their existing wire shapes. |
| [ConstantSource reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceTable.cs), [value validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) | Tags/bounds exist; selected value consumers are bounded and primarily frozen-scalar based, with some kind-2 use collection. Add context-aware exact substituted constant typing and admitted kind-2/3 handling. Do not inherit the wider frozen-scalar domain as the constant-formal catalog or claim missing enum/wrapper codecs are implemented. |

Perform shape/bounds decoding, contextual binding and finite dependency
resolution as separate before-use obligations; table-count references must
not force recursive reader construction that bypasses cycle checks.
Carry mixed facts through shared semantic/bound-tree traversal,
verification, persistence/dependency binding, rewriting and ordinary
checked-Body specialization/lowering. Do not add a sample/API recognizer,
host-object constant representation or second evaluator instruction stream.
Update support inventories when implementation evidence exists, not merely
because this proposal is written.

Future regression tests should use the existing public artifact/compiler
entry points and TRRM fixture patterns, including the
[general-graph/request fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGeneralPredicateGraphTableTests.cs),
[constructed bindings](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmConstructedTypeSpecBindingTests.cs)
and [constant value bindings](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmConstantSourceValueBindingTests.cs).
The following are acceptance cases, **not executed tests or support claims**.
Execution cases require all independently needed codecs/programs/targets.

| Case | Required outcome |
|---|---|
| Nongeneric source; type-only, constant-only and interleaved owner/method blocks | Empty is `(0,0)` only; otherwise exactly one correctly tagged entry per formal in owner-then-method order. Unit ordinary parameters retain their argument slots. |
| Generic owner on a static source; method-only generic source; same ordinal in both blocks | Keep the former's complete owner prefix, omit only genuinely empty blocks, and bind distinct declaration/ordinal identities. |
| Repeated type/constant values and defaults; valid full-range relocation | Keep separate owned slot rows and explicit selected defaults. Valid remapped range placement is ordinary input; canonical writers group by request order. |
| Type actual using equivalent row/F2/F3; nested constructed constant actuals | Preserve complete type identity and conditional payload namespaces, with all nested requirements and domain checks. |
| Fixed integers at signed/unsigned endpoints, zero and values above 64 bits; Boolean false | Preserve exact typed values without truncation/wrapping. Zero/false use nonzero ConstantSource references, never missing-slot markers. |
| Admitted enum aliases or eligible wrapper values | Equal values agree only in the same complete domain; underlying integers, another enum/wrapper or another complete wrapper construction do not match. Missing required domain/value schemas refuse support. |
| Admitted ConstantSource kinds 1, 2 and 3 | Match the exact substituted constant type; kind 2 completes before use, kind 3 preserves exact target/layout obligations. No implicit replay of already frozen construction. |
| Owner/method substitutions alter constructed result or input types | Validate full substituted result, receiver/parameters/contracts and exact value/view/init forms, not only nongeneric results. |
| Missing/truncated/zero-count section, wrong policy/stride, unknown tag, nonzero reserved, zero or wrong-namespace payload, malformed F2/F3 | Reject before affected use; no decoder guessing or reinterpretation as another table. |
| Empty `(1,0)`/`(0,1)`, high-byte first, endpoint overflow, one-past-table, shared/overlapping/orphan ranges | Reject invalid structure without wrapping. Exercise first/last row and `00FF_FFFF` bounds with bounded fixtures, not a claimed portable allocation guarantee. |
| Wrong formal owner/kind/count/domain/constraint, skipped unused/default formal, type/constant block partition | Reject incompatible binding; do not project, infer or reorder to repair it. A same-kind permutation that remains well typed denotes a different request, not detectable author intent. |
| ConstantSource type int64 for int32 formal; valid float/unit/native-width scalar; raw enum/wrapper carrier | Reject exact-domain mismatch or ineligible constant-formal domain even if scalar storage/value decoding succeeds. |
| Pure-only/bodyless/anchor/token source, incompatible substituted result/argument, invalid init/view form | Refuse invalid source or form/binding; no implicit ConstEval, body lookup by name or runtime fallback. |
| Direct/indirect request cycle through substitutions, nested constructed constants or view backing | Reject required-resolution cycles; ordinary eligible Body recursion still follows common fuel/depth/storage accounting. |
| Unavailable target/value/witness contract, profile mismatch, selected failure or envelope exhaustion | No successful required value; preserve distinct failure categories and exact ceilings, without another target, zero value or runtime fallback. |
| Imported source/types/constants, then packed/remapped rows with coincident TypeSpec and ConstantSource numbers | Apply tag-selected maps, preserve kind/ordinal/domain/origin/value and every original expectation, then repeat binding/ownership/cycle/requirement checks. |
| Two using Modules reach the same source/value helpers but only one declares a retained actual-use capability | Validate independently; neither source/provider manifests nor row-only caches repair the deficient Module. |
| Known `005D/26` identity with absent intrinsic formal/catalog/recipe contract | Remains unsupported, including purported zero-arity use. This proposal supplies no intrinsic constant-formal admission. |

The checked-Body carrier, tags, width, reference namespaces, ownership and
binding rules are concrete provisional direction. Approval, full
Feature-12 registration, intrinsic catalog/recipe decisions and compiler
conformance remain separate.
