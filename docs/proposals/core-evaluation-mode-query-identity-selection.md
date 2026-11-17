# Core evaluation-mode property identity selection

> [!IMPORTANT]
> **Status: UNAPPROVED; provisional direction only; non-normative.**
> All candidate carriers, registrations and integration edits below remain
> unapproved. This document approves or implements nothing.

The controlling instruction is: “At a genuine missing contract, prepare the
smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at
max reasoning, then continue with it being treated as a provisional
approval/direction.”

## Decision, baseline and evidence

Recommend **one compiler-implemented intrinsic registration attached to each
exact designated Core property/getter**, not a PropertyDef selection alone.
Reuse the unapproved [intrinsic-registration identity](intrinsic-registration-identity.md#semantic-identity-origin-and-binding)
`(origin Module, registrationId, ruleRevision)`; define its bounded member
attachment and mode catalog entries here. The host supplies the exact keys
and origins; the compiler owns the immutable program. No numeric values,
production providers or export keys are allocated.
The host disambiguates the accepted `RuntimeHelpers.IsPureEval` and
`RuntimeHelpers.IsConstEval` members; it cannot assign either role to an
arbitrary static Boolean property.

Extend `CoreEvaluationFormIdentitySelection` with optional
`IsPureEvalProperty` and `IsConstEvalProperty` registration inputs. Keep its
Pure/ConstEval AttributeSchema slots and catalog separate. Select the complete
host grammar **`terrumc.evaluation-core-manifest/2`** below, replacing the
unapproved v1 grammar rather than requiring draft compatibility.

Preserve the accepted [mode properties and accounting](../specs/language/published-predicates-and-effects.md#evaluation-modes),
[Core library surface](../specs/libraries.md#dependency-layers-and-package-boundaries),
[language summary](../specs/language.md) and
[logical evaluation records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms).
Their mode matrix, author authorization and Pure effects are not redesigned.
The source-binding exception below is an **intentional unapproved amendment**
to the ordinary auto-property rule, not existing accepted source behavior.

Current source evidence, not approval or executed qualification:

* [`CoreEvaluationFormIdentitySelection`](../../sources/Terrum.CodeAnalysis/Semantics/CoreEvaluationFormIdentitySelection.cs)
  carries AttributeSchemas only; the
  [`manifest reader`](../../sources/terrumc/EvaluationCoreManifestReader.cs)
  implements the older schema-only v1 input. No RuntimeHelpers mode-property
  implementation exists under `sources`.
* The [`registration table`](../../sources/Terrum.CodeAnalysis/Modules/TrRmIntrinsicRegistrationTable.cs),
  [`provider checks`](../../sources/Terrum.CodeAnalysis/Modules/TrRmIntrinsicRegistrationProviderBindingValidator.cs)
  and [`definition resolver`](../../sources/Terrum.CodeAnalysis/Modules/TrRmDefinitionResolver.cs)
  prototype distinguish registrations from properties/getters, but supply
  neither this attachment nor these executable catalog entries. Earlier
  proposal implementation observations are snapshots, not current support.

| Alternative | Disposition |
|---|---|
| Checked PropertyDef/getter selection alone | Does not identify its intrinsic implementation or authorize replacing a source body. Rejected. |
| **Existing intrinsic identity plus the exact member attachment below** | **Recommended.** One program identity mechanism, with a closed compiler-owned recipe and explicit source/external delivery. |
| Separate property-intrinsic identity table, option and manifest | Can be sound, but duplicates registration identity and the existing per-Module evaluation handoff. Not selected. |

## Exact registration and member attachment

Each new slot has candidate type `CoreEvaluationModePropertyRegistrationInput?`
with exactly one of these alternatives:

| Origin | Required in-process input |
|---|---|
| `SourceLocal` | Actual `(SyntaxTree, PropertyDeclaration SyntaxNode)`, plus explicit nonzero `u32 registrationId` and `u32 ruleRevision`. The declaration belongs to this using Module's source snapshot. |
| `External` | Exact `ProviderModuleIdentity`, `PropertyExportKey`, `IntrinsicExportKey`, and expected nonzero `u32 registrationId`/`u32 ruleRevision`, resolved through an already supplied ordinary checked `CompilationReference`. |

A **`ModeQueryIntrinsicAttachment`** logically contains the actual RuntimeHelpers
owner TypeDef identity, PropertyDef, derived getter MethodDef, owned
IntrinsicRegistration identity and one closed recipe contract:

| Selection slot | Required actual member | Exact compiler-owned contract |
|---|---|---|
| `IsPureEvalProperty` | The selected Core RuntimeHelpers owner's property named exactly `IsPureEval`. | The IsPureEval getter program under accepted `pure-evaluation/1`. |
| `IsConstEvalProperty` | The selected Core RuntimeHelpers owner's property named exactly `IsConstEval`. | The IsConstEval getter program under that same accepted rule set. |

The slot fixes the recipe only for its corresponding accepted member; it
does not relabel a different property. Neither source nor manifest supplies
executable code, a callback, transformation, alternate Boolean rule or resource
total.
Its complete logical program and ordinary activation/read/result/retirement
accounting are those of the accepted owner. The supplied tuple names this
immutable catalog contract; its numbers do not select a recipe by themselves.
Reusing a tuple for another recipe or member is invalid.

Require one-to-one property/getter/registration attachment for this bounded
pair of intrinsics. The getter is derived by
[ordinary property binding](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions),
not a `get_Name` search or separately exported getter key. Property and getter
have the same actual public declaring owner, public access, exact reserved
`bool` value result and synchronous static zero-input shape; no receiver, index
parameters, additional caller obligations or backing storage are admitted.
If both slots are supplied, their properties, getters and registration
identities are distinct and their actual declaring owner is the same.
Export aliases preserve identity; duplicate definitions or attachments do not.

### Accepted owner/member identity and qualification

The logical API identity is the **host-selected Core provider, actual
RuntimeHelpers owner TypeDef identity, corresponding accepted property name
and complete signature above**. Require the owner's decoded declared simple
name to be exactly `RuntimeHelpers` and the actual PropertyDef's decoded
declared name to match its selection slot. Compare names ordinally under
ordinary declaration-name rules, not export-key spellings, aliases or display
names. Thus a selected `OtherHelpers.IsPureEval`, `RuntimeHelpers.Other` or
swapped mode property rejects even with a valid intrinsic tuple and Boolean
signature. These name checks constrain the accepted API; names alone never
establish Core identity or registration authority.

For SourceLocal, the explicitly selected using Module supplies the provider
and the checked occurrence's declaring type supplies its exact owner identity.
For External, the selected provider/property export supplies the actual
`PropertyDef.ownerTypeDef` through ordinary checked binding. Retain that owner
identity in the selection and attachment; all selected mode properties must
belong to it. Do not scan for another same-named owner or member. Transparent
source aliases and export aliases remain valid only when they preserve these
same actual declarations and their accepted names.

The accepted clauses name RuntimeHelpers but do not select its full namespace/
qualification; the [Core identity policy](../specs/libraries/adopted-inventory.md#1-status-and-identity)
requires missing identity fields from the designated profile. That narrow
qualification choice remains a Core-profile input, supplied by the exact
local declaration or checked provider/property export, not a production
namespace chosen here. Its actual qualified owner identity must be unambiguous,
retained and remapped through checked declaration correspondence; unavailable
owner/name projections decline. This leaves neither the RuntimeHelpers simple
name nor either property name/signature open to host renaming. Provider Module
identities and export keys remain explicit inputs; no v2 field or default
namespace is added.

### Source-local definition: an explicit intrinsic implementation

Check tree/node membership by object identity and ordinary primary ownership,
then validate the **whole declaration before auto-backing inference**.
The selected concrete property must be public, static, nonpartial, have
exactly one bodyless `get;`, and have no initializer or authored getter
contract clauses. Its owner may use ordinary partial-type assembly.

The candidate fragment `public static bool IsPureEval { get; }`, inside the
actual selected Core owner, is a **signature implemented by the supplied
intrinsic registration**, not a runnable source implementation. The analogous
IsConstEval declaration follows the same rule. This intentionally exempts
only these explicitly registered occurrences from
[§5.10's backed-auto-getter rule](../specs/grammar-spec.md#510-properties-and-accessor-local-effects).
No new token, attribute or API spelling is introduced.

| Source form | Registration result |
|---|---|
| Exactly selected signature-only getter | Create the actual property/getter and public owned registration/catalog entry in the using Module. Set the getter's implementation kind to this intrinsic; create no ordinary Body, backing field or initialization group. |
| Block/expression body, property initializer, setter/init accessor, `field` use, partial property/competing implementation or already-bound backed implementation | Reject the selected declaration/request. Never discard an authored implementation, initializer, storage or effects. |
| `extern` property/getter, interface requirement, NativeImport/NativeCallable implementation or foreign symbol | Not this declaration route; reject. `extern` is not a property production and gains no new meaning here. |
| Unselected `{ get; }` | Keep ordinary auto-property semantics and backing-initialization obligations; do not convert it retrospectively. |

Constructor/static-initializer writes cannot initialize a registered computed
property because it has no backing; diagnose those writes, never discard them.

Binding the explicit local input creates the actual catalog entry mapping its
tuple to the slot's fixed compiler recipe. It is not a description of a
presumed provider and does not require fabricated exports or an Import.
Ordinary declaration collisions, access/type checks and attribute checks
still apply; Pure/ConstEval annotations neither supply nor replace the
implementation. Reparse/rebind validates a fresh source/options/reference
snapshot. Source spelling, paths, positions, ordinals and hashes do not by
themselves establish declaration or registration identity; the accepted
declaration-name requirements above still apply.

### Checked external provider

Resolve both export keys in the **same actual provider snapshot**. The first
must select its public owned PropertyDef, with its referenced getter and public
owner; the second must select its public owned IntrinsicRegistration, never an
imported expectation. Both definitions and their attachment belong to the
selected logical provider Module, not merely the same artifact. Require matching
`registrationId` and `ruleRevision`, the fixed supported catalog contract, and
the provider's actual attachment linking that property and getter to that
registration. The getter has intrinsic implementation kind, not an ordinary
Body, native import or auto-backing implementation.

An External input **checks an existing attachment; it cannot create one** or
turn an arbitrary exported getter into an intrinsic. A zero Body reference,
matching name/signature, consumer expectation or registration identity row
alone is insufficient. Preserve both original export-key expectations; getter
correspondence still needs no getter export. A producer wishing to publish a
local definition for this route must explicitly export both actual definitions;
no keys are generated here.

Provider identity must differ from the using Module. Retain its unambiguous
revision, exact artifact schema or in-process declaration-contract selection,
governing requirements, target/runtime context and actual declaration/catalog/
export maps. If a checked reference cannot supply them, decline, including for
an unused supplied selection. This adds no reference factory or provider loader.

## Exact host-manifest evolution

Use the existing `--evaluation-core-manifest <path>` option and select exactly
**`schema: "terrumc.evaluation-core-manifest/2"`**. This candidate replaces v1's
whole grammar: reject `/1`, missing new fields and unknown tokens; no alias,
automatic upgrade, shape-based decoder choice or compatibility reader is
required. The current reader/fixtures would need an explicit later migration.

Retain the [CLI proposal's](core-evaluation-forms-cli-selection.md#cli-and-build-target-contract)
option/command restrictions, snapshot capture, cross-manifest composition,
path checks, failure channels and staged-publication rules. Reuse its
[JSON lexical/duplicate-property rules and exact `Text`, `u32`, Selector and LocalCatalog shapes](core-evaluation-forms-cli-selection.md#exact-host-manifest),
including every nested origin/declaration/export constraint. The v2 root,
nullability and role cardinality are defined only below. All objects are closed;
duplicate decoded properties reject even when equal. These are host
configuration rules, not an artifact decoder or serialized authority.

Every root field below is required; only fields marked nullable accept null:

| Root field | Exact shape |
|---|---|
| `schema` | Exact v2 token above. |
| `coreAuthorityIdentity`, `usingModuleIdentity` | `Text`, with their existing authority/context meanings. |
| `pureSchema`, `constEvalSchema` | Selector or null, unchanged AttributeSchema roles. |
| `localSchemaDeclarations` | LocalCatalog or null, nonnull exactly when a selected **schema** role is local. Local mode registrations do not require this catalog. |
| `isPureEvalProperty`, `isConstEvalProperty` | Registration object below or null; never a Boolean mode flag. |

At least one of the four role fields must be nonnull. A Registration object
has exactly one of the following shapes, with every listed field required:

| `kind` | Exact fields in addition to `kind` |
|---|---|
| `"local"` | `sourceIndex: u32`, `propertyOrdinal: u32`, `registrationId: u32`, `ruleRevision: u32`. |
| `"external"` | `providerIdentity: Text`, `propertyExportKey: Text`, `intrinsicExportKey: Text`, `registrationId: u32`, `ruleRevision: u32`. |

Both registration integers must be nonzero. Local values define an owned
entry; external values are exact expectations checked against the selected
provider entry. Supply them explicitly; derive none from row numbers, source
ordinals, names or the role. No concrete values are assigned by this proposal.

For `"local"`, source index zero is the command's primary source, including
stdin; additional `--source` inputs follow argv order. In that actual immutable
tree, enumerate all `SyntaxKind.PropertyDeclaration` nodes in
`Root.DescendantNodes(includeSelf: true)` pre-order; `propertyOrdinal` is the
zero-based index, without filtering by name, shape or primary status.
Require both indexes in range, select that exact node, then apply all local
declaration checks. Indexes are invocation-local locators, not persisted
identities. Invalid locators never trigger name search. Resolve after parsing
the complete source snapshot but before declaration/body/backing binding.

For `"external"`, supply no provider path, declaration or catalog in JSON.
Use only the invocation's real checked ordinary references as above; present
CLI routes without that capability must reject the selection even when unused.
Schema-only manifests use explicit null property slots; mode-only manifests
need no schema selection. The same host option configures identities, never
the activation's mode. Ordinary CLI protocol gains no flags or fields.
Native primitive build manifests must be explicitly migrated to this exact
shape with their existing path/content and source-input identity checks;
neither an old manifest nor an existing output permits inferred registration.
Malformed manifests/locators and incompatible provider inputs use existing
configuration failures; invalid source declaration forms use ordinary located
source diagnostics. Unsupported consumption retains its route's existing
failure category; no new diagnostic family is selected.

## Bound flow, checking and refusal gates

Keep the intrinsic implementation/attachment on the selected getter and its
`BoundFunction`; retain each using Module's original selection, expected
property/getter/registration contracts and provider context separately.
Carry both through `CompilationOptions`, Binder/BoundProgram, shared traversal
and rewriting, mandatory verification and checked reference projections.
Require at most one complete selection per using Module across options/
references. Validate all supplied roles atomically, including unused ones.
The same actual member/registration cannot acquire different recipes through
another using Module. Property providers and attribute-schema providers need
not be the same; shared provider snapshots must agree.

Ordinary lookup and transparent aliases resolve actual members first.
No same-named property, attribute, `BoundFunction.IsConstEval` capability,
`--corelib` mode or later Core default can create or reselect this attachment.
The designated host supplies authority from outside the source/artifact;
an actual provider attachment is not authority to designate Core for new source.
An omitted required registration declines; unrelated user properties retain
ordinary semantics.

Nested calls, published-clause activations, required evaluation and JIT/AOT
folding preserve the accepted selected-mode and accounting rules, not
annotation presence. Ordinary semantics-preserving folding retains ordinary
observations; selecting another form needs its accepted authorization.
A shared callee must remain mode-sensitive or be validly specialized.
Registration supplies no evaluation permission for other callables, caching
discount, runtime fallback or new physical mode ABI.

Before affected consumption, check exact schema/support, bounded typed
references, exact RuntimeHelpers owner/member identity and accepted names/
signature, actual getter/registration correspondence, mutually exclusive
implementation kinds, complete catalog contract and binding/requirements.
Retain the [producer-truth boundary](../specs/representation/reader-trust.md);
consumers do not re-prove arbitrary provider bodies. Packing/replacement
preserves every original expectation, both export keys, tuple, recipe contract,
owner/getter identity and alias equality. Different exact registration
revisions do not roll forward; row order or sample results prove no replacement.

The [intrinsic-registration proposal](intrinsic-registration-identity.md)
supplies the candidate identity and Import/Export rules, **not an attachment
codec or executable mode catalog by itself**. This proposal supplies their
logical mode-specific contract, not bytes. Never put a PropertyDef/MethodDef
token in a registration-row field, fabricate an empty checked Body, or
reinterpret an AttributeSchema as a registration.

Direct in-process evaluation/lowering may use the complete checked catalog.
Independent TRRM/private exchange still needs an explicitly selected exact
schema and supported member-attachment, program/recipe, value/dependency and
original-context codecs. Absent provider projections or codecs must decline
before affected use/publication, not drop obligations, infer an empty program
from Body absence, persist host authorization in debug data or treat a folded
Boolean as discharged binding evidence. No program may start after such a
prepublication failure.

## Intended integration and remaining gates

If adopted, add the registration/attachment handoff to **Evaluation modes**,
link it from the libraries RuntimeHelpers paragraph and language form summary,
and require its retention in logical evaluation-form records. Amend only the
selected source declarations' §5.10 auto-backing interpretation as specified;
ordinary properties and Pure effect verification remain unchanged.

Extend [Pure schema selection](core-pure-schema-identity-selection.md)'s carrier
and nonempty-role condition, not its AttributeSchema semantics/catalog.
Supersede the CLI proposal's v1 token/root grammar with the complete v2
candidate here; preserve its other delivery checks. Reuse the intrinsic
proposal's logical identity without adopting its numeric allocations or
claiming a generic callable-attachment/recipe codec is complete.

No local-source implementation, member-to-recipe mapping or host-manifest
grammar choice is deferred. Adoption, actual implementation, explicitly
supplied provider/key/revision values, absent checked-provider adapters and
exact persistent codecs remain gates. No production Module ID, ExportKey,
DefId, section/Feature/opcode or intrinsic numeric ID is assigned, and no
general Core API inventory is completed.

## Focused decision cases

Required future cases, not tests run or support claimed here:

| Case | Required outcome |
|---|---|
| Exact local RuntimeHelpers member/signature plus explicit registration tuple; mode-only and combined schema inputs | Build one actual owner/property/getter/registration attachment with the fixed recipe and no backing; unrelated schema roles remain independent. |
| `OtherHelpers.IsPureEval`, `RuntimeHelpers.Other`, swapped role/member names, or a matching name outside the selected Core provider | Reject the attempted role assignment even with a valid Boolean signature/tuple; never rename or recognize by spelling alone. |
| Two qualified RuntimeHelpers owners with equal simple names; transparent source/export aliases | Retain the exact selected owner identity, not a name match. Both mode properties must share it; aliases are valid only for the same actual accepted declarations. |
| Same `{ get; }` without registration; ordinary already-backed property | Ordinary auto-property/initialization semantics, never implicit Core recognition or retrospective conversion. |
| Selected block/expression body, initializer, setter/init, partial implementation, constructor write or extern/native substitute | Diagnose rather than replace the body, discard initialization/effects or invent native backing. |
| External property and registration exports with matching actual attachment/catalog; aliases of either key | Resolve the exact pair and preserve both original key expectations; no getter key is needed. |
| Correct registration tuple but wrong/missing member attachment; zero Body alone; same names in another provider | Reject/decline, even when unused; no arbitrary getter replacement or identity inference. |
| Colliding tuple/recipe, two roles sharing a property/getter/registration, split owner/aliases or conflicting snapshots | Reject complete invalid input; preserve identity across Modules and replacement. |
| Exact v2, explicit null roles, first/last valid source/property indexes; `/1`, missing/unknown/duplicate fields, zero key/revision or out-of-range locator | Accept only the chosen complete grammar and valid input; no compatibility decoder, name search or mode flag. |
| Ordinary/pure/constant nested calls, clause return, shared JIT/AOT callee and repeated required reads | Preserve accepted observations, authorization and accounting; no premature ordinary fold or caching discount. |
| Logging guarded only by IsConstEval, or Pure-only backend folding | Registration does not weaken pure-form effects or grant required-evaluation opt-in to enclosing callables. |
| Provider replacement, missing external adapter or attachment/program codec, including after a fold | Preserve every original obligation or decline before use/publication; no runtime fallback, fake Body or weakened artifact. |
