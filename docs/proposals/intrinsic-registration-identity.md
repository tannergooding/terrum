# Intrinsic registration identity and resolution

> [!IMPORTANT]
> **Unapproved proposal; provisional implementation direction under the
> authorized missing-contract workflow.** This document changes no normative
> specification, registry or source code. Identity resolution and requirement
> traversal are not intrinsic admission, a completed Feature 12 registration,
> execution support or qualification evidence.

## Decision and boundary

**Recommend one dedicated, Module-owned identity table:** candidate required
section **`0x005D=IntrinsicRegistration`**, a **16-byte** row, and candidate
**DefId kind `0x26`** for its Import/Export bindings. Its stable identity is
the tuple **`(origin Module semantic identity, registrationId, ruleRevision)`**.
The last two components are explicitly assigned nonzero `u32` semantic keys,
not row numbers or implementation versions.

Keep the assigned four-byte reference positions **table-selected**:

* `ConstantExecution.sourceKind=2` selects an IntrinsicRegistration row.
* `ConstantExecutionDependency.kind=5` selects an IntrinsicRegistration row.
* Cross-Module source compilation references a local expected registration
  row with an ordinary Import. After complete binding, packing may use the
  provider's row directly while preserving retained obligations.
* `ConstantExecutionDependency.kind=6` still selects ImmutableData. Visit it
  in the **request's using Module**, not whichever Module owns its selected
  intrinsic or happens to be visited first.

This closes the identity/reference/Module-origin choice only. It adds no
intrinsic signature, input-transformation or resource-recipe payload, actual
intrinsic catalog entry, Body opcode, runtime helper, private codec or source
annotation. No MethodDef, ordinary Body, source name, export-key spelling or
native symbol is reinterpreted as an intrinsic registration.

## Baseline and interacting decisions

