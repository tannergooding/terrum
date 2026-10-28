# ImmutableData Feature admission

> [!IMPORTANT]
> **Unapproved proposal; provisional implementation direction under the
> authorized missing-contract workflow.** This document changes no normative
> record, registry allocation or compiler behavior. It selects admission and
> DefId-target policy, not approval, complete codec registration or support.

## Decision and boundary

Recommend candidate **`19/1=ImmutableDataIdentities`**, governing edition
**`1`**, with **no unconditional Feature prerequisites (`[]`)**. It owns
retained section `0x0041` ImmutableData identities, their declaration/owner
resolution, comparison, binding and remapping, and their use as immutable
read roots or constant-input dependencies. It is **independent of Feature
17**; a general predicate using it requires both capabilities.

Preserve the [general-predicate candidate](general-predicate-graph-schema.md):
scalar-only `11/1 -> {}`, selector-only `10/1 -> {}`, `17/1 -> {10/1}`,
and pending `12/1 -> {11/1,17/1}`. Neither 11, 13 nor 17 admits ImmutableData
by itself. Do not give 19 an unconditional value, graph, union, evaluator,
layout or static-execution prerequisite: those depend on the retained use.
Its required declaration, value, witness and publication representations
remain real support prerequisites even when they have no registered Feature.

The [accepted logical owner](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
currently assigns ImmutableData to `TypedFrozenValues` (`11/1`), with
`11 -> 9` and `10 -> 11`. The scalar and graph proposals intentionally
replace those edges and exclude ImmutableData, but do not select its new
owner. **This proposal replaces that ownership sentence and closes that
Feature-admission deferral, composing with the exact DefId candidates below.**
It does not broaden frozen-value kinds, complete Feature 12, or choose missing
declaration/publication codecs.

Preserve the separate unapproved [declaration-reference](immutable-data-declaration-reference.md)
and [definition-kind](immutable-data-definition-kinds.md) candidates:

| Existing row/position | Candidate identity contract preserved |
|---|---|
| `0041`, 12 bytes | `u8 kind, u8 flags=0, u16 reserved=0, u32 declarationDefId, TypeSpecRef ownerTypeSpec?`; no new fields |
| Kind `1=ResolvedConstant` | Direct DefId `0x22=InterfaceConstDef` or `0x23=WitnessAssociatedConst`, the latter retaining its exact selected witness binding |
| Kind `2=PublishedImmutableRegion` | Direct DefId `0x24=StaticField` plus separately admitted immutable extent/publication/backing and load-lifetime contracts |
| `0039` PredicateReadRoot kind `6` | Required nonzero, high-byte-zero, one-based `u32` reference to `0041`; not a declaration DefId, ConstantSource, FrozenValueNode or address |
| Owner | Zero exactly when no applicable owner substitution exists; otherwise the exact complete owner construction under the declaration-reference proposal |

This owns the **ImmutableData identity rows**, not every InterfaceConstDef,
associated binding or StaticField declaration independently of its use.
Those declaration families retain their own representation gates. A
StaticField alone is not an immutable publication, and a resolved constant
is not addressable runtime storage.

## Exact DefId targets and identity reuse

Select the companion definition-kind proposal's **unapproved** mappings as
part of this combined direction, not as a second alternative numbering:

| Candidate DefId kind | Exact target | ImmutableData kind | Import/Export |
|---|---|---|---|
| `0x22` | InterfaceConstDef, section `0x0031`, 16-byte row | 1 only | Add to both target sets, with complete expected-definition/access and exact owner binding |
| `0x23` | WitnessAssociatedConst, section `0x0033`, 16-byte selected binding row | 1 only | Neither; correspondence follows the exact selected origin witness/binding graph |
| `0x24` | Distinct logical StaticField table, approved candidate 20-byte row; physical section still unassigned | 2 only | Add to both only with the complete static expectation/provider representation |

Encode `(kind << 24) | row` as little-endian `u32`, with one-based row
`1..0x00FF_FFFF` inside the selected table. Kind 1 permits exactly `22/23`,
kind 2 exactly `24`; neither accepts `0x1C` Import tokens, raw typed rows,
section-derived tags or `0xFF` indirection. Preserve existing
`0x13/14/16/19/1E/21` assignments and the separate unapproved
`0x20=AssociatedTypeDef` candidate. No associated-constant requirement kind
is assigned: an unbound AssociatedConstDef is not a resolved binding.

The `0x24` target is **not** FieldDef, InterfaceMethod section `0x0024`,
an initializer MethodDef or a state/group record. Its missing physical
section/static codecs remain a genuine separate decision; this exact
definition-kind mapping does not make arbitrary existing rows decodable as
StaticField. The companion owner/substitution, value, access, witness-origin,
expected-definition and publication rules apply unchanged.

| Identity design | Assessment |
|---|---|
| Reuse InterfaceConstDef and selected WitnessAssociatedConst identities, with distinct `22/23`, plus separate static identity `24` | **Recommended.** Reuses the existing fixed-value and exact witness-binding rows, adding no constant table or witness selector to ImmutableData. Three DefId kinds distinguish three target tables even though ImmutableData has only two semantic tags. |
| One canonical ConstantDef identity for fixed constants and selected bindings | Coherent if a single definition table with explicit fixed/bound roles and complete witness/member/owner references replaces or wraps the current two families. Then one constant DefId kind could suffice. Merely using the same tag for the two current row namespaces is ambiguous; the coherent redesign needs a new table/indirection and remapping contract with no width saving here. |
| Reuse `0x14` through a unified instance/static FieldDef identity | Coherent only as an intentional redesign of the common definition table, role discrimination, owner/range/layout participation, static access/dependencies and binding rules. It would replace the accepted distinct-static model; the current static bit alone does not implement that redesign or establish immutable publication. Larger than selecting `24` for the existing logical StaticField identity. |
| Only fixed constants and static regions: `22/24` | A valid narrower implementation slice, not completion of the selected witness-capable kind-1 contract. Do not reinterpret a requirement, witness row or equal scalar as the missing `23` binding identity. |

Reassigning draft kinds is permitted, including moving the `20` type
candidate or `21` union assignment to make another grouping. It removes no
identity distinction or shrink the fixed four-byte word, so no such repack
is selected. No additional kind or family band is reserved.

## Feature allocation and alternatives

Feature IDs below are decimal; section/DefId codes are hexadecimal and
occupy different namespaces. The [registry](../specs/representation/trrm-1.registry.json)
registers native 1–7 and layout 13; 9–12 are assigned with incomplete
registrations. Current proposals use 8 for native outputs, 14 for root/pin,
15 for copy-origin, 16 for foreign invocation, 17 for general predicates
and [18 for nominal enums](nominal-enum-persistence.md#allocation-and-representation).
No current assignment or candidate examined assigns Feature 19. Its DefId
candidates are exactly those above; it adds no section, clause, opcode,
TypeSpec form or family reservation.

| Alternative | Assessment |
|---|---|
| Expand the candidate `11/1` to scalars plus ImmutableData | A legal intentional pre-v1 amendment, but reverses the selected scalar-only boundary and couples value-only consumers to declaration/witness/publication identity. An explicit scope change, not interpretation of the current scalar candidate; no encoding-width benefit. |
| Richer exact `11/2` | Conflicts with exact `11/1` required by candidate 12 and 18 in one Module. A larger revision is not compatibility; revising those compositions or defining a directed mapping is larger than this decision. |
| **Separate `19/1`, prerequisites `[]`** | **Recommended.** One shared identity owner composes with predicates, values and constant views without making any one use its definition. Retains the selected scalar scope. |
| Put ImmutableData in 17 or 13 | General graphs are a use, not the declaration identity; constant dependencies/views share that identity. Layout queries/path binding do not establish immutable publication. Neither is the appropriate unconditional owner. |
| Repack assigned 9–13 or displace another candidate | Permitted: unpublished numbers are not frozen or reserved. Regrouping these identities requires corresponding catalog, manifest and fixture changes but adds no shared codec or smaller field. Not justified for this admission boundary. |

## Per-Module detection and closure

Inspect retained declarations and expectations, contracts/read bounds,
graphs and helpers, complete types/constant arguments, value sources,
required programs, views and dependencies in **every using Module/formal
context**. Include uncalled/bodyless declarations, unselected graph branches,
allowed-root entries not directly loaded by a node, and dependencies retained
after folding. Do not limit detection to machine calls or executed reads.

Require effective **19/1** whenever this closure reaches an ImmutableData row:

* PredicateReadRoot kind 6, in either a graph's actual footprint or an
  EvaluationReadBound's allowed footprint;
* ConstantExecutionDependency kind 6;
* LogicalInputView backing kind 2, including views reached through required
  execution arguments;
* Any other already-admitted typed contract reference retaining that same
  identity. This rule creates no new attachment or reference alternative.

ImmutableData has no Module cursor. Each typed use supplies its context;
follow an expected declaration's original Import/witness binding. A target
provider retains its own requirements, which cannot supply the using
Module's missing 19. Conversely, a provider's private use does not add 19
to a consumer whose own retained metadata/expected contracts contain no such
identity; admit the provider's manifest separately. A physically present but
unreferenced identity row has no invented using Module and does not activate
19; it still obeys the selected schema. Merely retaining a target declaration
does not attach an otherwise unused ImmutableData row to it.

Add **only the requirements actually retained**, then derive least exact
closure. In particular:

| Retained use | Required effective pairs under the combined candidate |
|---|---|
| Immutable identity, independently of a graph | `19/1`; this is the identity capability, not a new standalone retention hook or a claim that its declaration codec is complete |
| Phase-1/2 graph or Pure allowed bound with root 6 | `{10/1,17/1,19/1}`, plus the identity's actual value/witness/publication dependencies |
| Same use retaining a fixed-scalar constant source/value | `{10/1,11/1,17/1,19/1}`; 11 is required for the retained value, not derived from 19 |
| ConstantExecution dependency kind 6 or input view backed by ImmutableData | `{10/1,11/1,12/1,17/1,19/1}`, plus actual program/view/target dependencies; 12's incomplete registration is not completed here |
| Any preceding use with retained layout-query/TypeIdentity/FieldPath or FieldDef Import/Export facts in Feature 13's scope | Add `13/1`; ordinary Layout rows, owner TypeSpecs, DefId `22/23/24` or a static region alone do not add it |
| Resolved constant with a candidate nominal-enum value | Add `18/1` and its `11/1` prerequisite; enum members do not thereby become allowed ImmutableData declaration targets |
| Phase-3 selector or function-pointer anchor attempts root 6 | Invalid under the selector-only applicability contract, even with 19 and 17 present |

19 has no edge to 17, nor does 17 acquire an edge to 19. Current graph users
need both; constant execution also derives 17 through 12's separate
prerequisite. A bare Feature declaration grants no storage, read, evaluation
or publication authority. Union, native, root/pin and other requirements
remain conditional on their own retained forms.

Physical prerequisite rows may be omitted. Ordinary reordered lists and
identical duplicate pairs remain equivalent; conflicting revisions do not.
Canonical explicit lists sort by unsigned ID without repeats. Packing keeps
these requirements per Module, not as one artifact-wide union.

### Type-pattern, call and constant inputs

Preserve the graph proposal's typed namespaces:

* PredicateTypePattern **kind 8=ConstantArgument** and PredicateCall generic
  argument **kind 2** reference ConstantSource, not ImmutableData.
* Predicate literal operand **02** references a FrozenValueNode root, not a
  declaration. A formed literal with no surviving declaration dependency
  needs its value capability, not 19 merely because compilation used a
  constant declaration.
* Conversely, if those sources, selected calls, owner/argument constructions
  or value dependencies retain an ImmutableData identity, follow it and
  require 19 in that using Module too. ConstantSource tag 2's execution
  dependencies and LogicalInputView backing are examples; a scalar payload
  cannot replace an expected constant/witness/publication obligation.

There is no direct ImmutableData ConstantSource tag, new predicate operand,
constant-argument capture or name-based declaration search here. Numeric
kind 8 in a type pattern is unrelated to read-root kind 8=PhaseCapture.

## Consumption, replacement and transformation

Before affected binding, lowering or execution, consumers must check the
selected exact schema/catalog, Module closure, section policies/strides and
checked extents, closed tags/zero reserved fields, 24-bit row bounds,
per-kind DefId targets, complete owner substitutions, required declaration/
value/witness/publication references and directed binding compatibility.
Recognizing 19 or a 12-byte row does not supply a missing codec. Follow the
[reader-trust boundary](../specs/representation/reader-trust.md): producers
establish truthful values, immutable extents and publication facts; ordinary
consumers need not re-prove those facts against Bodies or foreign behavior.

Compare identity by kind, resolved declaration/selected binding and
normalized complete owner substitution, not rows, names or equal values.
Preserve each expected provider/export key and selected witness origin.
Duplicate identity rows may be equivalent; duplicate semantic roots in one
footprint remain invalid. Replacement must preserve the exact promised
typed value/domain/witness and any stronger relied-on facts, or the region's
extent/publication, loaded closed-owner/storage generation, backing,
initialization effects, cleanup and unload dependencies. Binding does not
run initialization, publish a failed unit or extend a lifetime.

Rewriters remap DefIds by their selected table/kind, owner TypeSpecRefs and
nested arguments, definitions/Imports/witnesses/value sources, then every
0041 reference in roots, executed dependencies and views. Clone shared rows
when using contexts would differ. Rebuild exclusive ranges, semantic
ordering/duplicate checks, variable indexes, section extents and Module
Feature ranges; rebuild Body maps/code and affected SourceMap slots if
lowering changes them. Preserve original expectations after internalizing
Imports or folding values. Never replace matching bytes inside payloads.
Keep actual observations, allowed bounds, executed dependencies, layout and
ordinary backing/static dependencies under their distinct owners.

## Owner integration and remaining gates

Future adoption must coordinate these exact amendments; none is made here:

| Owner | Proposed change |
|---|---|
| [Logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms), [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Replace 11's ImmutableData ownership with 19; add the exact `22/23/24` target sets/mappings above, preserving row shapes and the companion identity/owner rules |
| Registry definition/reference projections | Add `22 -> 0031`, `23 -> 0033`, `24 -> StaticField`; extend Import/Export target sets by **22/24 only**. Do not assign StaticField's missing physical section by analogy or widen unrelated polymorphic fields |
| [Machine registry](../specs/representation/trrm-1.registry.json), [registry prose](../specs/representation/registry.md#4-admission-status-and-namespace-boundaries), [manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Add exact 19/1, governing editions `[1]`, prerequisites `[]`, this identity scope and detection; mirror both Feature projections, preserving the selected scalar/graph/enum compositions |
| [Graph proposal](general-predicate-graph-schema.md#complete-feature-closure-and-per-module-detection) and companion ImmutableData proposals | Supersede only the Feature-owner/closure deferral with this candidate; preserve their separate declaration, witness, value and static/publication gates |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), reader/reference and transformation owners | Apply the identity, expected-binding, before-use and remapping rules above to every retained use |

Select the eventual complete combined draft/catalog out of band under
[format versioning](../specs/representation/format-versioning.md). Equal
`(1,0)` bytes or a Feature list do not select a decoder. No compatibility
alias, historical reader or numeric roll-forward is proposed.

The provisional [graph validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmGeneralPredicateGraphValidator.cs)
now structurally decodes candidate 0041 rows and requires the using Module's
19/1 for ImmutableData root 6 in both actual graph footprints and
EvaluationReadBound allowed footprints. The reader checks the closed kind and
target-row bounds; it does not yet bind the target's exact owner, declaration,
witness/provider expectations or value facts. Kind-2 StaticField rows remain
unavailable until the separate physical/publication candidate is adopted and
implemented. Add 13 only for actual Feature-13 content; missing semantic
binding or publication support still refuses affected use.

**No further Feature-ownership choice is left to implementation by this
recommendation.** Separate unapproved proposals now provide concrete
candidates for [AssociatedConstDef identity and selected bindings](associated-constant-definition-identity.md)
and [StaticField/static-publication persistence](immutable-static-field-persistence.md);
they do not approve or implement those contracts. Effective restrictive
member/source-unit access, witness-origin/expected-binding runtime carriers,
indirect publication backing, richer values and Feature 12 retain independent
gates. Thus 19 remains an unapproved candidate, not a completed executable
registration. Source syntax, runtime publication, libraries, targets and
native ABI are unchanged.

## Required candidate cases

These are future complete-schema cases, not tests run or support claims.
Assume separately needed representations are selected and supported unless
the case tests their absence.

| Case | Required outcome |
|---|---|
| Explicit manifest containing only `19/1` | Derive exactly `{19/1}`; no new retention hook is implied |
| Uncalled, bodyless Pure declaration with root 6 only in its allowed EvaluationReadBound footprint | Require 19 for the using Module despite no corresponding actual-footprint root |
| ConstantExecution dependency kind 6 only, tested with and without using-Module 19 | Preserve the exact identity; require 19 when present and refuse the affected use when absent |
| LogicalInputView backing kind 2 only, tested with and without using-Module 19 | Preserve the exact identity; require 19 when present and refuse the affected use when absent |
| Fixed constant root 6 with int32 value; same identity in actual and allowed footprints | Require 19/17/10 and retained scalar 11; use separate footprint rows and one equivalent resolved identity |
| Scalar literal/type-pattern/call constant argument with no retained ImmutableData dependency | Require its value and surrounding graph/call capabilities, not 19; scalar-only 11 admits no 0041 use |
| Published-region root without literal or Feature-13 content | Require 19/17/10 and complete publication/static dependencies, not unconditional 11/13; readonly/static bits alone fail admission |
| Required execution dependency kind 6 and input view backing kind 2 naming the same identity | Preserve that identity and complete 12/11/17/10/19 closure; no duplicate identity system or initializer replay |
| A type-pattern/call ConstantSource retains such a request/view; branch is unselected | Require 19 and structural closure despite skipped execution; do not execute the branch to discover requirements |
| Missing using-Module 19; provider or another packed Module has it; only 11/13/17 supplied | Refuse affected use; no inferred ownership, manifest union or scalar/graph widening |
| Provider privately uses ImmutableData; consumer retains no such identity; unused 0041 rows are co-located | Admit each Module's own closure; neither table presence nor the provider's private requirement adds 19 to that consumer |
| Selector-only phase 3 or anchor names root 6 | Reject applicability, even with complete Feature closure |
| Kind 1 with `22/23`, kind 2 with `24`; wrong kind/DefId family, standalone `23` Import/Export, Import token, bad reserved fields or owner | Accept only the selected mapped families with their complete supporting contracts; reject wrong namespaces/targets before use |
| Zero/out-of-bounds/high-byte typed row reference; valid `24` with no selected StaticField section/publication codec | Refuse affected use; missing target/publication codecs are not inferred from plausible rows |
| Equivalent row/F2/F3 owner spellings where valid; equal-valued distinct declarations, owner constructions or witness bindings | Normalize valid complete types; preserve distinct declaration/binding identities and exact domains |
| Reordered/duplicated identity rows; duplicate semantic roots; first/last permitted 24-bit rows | Accept equivalent valid row permutations and in-bounds endpoints; reject duplicate footprint identities and overflow |
| Nominal enum constant or retained FieldPath added | Add 18/11 or 13 respectively, without granting enum-member targets, union support or general layout-free access |
| Replacement changes promised value bits, witness, extent, trigger or load lifetime; pack shared rows under different bindings | Check every retained expectation/fact; reject incompatible replacement, clone/remap context-sensitive rows and preserve dependencies |