The [specification index](../specs/README.md) leads to the
[logical record owner](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
and [machine registry](../specs/representation/trrm-1.registry.json).
They already assign `0042` ConstantExecution, stride 80, source tags
`1=CheckedBody`, `2=Intrinsic`, and `0044` ConstantExecutionDependency,
stride 8, including `5=IntrinsicRegistration` and `6=ImmutableData`.
The logical owner explicitly leaves intrinsic registration reference details
and the recipe schema open. This is a genuine missing contract, not authority
to infer identity from a missing Body or recognizable method name.

Preserve the following **unapproved** companion directions when composing
this proposal:

| Candidate owner | Preserved boundary |
|---|---|
| [Scalar frozen values](scalar-frozen-value-registration.md) | Closed scalar-only `11/1`, no unconditional prerequisites; a frozen result is not its execution request or intrinsic identity. |
| [General predicate graphs](general-predicate-graph-schema.md#complete-feature-closure-and-per-module-detection) | Selector-only `10/1 -> {}`, general `17/1 -> {10/1}`, and still-incomplete `12/1 -> {11/1,17/1}`. No return to the accepted draft's older `10 -> 11 -> 9` chain in this composition. |
| [ImmutableData admission](immutable-data-feature-admission.md#per-module-detection-and-closure) | Independent `19/1 -> {}`, with use-dependent closure through kind 6 and input-view backing kind 2. Neither 11 nor 12 nor 17 alone admits ImmutableData. |
| [ImmutableData references](immutable-data-declaration-reference.md), [definition kinds](immutable-data-definition-kinds.md), [associated constants](associated-constant-definition-identity.md), [static persistence](immutable-static-field-persistence.md) | Preserve the exact declaration/owner/witness/publication contracts and their remaining gates. Kind 6 does not become a direct StaticField, ConstantSource, Blob or registration reference. |

These are composition prerequisites, not normative adoption. The current C#
sources contain no IntrinsicRegistration implementation. The
[artifact index](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs)
admits static sections `0055..005C` only under the unapproved
`GeneralPredicateGraphsDraft` selection; that prototype approves neither
those numbers nor this addition.

## Allocation audit and alternatives

Codes here are hexadecimal except decimal Feature IDs. Sections, DefId kinds,
clauses, predicate operations and Body opcodes are different namespaces.

| Namespace | Existing accepted or selected candidate occupants; proposed delta |
|---|---|
| Required sections | Registry owners run through `004B`, plus `7F00=SourceMap`. Candidates use `004C` for foreign-interface identity, `004D..0052` for general graphs, `0053/0054` for enums, and `0055..005C` for static persistence. **Select `005D`**, required policy `0000`; no examined registry/proposal assigns it. |
| DefId kinds | Registry has `13/14/16/19/1E/21`; companions select `20` associated types, `22/23/24` immutable declaration targets and `25` associated-constant requirements. **Select `26 -> section 005D`**. Section `0026=InterfaceBase` is unrelated. |
| Other allocations | None. Preserve Feature 12's pending status and candidate Feature 19. Do not add a Feature, clause, TypeSpec form, Body-map token, opcode, prefix, escape or family reservation. |

The current draft assignments can be moved. Choosing `005D/26` avoids
unrelated changes, not a supposed compatibility obligation. In particular,
`0055..005C` are actively implemented **unapproved** candidates, not normative
registry owners and not frozen numbers.

| Coherent alternative | Assessment |
|---|---|
| **Dedicated owned row with explicit semantic key and existing Import/Export mechanism** | **Recommended.** Keeps intrinsic identity independent of source/runtime callables, provides an unambiguous owner without a new Module cursor, and preserves both assigned row-reference positions. No signature or recipe is duplicated. |
| MethodDef/Import-based registration | Coherent only with a new explicit registration contract: for example, `(resolved MethodDef identity, registrationId, exact revision)` plus a typed attachment and unique selection rules. Method Imports would establish callable correspondence, but the attachment would still have to define registration identity, multiplicity, ownership and recipe binding. Making a MethodDef/Body token valid in the two existing reference positions would also replace their assigned target family. The dedicated table permits multiple registrations per callable without forcing every intrinsic to acquire a source/runtime MethodDef. |
| Use the imported method, its name, body hash or native symbol alone | Not a completion of the gap. None distinguishes an exact versioned intrinsic contract, input transformation or cost recipe. ConstEval/Pure opt-in is not registration. |
| Repack this and the static/graph/enum families into another band | Permitted with coordinated projections and fixtures, including moving the static candidates. It removes no identity distinction and saves no bytes in fixed `u16`/`u32` fields; not selected here. |

## Exact candidate wire contract

All multibyte fields are little-endian. `R(T)` is a required one-based `u32`
row in table T, in `1..0x00FF_FFFF`, high byte zero, and within the actual
table count. It is not a stable semantic identity, DefId or Import token.

Section `005D` concatenates fixed rows, with no row header, offset array,
padding or recipe trailer:

| Offset | Bytes | Field | Exact meaning |
|---:|---:|---|---|
| 0 | 4 | `R(Module) ownerModuleRef` | Physical declaration/expectation owner; not ModuleRef or a producer string reference. |
| 4 | 4 | `u32 registrationId` | Nonzero stable key assigned in the **origin Module's registration namespace**. All 32 bits are available. |
| 8 | 4 | `u32 ruleRevision` | Nonzero exact revision of that registration's semantic contract. No numeric compatibility or roll-forward. |
| 12 | 1 | `u8 declarationRole` | Exactly `1=OwnedDefinition` or `2=ImportedExpectation`; other values are invalid. |
| 13 | 1 | `u8 visibility` | Existing visibility values `0=private`, `1=internal`, `2=public`; other values are invalid. |
| 14 | 2 | `u16 reserved=0` | No trust, recipe-presence or runtime-implementation flag. |

The directory count is `1..0x00FF_FFFF`; byte length equals `16 * count`
under nonwrapping extent arithmetic. An absent table is allowed only when
no retained reference/definition requires it. Every present row has a valid
Module owner; no new start cursor is added to the 48-byte Module row.

| Position | Exact four-byte encoding |
|---|---|
| IntrinsicRegistration DefId | `(0x26 << 24) \| r`, selecting `005D` row r. |
| `Import.expectedLocalDefId` | Direct `26` DefId of a local expected registration. |
| `Export.localDefId` | Direct `26` DefId of a public owned registration, never an expectation. |
| `ConstantExecution(sourceKind=2).selectedSourceRef` | **`R(IntrinsicRegistration)` only.** |
| `ConstantExecutionDependency(kind=5).targetRef` | **`R(IntrinsicRegistration)` only.** |
| `ConstantExecutionDependency(kind=6).targetRef` | Unchanged **`R(ImmutableData)`**, section `0041`. |
| `ConstantExecution(sourceKind=1).selectedSourceRef` | Unchanged **`R(Body)`**, not MethodDef. Its owning MethodDef supplies checked-source origin. |

For example, registration row 1 is `01 00 00 00` in either table-selected
position, but `01 00 00 26` in Import/Export's definition field. A `1C`
Import token, `26` DefId, MethodDef/Body token, address or section-derived
tag is invalid in either registration-row position. DefId 26 is **not**
added to GenericParam owners, TypeSpec operands, MethodMap, FieldMap or
unrelated polymorphic fields.

Registration rows own no ConstantExecutionDependency range. Keep `0044`'s
exclusive ranges owned by execution requests, with their existing eight-byte
shape and tags 1..9. Do not turn actually reached execution dependencies
into a registration-wide potential-read set or a general dependency graph.

## Semantic identity, origin and binding

An owned registration's origin is `ownerModuleRef`'s Module semantic identity.
Its author assigns `registrationId` explicitly and preserves it when
reordering rows, renaming implementation code or replacing an implementation.
It is not derived from a row ordinal, MethodDef, string, hash or native
address. The accepted ordinal decoded-text Module identity is the origin
component, not a source member name or evidence of producer authenticity.

The registration's exact `(origin, id, revision)` selects one immutable
semantic contract in the consuming environment's explicitly admitted
registration catalog. That contract must ultimately determine the actual
intrinsic, logical input/formal coordinates, permitted forms/effects,
transformations, dependencies, target observations and resource rules.
The identity row is a reference to that contract, **not its missing recipe
serialization or a producer-supplied authorization to execute a helper**.
Unknown/incomplete catalog entries can be identified structurally, but do
not establish complete semantic binding or constant-execution support.

An owned row has role 1 and cannot be an Import's expected definition.
An expected row has role 2 and requires its Import; neither role is inferred
from a missing Body or a catalog lookup:

1. A role-2 local expected row E has owner U, public visibility, and exactly
   one Import whose `expectedLocalDefId` is E's `26` DefId. Its ModuleRef
   belongs to U under the existing Module partition rules. E defines no new
   local intrinsic; its key/revision describe an expectation of the selected
   origin.
2. Resolve that ModuleRef's exact producer identity and export key using the
   [existing binding contract](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities).
   The selected provider P must be unambiguous and target/runtime-compatible.
   The key selects one public, non-expected registration row owned by P.
3. Require the provider row's `registrationId` and `ruleRevision` to equal
   E's exactly. Resolve its catalog entry in P's origin, and preserve every
   retained contract, rule, value, target and dependency obligation. Lookup
   failure or mismatch does not search other keys, modules or callables.
4. E's physical owner remains U, while its semantic origin is P. A direct
   row reference to E still follows this Import; it cannot bypass binding.
   Standalone nonpublic Imports/Exports are not admitted by this candidate.
   Direct private/internal use retains existing access obligations; no
   friend/source-unit access codec is completed here.

An Export of an expected row is invalid; there are no registration
re-exports, chained Imports or implicit method-to-intrinsic adapters.
Distinct export keys may alias one owned registration. They select the same
identity, but each original key remains a replacement obligation.
Different expectations may bind the same identity; check each independently.
Conflicting resolutions of one expected row reject.

Source compilers use U's expected row for a cross-Module selection. A merger
may replace it with P's direct row only after satisfying the binding and
preserving each original expectation/key and stronger relied-on fact, or
completely discharging them under the existing preservation rules. Packing
does not merge Module identities or authorize previously invalid access.
Retaining the original Import/expected row is the available conservative
way to retain an obligation; no serialized “already trusted” bit is added.

Replacement under the same producer/key must preserve the exact registration
identity and its promised input/result/failure, transformation, resource,
target and dependency contracts. An implementation can change within those
contracts; equal sample results are not equivalence. A new revision is a
different identity, not a compatible replacement without a separately
defined directed mapping. Changes to ordinary MethodDef bodies neither
change nor establish intrinsic identity; any independently retained callable
obligations still apply.

## Dependency traversal and per-Module Feature closure

Keep three contexts explicit: **using Module U**, physical registration
owner, and resolved **origin/provider P**. A request/view/source helper has
no new owner field here. Its retained declaration, expected contract, Body,
graph, constant argument or other already-admitted attachment supplies U and
the complete formal/binding context. The selected source's owner must not
overwrite that context.

Implement structural requirement discovery as follows, without execution:

1. Seed all retained attachments in each Module, including uncalled/bodyless
   expectations, unselected graph branches and dependencies surviving a fold.
   Validate each request's separately admitted inputs/substitutions, nested
   requests/views and layout references in its supplied using context.
2. For source kind 2, resolve the typed registration row and any original
   Import as above. For dependency kind 5, use the **same resolver**. Admit
   every selected origin/provider Module and inspect its own retained
   declarations/expectations under its own manifest. Identity resolution
   does not reinterpret or execute a MethodDef Body.
3. Visit **every retained entry** of that request's `0044` range, regardless
   of source kind or whether the consumer has evaluated the request.
   Kind 6 follows `0041` with **U's** context and requires effective `19/1`
   in U. Resolve its existing declaration/owner/witness/publication facts;
   never read a static initializer to discover the identity or requirements.
4. Follow argument requests, views, nested constant sources and all other
   separately admitted typed dependencies. An ImmutableData-backed input
   view likewise requires 19 in its using Module. Do not omit these paths
   because a scalar result or selected intrinsic is already known.
5. Memoize with artifact/exact schema, using Module and relevant formal,
   binding and target context, not a global row-number-only visited set.
   If a schema permits multiple using contexts, visit each; this grants no
   new request/range-sharing permission. Finite dependency closure does not
   excuse forbidden nested-request, view, storage or initialization cycles.

A registration declaration/expectation/export, either registration reference
position, and the existing retained execution uses require **12/1** in their
respective retaining Modules. This proposal adds **no unconditional edge to
19**: under the companion catalog, `12 -> {11,17}`, `17 -> {10}`, while
`19 -> {}`.

| Retained use in U | Required effective pairs, before additional actual dependencies |
|---|---|
| Required request, selected source kind 2; or kind 5 without an immutable identity | `{10/1,11/1,12/1,17/1}`; still not a complete Feature-12 execution registration. |
| Same request with retained dependency kind 6 or ImmutableData-backed input view | `{10/1,11/1,12/1,17/1,19/1}`. |
| Provider P has a private retained kind-6 dependency; U retains no such identity in its own requests/expectations | P requires 19; U does not inherit it merely by selecting P. Admit both Modules' complete closures. |
| Two using Modules retain kind 6 through separate requests selecting the same registration | Each requires 19 independently, even if the registration is resolved/cached once. |

Provider P's 19, or another packed Module's 19, cannot repair U's missing 19.
Likewise, source-kind-2 ownership alone does not invent an ImmutableData use
in U. Physically unreferenced ownerless request/value helpers do not acquire
a using Module merely from table co-location; they still obey their schema.
Owned retained registration declarations do have their explicit Module.

Retained closure and actual execution are different. Do not synthesize an
executed dependency for a runtime-only call on an unselected path. Conversely,
an already retained kind-6 row is not erased because the consumer will not
execute that path. The current direct-set rule still omits the selected
source, nested requests named by argument rows and separately owned layout
dependencies.

No hidden recipe-dependency list is inferred to be empty. If a selected
intrinsic needs additional program/recipe-owned dependencies, their exact
representation and traversal must be separately supplied before execution
can be admitted. This identity carrier makes the **existing retained**
kind-5/kind-6 graph traversable; it does not complete that remaining codec.
Add 13, enum, union, native or other requirements only for their actual
retained forms, not for a registration's numeric key or Module ownership.

## Duplicates, canonicality and transformation

* Owned definitions are unique by `(owner Module, registrationId,
  ruleRevision)`. Two owned rows for that identity reject even if byte-equal;
  aliases belong in Export, not duplicate definitions. Different revisions
  may coexist if each is supported; they are not conflicting Feature revisions.
* Expected rows can repeat the same selected identity while retaining
  different original bindings/obligations. They are not new identities.
  Existing Import-triple uniqueness and export-key uniqueness still apply.
* Compare resolved registration identities componentwise: ordinal decoded
  origin Module identity, unsigned `registrationId`, unsigned `ruleRevision`.
  Never compare raw row references, declaration role/visibility, DefId bits
  or export aliases as the intrinsic identity. Keep original binding
  obligations separately.
* Within one request, duplicate semantic dependencies remain invalid,
  including kind-5 aliases resolving to the same registration. A kind-5
  reference to the selected source is omitted, including proxy/direct aliases
  of that source. Do not manufacture a kind-1 MethodDef dependency instead.
* Canonical writers order dependency sets by unsigned kind then that family's
  stable logical identity, using the tuple above for kind 5 and the existing
  ImmutableData identity for kind 6. Otherwise valid permutations are
  noncanonical ordinary input, not a different intrinsic. Physical table
  order is not identity; no unique whole-artifact row order is imposed.

Rewriters remap `ownerModuleRef`, registration rows, `26` DefIds,
Import/Export/ModuleRef bindings, both registration reference positions and
every affected request/dependency/view reference according to its own
namespace. Rebuild exclusive ranges, semantic duplicate/order checks,
section counts/extents, Module Feature ranges, affected Body maps/offsets and
SourceMap slots. Preserve keys/revisions as semantic integers, never remap
them as rows. Clone context-sensitive helpers when needed; do not coalesce
different origins, revisions or retained expectations. Optional removal of
equivalent expected rows requires preserving all their binding obligations.
Removing an Import cannot turn a retained expected row into an owned
registration; remove/remap both coherently or retain the binding.
This is decoded re-encoding, not replacement of matching bytes.

## Refusal boundaries and remaining decisions

Before affected use, consumers check framing/stride/checked extents, closed
tags/roles/visibility and zero reserved fields, actual table bounds, required
owner Modules, unique owned keys, Import/Export ownership and eligibility,
exact selected identity/revision, complete Module closure and supported
binding/target/runtime facts.
Zero/high-byte/wrong-family references, orphan/wrong-owner expectations,
ambiguous providers, identity mismatches and invalid dependency sets reject.
Unknown schema/catalog/rules, unavailable prerequisite codecs or unsupported
intrinsic semantics refuse affected use rather than returning an empty
dependency set, unit, false proof, another overload or runtime fallback.

The [consumer-trust boundary](../specs/representation/reader-trust.md)
is unchanged: producers publish truthful contracts and dependencies; consumers
perform mandatory structural/support/binding checks, not mandatory semantic
re-proof of arbitrary intrinsic implementations or foreign behavior.

The following decisions remain **separate and unresolved here**:

* The compact resource-recipe grammar, wire records/allocations, exact input
  validation/transformation schemas, recipe-owned dependencies and computation
  of semantic work/peak incremental storage. `ruleRevision` selects an exact
  contract; it is not itself a recipe or a producer-reported cost.
* Individual catalog entries, their explicitly allocated Module-local keys
  and complete admitted programs/implementations, including any relation to
  source-callable registrations. No real intrinsic key or plug-in API is
  allocated by this proposal.
* The remaining Feature-12 program/value/view/substitution/resource codecs
  and independent target, publication, witness and runtime support. Existing
  request/input ordering requirements remain; no new intrinsic signature
  format or eligible-Body exception is inferred.

Required execution still needs the actual admitted program/recipe and exact
inputs before JIT/AOT constant work. Selecting source kind 2 cannot silently
switch to a MethodDef Body; an explicitly selected eligible checked source
uses the existing kind-1 path. Identity-only inspection must not be reported
as executable Feature-12 support. Source syntax, runtime initialization,
library APIs and native ABI are unchanged.

No identity, reference-family or Module-origin choice is left to implementation
under this recommendation; the separate recipe/catalog/runtime decisions
above remain open.

## Intended supersession and affected owners

These are future coordinated edits, **not changes made here**:

| Owner | Exact proposed integration |
|---|---|
| [Logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Supersede only the open intrinsic-registration identity/reference/ownership allocation slice with `005D`, its 16-byte row, `26` binding and typed-row rules. Preserve the 80/8-byte request/dependency rows, existing tags, request-owned ranges, separate dependency families, checked Body selection and recipe-schema deferral. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry](../specs/representation/registry.md), [machine registry](../specs/representation/trrm-1.registry.json) | Add the section/stride/fields, closed roles/visibility and `26 -> 005D`; extend **only** Import/Export's DefId target sets. Document explicit Module ownership and the two row-reference positions. Do not change normative static owners or mark Feature 12 completely registered. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reference verification](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces), [canonicality](../specs/representation/encoding-canonicality.md) | Add selected registration identity, expected/provided roles, exact comparison, alias/duplicate rules and schema-aware remapping above. Preserve complete original expectations and producer-truth boundaries. |
| [Admission](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md), companion graph/ImmutableData closure text | Supply the request using-context versus registration-origin traversal. Supersede their intrinsic-identity resolution deferral only; preserve scalar/selector/general/immutable capability scopes and conditional 19, with no new unconditional prerequisite. |
| [Proof/constant execution](../specs/language/proof-and-constant-evaluation.md#dependencies-availability-and-admission), [resource accounting](../specs/representation/resource-accounting.md) | Cross-reference exact identity and origin; preserve owned/versioned compact recipes, common evaluator accounting, actual program admission and no runtime fallback. No recipe algorithm or new resource minimum is selected. |

Exchange must select the complete amended draft/schema/catalog **out of
band** under [format versioning](../specs/representation/format-versioning.md).
Provisional implementation can incorporate this delta into an explicitly
agreed successor of the current unapproved general-predicate composition;
the existing enum/profile name or equal `(1,0)` bytes do not establish that
agreement. Feature rows and plausible strides cannot select its decoder.
No historical reader, compatibility alias or draft migration is required.

## Required candidate cases

These are future complete-schema tests, not executed tests or support claims.
Independently missing representations are prerequisites unless their absence
is the case being tested.

| Case | Required outcome |
|---|---|
| Owned registration selected by source kind 2; unrelated registration reached by kind 5; no checked Body | Resolve exact keys/origins through typed rows without name/native lookup or a fabricated Body. Actual execution remains gated on complete semantics/recipe support. |
| Consumer expected row with valid public provider Export; external binding and packed direct rewrite | Preserve one identity, original producer/key/expectation and every relied-on fact; enforce target/runtime and both Modules' manifests. |
| Two export aliases or two expected rows select one provider registration | Resolve one identity while checking all bindings; kind-5 aliases repeated in one request reject as duplicate semantic dependencies. |
| Equal names, signatures, body bytes, native symbols or numeric keys in different Modules; same key with different revision | Do not merge identities or bind by resemblance. Wrong selected key/revision fails, even if the requested method can otherwise run. |
| Zero, MethodDef/Body token, `26` DefId or `1C` Import token in either row-only position; wrong kind in Import/Export | Reject before affected use. Import/Export use `26`; the two selected row fields use a zero high byte only. |
| Missing/truncated `005D`, wrong stride/policy, nonzero reserved fields, unknown role, visibility 3, invalid Module owner, duplicate owned key/revision | Reject structural input; byte-equal duplicate definitions are not export aliases. |
| Wrong-Module ModuleRef, nonpublic Export/Import, exported expected row, orphan/multiply imported expected row, Import expecting an owned row, conflicting binding or ambiguous provider revision | Reject; do not treat an expected row as a local implementation or search another provider/key. |
| First/last valid 24-bit rows and `26` DefIds; row zero, `0x0100_0000` typed row, one-past-table, overflowing section/range arithmetic | Accept exact in-bounds endpoints; reject invalid references/extents using nonwrapping checks. Both semantic key fields independently allow `0xFFFF_FFFF`, but reject zero. |
| Source kind 2 and kind 5 beside retained kind 6, without evaluating the request | Require 19 in the request's using Module. Selecting an origin with 19 cannot supply the using Module's missing requirement. |
| Same case retained only through an unselected graph/type-argument source or ImmutableData-backed input view | Traverse retained structure and require 19 without executing a branch, reading backing or running initialization. |
| Two Modules' separate requests select one cached registration; only one manifest has 19 | Fail the deficient Module's affected use; no row-only visited-set shortcut or artifact-wide Feature union. |
| Provider-private kind 6; consumer retains no immutable identity; 11/13/17 alone supplied for a genuine kind-6 use | Keep provider/private and consumer requirements distinct; the latter use still requires 19 and complete declaration/publication support. |
| Reordered registration rows/dependencies, alias-remapping, original Imports retained after direct binding, immutable identities with different owners/witnesses | Preserve resolved meanings and obligations; ordinary valid permutations are accepted, canonical dependency sorting is stable, and distinct immutable identities remain distinct. |
| Selected source repeated as kind 5, overlapping/shared request dependency ranges, forbidden nested request/view cycle | Reject under source-omission/exclusive-ownership/acyclicity rules; do not deduplicate away an invalid ownership graph. |
| Exact identity but missing catalog entry, recipe/input schema, intrinsic-owned dependency codec or evaluator; incomplete Feature 12 | Allow clearly scoped identity inspection only; refuse complete binding/execution support. Never invoke a similarly named method/native helper or invent zero cost/empty dependencies. |
| Replacement changes key, exact revision, promised value/failure behavior, transformation, cost contract, target or retained immutable dependency | Reject incompatible replacement or require an explicitly justified rebuild/rebind preserving all surviving obligations; passing examples are not equivalence evidence. |
