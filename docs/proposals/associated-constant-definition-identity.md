# Associated-constant declaration identity and binding

> [!IMPORTANT]
> **Unapproved proposal.** Every assignment and rule addition below is a
> candidate. Treat it as provisional direction only under the user's
> authorized missing-contract workflow, not normative approval. This document
> changes no specification, registry or compiler and establishes no support,
> qualification or conformance.

## Decision and scope

**Recommend DefId candidate `0x25=AssociatedConstDef`, targeting existing
section `0x0032`, with Import/Export eligibility.** In
`WitnessAssociatedConst.associatedConstDefId`, select one explicit four-byte
**AssociatedConstReference** union: a direct `0x25` DefId or a `0x1C` Import
token whose expected definition is an AssociatedConstDef. Preserve the
existing declaration and witness-binding row layouts.

This identifies the **uninitialized requirement declaration**, not a value
or a selected conformance. Preserve the separate candidate
`0x23=WitnessAssociatedConst` for one selected binding in one origin witness.
That binding remains excluded from standalone Import/Export. Its identity
includes the declaration, exact interface instances, complete implementer
and selected witness origin; equal names, domains or values do not identify
it.

Preserve `0x20=AssociatedTypeDef` and its own reference rules,
`0x21=UnionCaseDef`, and candidates `0x22=InterfaceConstDef`,
`0x23=WitnessAssociatedConst`, `0x24=StaticField`. In particular, `0x25`
does **not** enter ImmutableData's closed target sets. Its declaration word
remains direct-only: kind 1 accepts `22/23`, not this requirement or an Import
token. Candidate Feature `19/1` remains independent of Feature 17 with
unconditional prerequisites `[]`.

No source production, constant default, value codec, Feature, static
publication carrier, witness-origin carrier, dispatch ABI or initialization
operation is selected here. The recommendation closes the associated-constant
declaration/reference choice and specifies selected-binding comparison once
the surrounding witness context is representable; it does not pretend those
independent carriers already exist.

## Current owners and evidence

Locations below refer to the current working-tree text. Accepted contracts,
unapproved directions and implementation evidence have different authority.

| Source and precise location | Relevant boundary |
|---|---|
| [Metadata, lines 315–324](../specs/il-metadata.md#table-stream-and-token-encoding), [lines 725–739](../specs/il-metadata.md#section-kinds-and-row-layouts), and [lines 863–870](../specs/il-metadata.md#section-kinds-and-row-layouts) | DefId's closed list lacks AssociatedConstDef. InterfaceDef and InterfaceWitness have separate associated-constant ranges; AssociatedConstDef is `u32 ownerInterfaceDef, str name, u32 constantTypeSpec`, and WitnessAssociatedConst is `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 associatedConstDefId, u32 constantSourceRef`. Import/Export target lists do not supply this declaration. |
| [Machine registry, line 190](../specs/representation/trrm-1.registry.json#L190), [line 269](../specs/representation/trrm-1.registry.json#L269), and [lines 297–309](../specs/representation/trrm-1.registry.json#L297-L309) | Required policy `0000`; InterfaceDef `0025` is 48 bytes, InterfaceWitness `0028` is 32, AssociatedConstDef `0032` is 12, and WitnessAssociatedConst `0033` is 16. Section allocation is not DefId allocation. |
| [Logical witness records, lines 76–127](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain), and [constant sources, lines 237–244](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) | Separate first-class associated-constant declarations and typed witness bindings are required; declaration defaults are absent. Identity/reference and surrounding witness codecs remain gates. ConstantSource has its own typed alternatives and result domain. |
| [Source witnesses, lines 97–146](../specs/language/interface-witnesses.md#inherited-requirements-and-defaults), and [publication, lines 331–352](../specs/language/interface-witnesses.md#publication-and-independent-use) | Close inherited instances before selecting members; associated constants require a selected typed binding. Projection retains its original witness and obligations, not a new conformance. |
| [Binding/replacement, lines 13–39](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities), [lines 232–265](../specs/representation/binding-and-replacement.md#4-complete-comparison-graphs), and [lines 268–314](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts) | Select identity before contract comparison; retain each expectation, complete context, finite comparison graph and stronger relied-on fact. |
| [Definition-kind candidate, lines 11–29](immutable-data-definition-kinds.md#decision-and-scope), [lines 75–85](immutable-data-definition-kinds.md#candidate-words-and-exact-targets), and [lines 129–150](immutable-data-definition-kinds.md#0x23-selected-associated-constant-binding) | `22/23/24` are explicit candidates; `23` is not importable/exportable. AssociatedConstDef's independent declaration/reference codec is expressly deferred. |
| [Declaration-reference candidate, lines 11–28](immutable-data-declaration-reference.md#decision-and-scope), [lines 62–85](immutable-data-declaration-reference.md#resolved-constants), and [lines 128–175](immutable-data-declaration-reference.md#exact-owner-substitution) | ImmutableData uses direct DefIds, exact owner substitutions and the selected binding, not an unbound requirement or a ConstantSource. |
| [Feature candidate, lines 11–23](immutable-data-feature-admission.md#decision-and-boundary), and [lines 110–181](immutable-data-feature-admission.md#per-module-detection-and-closure) | `19/1` owns retained ImmutableData identity independently of graphs. Its per-Module detection and use-dependent closure are not revised here. |
| [Associated-type candidate, lines 12–17](associated-type-definition-identity.md#decision-scope-and-dependencies), and [lines 51–104](associated-type-definition-identity.md#candidate-physical-contract) | Its `20` declaration and AssociatedTypeReference union concern types, including type defaults. Neither is an associated-constant identity or value. |
| [Static persistence candidate, lines 11–40](immutable-static-field-persistence.md#decision-and-scope), and [lines 98–102](immutable-static-field-persistence.md#allocation-audit-and-alternatives) | Separate provisional direction selects static sections `0055..005C`, clauses `0013/0014`, and whole-inline publication, preserving `24` and `19/1`. This proposal neither revises nor reopens those choices. |

Current code establishes only a bounded structural starting point.
[AssociatedConstDef reader/writer, lines 177–310](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceConstantTables.cs#L177-L310)
validate/encode declaration ownership, names and complete type references.
[InterfaceWitness, lines 35–141](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceWitnessTables.cs#L35-L141)
checks associated-binding ranges, not their declaration/value semantics.
The current Modules sources have no WitnessAssociatedConst reader/writer;
the [definition resolver, lines 15–32](../../sources/Terrum.CodeAnalysis/Modules/TrRmDefinitionResolver.cs#L15-L32)
has no associated-constant kind. Existing provisional associated-type code
does not approve a constant codec or establish this binding contract.

## Identity alternatives and allocation

The decision is how to retain a declaration across Modules and then select
its binding, not merely which unused number comes next.
Section and definition-kind codes below are hexadecimal; Feature IDs are
decimal.

| Coherent alternative | Assessment |
|---|---|
| **Independent `25` DefId, explicit member Import/Export and field-specific direct/Import union** | **Recommended.** Reuses the existing expected-definition and explicit export-key mechanisms without changing row widths or making source names stable binary keys. Keeps requirement identity distinct from `23` binding identity. |
| Owner-relative requirement identity without member Import/Export | A coherent replacement would make `(resolved declaring interface definition, associated-constant role, exact ordinal decoded declaration name)` the declaration key, require unique stable names per direct owner, and use the complete declaring interface instance for instantiation. The four-byte field could then be a typed local AssociatedConstDef row; the owner's Import and this newly specified key would pair expected/provider requirements. Compare domains only after that selection. This avoids a DefId kind/member Import but makes renaming a binary identity change and requires a complete owner-relative key/mapping contract. It is not the current meaning of a raw row, nor selection by an unqualified name. No demonstrated need justifies replacing explicit member keys with that additional naming commitment here. |
| Independent kind `32`, matching AssociatedConstDef section `0032` | Equally expressible, same width/capacity and binding rules. Section matching supplies no semantic advantage: UnionCaseDef kind `21` already targets section `0034`. Select `25` as one explicit mapping, not because sequential allocation is inherently superior. Do not accept both as aliases. |
| Regroup requirement/fixed/bound/static kinds, moving current `23/24` candidates | Permissible pre-v1 redesign with explicit coordinated changes, not forbidden by compatibility. It changes user-directed candidates and their fixtures without removing a distinction or saving bytes; not selected in this focused decision. |
| Independent `25` with direct-only references to expected stubs | Also coherent: a required Import on the stub supplies binding. The selected union makes the retained member Import explicit at a witness use and follows the separate associated-type reference pattern. Direct proxy spelling remains equivalent ordinary input as specified below. This choice does not widen ImmutableData's different direct-only field. |

Registered DefId kinds are `13/14/16/19/1E/21`; the current selected
companions use `20/22/23/24`. None assigns `25`; its appearance as StaticField
in **rejected alternatives** is not a competing selected assignment.
Section `0025=InterfaceDef` occupies a different `u16` namespace and is not
this `u8` definition kind's target. No opcode, Body-map token, TypeSpec form,
Feature or family band is allocated. Other unassigned kinds stay unassigned,
and `FF` remains reserved for future required indirection.

## Candidate reference and row contract

All multibyte words are little-endian. `R(Table)` means a required
high-byte-zero, one-based `u32` typed row reference in `1..00FF_FFFF`.
Complete type fields retain the accepted four-byte TypeSpecRef grammar.

| Position | Exact candidate encoding |
|---|---|
| AssociatedConstDef DefId | `(0x25 << 24) \| r`, `1 <= r <= AssociatedConstDef.count <= 0x00FF_FFFF`; target **section `0032`** |
| `Import.expectedLocalDefId` | That direct `25` DefId of a consumer-owned expected AssociatedConstDef |
| `Export.localDefId` | That direct `25` DefId of an eligible provider-local AssociatedConstDef |
| AssociatedConstReference: direct | The `25` DefId above |
| AssociatedConstReference: imported | `(0x1C << 24) \| i`, `1 <= i <= Import.count <= 0x00FF_FFFF`, whose Import expects a `25` definition |
| `WitnessAssociatedConst.constantSourceRef` | `R(ConstantSource)` only; never either declaration-reference form, a value node or inline bytes |

Thus `0x2500_0001` / bytes `01 00 00 25` names AssociatedConstDef row 1,
not InterfaceDef row 1. `0x1C00_0001` / `01 00 00 1C` names Import row 1
with the required expected kind. Import/Export's definition fields themselves
do not accept Import tokens, chained expectations or `23` binding definitions.

Apply AssociatedConstReference **only** to
`WitnessAssociatedConst.associatedConstDefId`. The unchanged rows are:

| Section / stride | Ordered payload and interpretation |
|---|---|
| `0032` / 12 | `R(InterfaceDef) ownerInterfaceDef, R(String) name, TypeSpecRef constantTypeSpec` |
| `0033` / 16 | `R(InterfaceWitness) witnessRow, TypeSpecRef declaringInterfaceTypeSpec, AssociatedConstReference associatedConstDefId, R(ConstantSource) constantSourceRef` |

Names are required nonempty well-formed declaration labels. Both complete
types are required and valid in their positions; the declaring type must
resolve to the exact nominal interface instance, not merely any valid type.
Inline F2/F3 forms do not supply an unassigned nominal interface identity.
Required section policy `0000`, fixed strides and checked extents remain.

Zero, a zero row, raw row `0000_0001`, section-shaped `3200_0001`, `20`,
`22/23/24`, another definition kind, F2/F3 or `FF` rejects in the declaration
reference position. Check the discriminator before the row and its actual
table bounds; do not mask it or probe another table. Typed owner/source
fields retain zero high bytes even when their targets have DefId kinds.

Canonical writers use the Import form for a retained member Import dependency
and direct form for an authored or satisfied direct-bound declaration. An
ordinary direct `25` reference to an expected stub still follows its Import
binding and retains its obligations; that equivalent spelling cannot bypass
binding. This is no raw-row alias or inferred historical codec.

## Declaration ownership, imports and domain comparison

Each nominal interface TypeDef has one InterfaceDef. AssociatedConstDef
ownership is `AssociatedConstDef -> InterfaceDef -> TypeDef -> Module`.
Its owner's `associatedConstFirst/associatedConstCount` contains exactly
the directly declared requirements, never copied inherited members.
Each declaration row belongs to exactly one exclusive contiguous range and
agrees with `ownerInterfaceDef`; no orphan, overlap or competing owner is
valid. Empty ranges are exactly `(0,0)`; nonempty checked ranges have a
nonzero first row and an in-bounds last row.

A declaration has no independent generic-formal list, initializer, value or
default. Its `constantTypeSpec` is interpreted in the declaring interface's
formal scope, including the explicitly identified self formal. It does not
borrow an arbitrary using method's formal scope.

An imported expectation is an ordinary AssociatedConstDef row **inside the
consumer's expected InterfaceDef**, preserving its complete expected domain.
The owner TypeDef, member Import and its ModuleRef belong to that consumer
Module. Retain the expected owner's complete direct requirement contract,
not a sparse name/range-position cache. Every retained expected requirement
has an explicit determinate member Import binding; importing only the owner
does not identify its member. Absence of a Body/source is not an import
sentinel: authored requirements lack those too.

An Export names a directly declared provider-local requirement, with a
public non-expected owner and public effective requirement access. Neither
an expected member nor a member reparented from an imported owner is eligible.
AssociatedConstDef has no access flag; do not invent one, infer additional
permission from `name`, or erase a source restriction. The bounded
owner-derived interface-contract case is usable only when that is the
complete effective access contract. Forms needing additional source-unit,
member or conformance access metadata remain gated by the
[access-record owner](../specs/representation/logical-records.md#source-unit-access-and-definition-identity-encoding-pending).

Apply this candidate specialization of the shared binding relation:

1. Select the member Import's exact Module identity, unambiguous provider
   revision and nonempty export key; require an eligible `25` target.
   Export keys retain Module-wide uniqueness and ordinal decoded-text
   comparison. Do not retry names, another owner/key or another provider.
2. Independently bind the expected interface TypeDef. The selected member's
   InterfaceDef must own **that exact selected provider interface TypeDef**
   in its provider Module. A public owner, equal name or equal layout does
   not establish this relationship. A member Export does not implicitly
   export/bind its owner.
3. Map expected/provider owner formals by kind and ordinal, preserving
   arity, every ordered type/constant argument, formal domains, self identity
   and retained formation constraints. Compare `constantTypeSpec` by exact
   normalized complete domain under that correspondence. No assignability,
   numeric conversion, carrier equality or erased qualification substitutes
   for domain equality.
4. An open declaration comparison checks corresponding symbolic terms
   throughout the retained admitted domain; coincidence in one closed
   substitution is insufficient. Unsupported symbolic comparison refuses
   affected use. Complete the finite retained owner/member/dependency graph
   before publishing success.

Names are source labels, not additional binary selectors or equality keys
here. Existing aliases to one provider definition create no new declaration
identity. Each retained expectation still passes independently; one expected
declaration cannot resolve to conflicting provider identities.

An AssociatedConstDef import promises a declaration and domain, **not one
global value shared by all implementers**. It neither supplies a witness nor
attaches one implementer's currently observed value to the requirement.

## Exact instantiated requirements and selected bindings

Keep three different semantic identities:

* **D: declaration.** The resolved AssociatedConstDef, with its exact nominal
  declaring owner. Its DefId is an artifact-local reference, not a stable
  cross-artifact row number.
* **R: instantiated requirement.** `(normalized complete declaring interface
  instance, D)`. Substitution creates no new declaration DefId.
* **B: selected binding.** `(resolved witness origin, normalized complete
  implementing type, normalized complete witness interface instance, R)`.
  The selected `23` row denotes B, not D or merely its resulting value.

Resolve the exact witness first. Close its interface and every ordered base
instance by forward substitution, retaining all type/constant arguments.
The finite acyclic inherited closure contains the directly declared D rows
of each exact instantiated interface. Repeated diamond paths to the same R
share one requirement; distinct constructions or declarations remain distinct.
The declaration in each WitnessAssociatedConst row must be directly owned
by the normalized nominal definition of its `declaringInterfaceTypeSpec`,
and that exact complete instance must occur in this closure. Nominal
definition-only membership is insufficient.

Require exactly one binding row per R in that complete witness's closure,
with no extras. Each row lies exclusively in the owning InterfaceWitness's
associated-constant range, and `witnessRow` agrees. Two rows for the same R
are invalid even if their sources/values agree. Inherited rows name the
actual base declaration and constructed base instance, not the derived owner
or a physical ordinal. No missing binding is synthesized from a name, fixed
InterfaceConstDef, alleged default or equal value.

Substitute declaring-owner arguments before checking its domain/source.
The complete implementer and root/declaring interface constructions must
agree with the selected witness context, including constant domains/values
and ordinary arguments that happen to equal self. Only the explicit self
formal is self. Open symbolic obligations require their admitted formal
context; a concrete value use requires its closed actuals and selected
evidence. This selects no generic witness-template codec.

### Origin, expected bindings and using context

The declaration provider and witness origin need not be the same Module.
For nominal witnesses, origin authority follows the implementing declaration;
for primitive witnesses it follows the exact admitted privileged Core
registration, not a fabricated TypeDef. An expected witness is a retained
consumer-side contract for that selected origin, not a new conformance.

Resolve every row in the Module/formal context supplied by its owning witness
contract. An expected witness uses its consumer expectation context; a
provider witness uses its provider origin context. A member Import token
must belong to that using Module. A direct reference naming an expected
stub likewise retains that stub's consumer context and Import obligation.
Satisfied direct provider declarations may be referenced after the required
checks; a still-imported declaring interface must bind to that same owner.
The declaring interface's Module is **not** a default context for the witness,
and a comparing consumer cannot borrow another Module's Import.

For expected/provider witness correspondence, first establish the exact
implementing-owner/origin and witness interface correspondence from the
retained witness graph. Map each expected R through its explicit owner/member
bindings and substitutions to the provider's R; select the unique row for
that R **in that already selected provider witness**. Compare its typed
value contract and all retained dependencies. Do not search another
conformance or use value equality to choose a row. A domain-only requirement
expectation is not a complete `0033` row with a zero/wildcard source.
An expected binding's ConstantSource retains its promised typed value/work
and dependency contract, not a locally authored initializer. Its expected
role must come from the complete surrounding witness context, not a new
sentinel in these fields.

The existing four fields do not independently serialize witness authority,
primitive registration origin, expected-witness rooting or projection
evidence. Their complete surrounding reference/context codec remains a
separate genuine gate. This proposal specifies the correspondence, not an
implicit reconstruction algorithm for absent origin metadata. Until that
gate is met, these rows cannot establish independently consumable selected
bindings merely because `25` can be decoded.

Inherited views and checked qualified-self projections retain their original
B and the exact requested view/mapping dependency. They do not fabricate
another declaration, reselect a value or turn a projected view into a
standalone exported `23` binding. ImmutableData continues to use that origin
binding and its companion implementing-owner substitution rule: zero exactly
when no applicable owner formals exist, otherwise the complete ordered owner
construction agreeing with the witness.

## Typed sources, failure and replacement

`constantSourceRef` stays a nonzero typed ConstantSource reference. Its
assigned alternatives are `1 -> FrozenValueNode`, `2 -> ConstantExecution`,
`3 -> TargetValueBinding`; validate that selected namespace, required
dependencies and result TypeSpec. After exact declaring/witness substitution,
the result domain must equal the requirement domain. A scalar payload or
carrier cannot stand in for a nominal domain, declaration or witness.
Constant-generic arguments retain their separately admitted domains; this
reference does not broaden them.

A formed source retains its exact promised typed value. Required target
evaluation retains the admitted program, inputs, target, limits and closure;
it must complete before a use requiring the materialized value. Identity
binding never replays an initializer or runs a getter/body to find a
declaration, witness or equality. Admitted constant evaluation remains
separate from identity resolution and runtime static initialization.
Preserve promised signed-zero, NaN and decimal observations according to the
selected value codec, not ordinary numeric equality. No richer value codec
is implied by a recognized tag.

Missing/wrong-kind references, owner/closure mismatches, duplicate/missing
bindings, unsupported representations and incompatible contracts refuse
affected use. Failed or exhausted comparison/evaluation is not success,
zero, a default, a different provider or a replacement witness. Source/new
binding formation must reject nonfinite binding expansion under its owner.
Consumers perform required structural/reference/support and binding checks;
they need not re-prove producer-published value/body facts. False semantic
facts remain producer violations under
[reader trust](../specs/representation/reader-trust.md#1-valid-artifacts-and-producer-obligations),
not a new mandatory Body audit.

Replacement re-establishes the selected Module/key, declaration and owner
correspondences in the selected revision, exact domains, R/B relationships,
origin authority and each expected typed value/dependency. Reordered physical
rows are not new identities; changed keys or equal names/values cannot repair
a broken retained correspondence. Preserve alias/equality relationships,
projected obligations and any stronger value/target fact used by folding or
specialization. Satisfying the bare declaration/domain cannot discharge an
embedded selected-binding value obligation. Cache keys include complete
contexts, selected revisions, rules and dependencies, not D alone.
Internalizing an Import or emitting a constant does not patch existing native
code, extend a lifetime or introduce live replacement.

## Remapping and propagation

A meaning-preserving transformation explicitly selects source/destination
schemas and:

1. Maps direct `25` words through AssociatedConstDef, Import-form words
   through Import, and each Import's expected `25` through AssociatedConstDef.
   Maps `ownerInterfaceDef -> InterfaceDef -> TypeDef -> Module`, names
   through String, and owner/member ModuleRefs through their own maps.
2. Maps `witnessRow` through InterfaceWitness, the candidate `23` definition
   through WitnessAssociatedConst, complete type references and nested
   arguments through TypeSpecRef rules, and `constantSourceRef` plus its
   value/program/target dependencies through their distinct typed graphs.
   An associated-type `20` reference uses its own schema, never this union.
3. Rebuilds exclusive declaration/binding ranges, counts, extents and affected
   variable indexes/reference slots. Rechecks uniqueness using resolved R/B
   and preserves Module ownership and each formal/substitution context.
   Clones/remaps shared structures when rewriting one context would change
   another's meaning; does not merge independent expected contracts.
4. Maps downstream ImmutableData direct `23` uses and their owner/typed users
   without admitting `25` or an Import token there. Replaces a declaration
   Import with a direct provider `25` only after complete owner/member checks,
   retaining original expectations and stronger relied-on facts wherever
   needed after folding, trimming or direct binding.

Never substitute matching bytes in values or treat section numbers as kinds.
Shared bound-tree traversal, rewriting, persistence and lowering must carry D,
R, B, origin/projection context and typed source dependencies rather than
reconstructing names or erasing an unsupported obligation. No source lookup,
library authority, runtime storage, target layout, dispatch carrier,
marshalling or native-call authorization changes.

## Exact owner integration

These are coordinated **future amendments**, not edits made by this proposal.

| Owner / current location | Addition or replacement on adoption |
|---|---|
| [Logical records, lines 90–106](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain) | Replace only the associated-constant declaration identity/reference deferral with `25`, this union, owner/domain and selected-binding rules. Keep the no-default rule, associated-type `20` status and all remaining witness-origin/projection/carrier gates. |
| [Metadata, lines 315–324, 725–739, 863–870 and 896–941](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Add `25=AssociatedConstDef` to the DefId list and this declaration to both Import/Export target lists and their repeated ownership descriptions. Specify AssociatedConstReference in exactly `WitnessAssociatedConst.associatedConstDefId`, plus direct ranges, expected stubs, access eligibility, exact domains and witness closure. Keep all four involved table layouts/strides unchanged. |
| [Machine registry, lines 190, 269 and 297–309](../specs/representation/trrm-1.registry.json#L190) | Add `["25","AssociatedConstDef"]` to `metadata.definition_kinds`, explicitly mapped to `0032`; add `"25:AssociatedConstDef"` to both `metadata.import_export_target_sets` arrays. Add a schema-owned two-form reference projection for exactly the field above, with Import expected kind `25`. Preserve sections and every unrelated target set; `23` stays excluded. |
| [Binding/replacement §§2, 4–5](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | Add the exact declaration/domain specialization, R/B selected-witness correspondence, using contexts and retained replacement/remapping obligations. Do not add a witness search or initializer-based comparison. |
| [Reader trust §4](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces), [canonicality §1](../specs/representation/encoding-canonicality.md#1-ordinary-reading-writing-and-auditing), and [binding conformance](../specs/conformance/il-binding-and-replacement.md) | Extend the reference/eligibility and canonical spelling rules with the checks and future cases below, preserving consumer safe consumption versus producer truth. |
| [Definition-kind](immutable-data-definition-kinds.md#integration-boundary-and-remaining-gates), [declaration-reference](immutable-data-declaration-reference.md#integration-owners-and-remaining-gates), and [Feature-admission](immutable-data-feature-admission.md#owner-integration-and-remaining-gates) proposals | Supersede only AssociatedConstDef's declaration/reference/Import decision deferral. Preserve `20/21/22/23/24`, ImmutableData's direct-only closed sets, Feature `19/1` ownership/detection/closure and the independently missing witness/value/access gates. |

GenericParam's TypeDef/MethodDef owner set, Body maps and associated-type
reference positions do not gain `25`. The static persistence/publication
candidate remains separate and unchanged; its selected physical direction
is not an unresolved choice delegated back to this proposal.

## Candidate acceptance cases

Future complete-schema obligations below are **not tests run or support
claims**. Use independently authored vectors and unrelated synthetic providers,
not only one implementation's round trip. Assume other necessary carriers
are admitted except where their absence is the case being tested.

| Class / case | Required result |
|---|---|
| Positive: local `2500_0001`, correctly owned requirement and one complete witness binding with typed source | Resolve `0032` row 1, exact R and B; preserve `0033` row/source identity separately. |
| Positive: consumer owner/member expectations use separate explicit Imports to one eligible provider owner/member | Bind both and compare exact domains; no member-key derivation or owner-only import shortcut. |
| Positive: Import-form and direct proxy-form references name the same satisfied expectation; alias keys and different row/name/String spellings | Same resolved declaration under all retained contracts; each expectation passes. Names and aliases create no identity. |
| Negative: zero, row zero, raw row, `32`, `20/22/23/24`, F2/F3, `FF`, wrong stride, missing table or out-of-bounds declaration | Reject before affected use without masking, table probing or historical fallback. |
| Negative: `1C` points to an Import expecting TypeDef, AssociatedTypeDef, InterfaceConstDef or WitnessAssociatedConst | Reject the wrong expected kind; never follow an Import token in `expectedLocalDefId`. |
| Negative: orphaned/overlapping ranges, wrong row owner, non-interface owner, duplicate InterfaceDefs or wrong `witnessRow` | Reject selected malformed ownership; a same-numbered valid row elsewhere does not repair it. |
| Negative: consumer stub reparented into provider, ModuleRef from another consumer, imported member/owner re-export or unrepresented restrictive access | Reject invalid ownership/publication or refuse unsupported affected use; no inferred permission. |
| Negative: correct member key with wrong provider owner, missing owner Import, ambiguous revision or duplicate Module export key | Binding fails; no same-name, range-position or alternate-provider retry. |
| Negative: equal-width/signedness-different or nominally distinct domains; lost qualification or out-of-scope formal | Exact domain comparison fails; representation equality does not satisfy it. |
| Negative: unbound declaration, missing/duplicate/extra R, exact nominal base with wrong constructed arguments, or invented default | No complete binding; equal values and unused requirements do not excuse the mismatch. |
| Negative: zero/high-byte source, wrong ConstantSource target/tag, or result domain different after substitution | Reject structural/domain mismatch; do not reinterpret a DefId, FrozenValueNode or Blob as ConstantSource. |
| Boundary: first/last admitted 24-bit rows for declaration, Import, witness and source; empty and maximal valid ranges | Respect each actual table's bounds and checked extent/count arithmetic, including exclusive range endpoints; no portable resource-minimum claim. |
| Boundary: declaration, InterfaceDef, Body, Import, ConstantSource and associated-type tables all have row 1 | Each field selects only its exact namespace; `25` is not section `0025`. |
| Cross-Module: interface/declaration provider A, implementing witness origin B, and expected witness consumer C | Member/owner Imports owned by B/C bind A's declaration/owner; origin and consumer expectation contexts remain distinct. Map the selected origin's exact binding without borrowing A's Module or inventing a `23` Export. |
| Cross-Module: standalone `23` Import/Export or two equal-valued bindings from different implementers/origins | Reject standalone exchange and identity substitution; `25` Import supplies only the requirement. |
| Generic: one D under two type constructions, two constant arguments or two generic implementer constructions, including unused arguments | Distinct applicable R/B identities; exact ordered substitutions and argument domains/values survive. Equivalent valid TypeSpecRef spellings may normalize equally. |
| Generic: open expected/provider domain terms agree only for one closed actual; symbolic context is missing | Do not accept the open comparison or fabricate a template/witness; require supported complete correspondence. |
| Inheritance/projection: diamond repeats one R versus two differently constructed bases; requested qualified-self view changes only explicit self | Deduplicate only repeated R; retain original B and complete checked projection mappings, not ordinary equal-to-self argument rewriting or value reselection. |
| Primitive origin: exact admitted Core registration versus a type-shaped row with no origin evidence | Admit only the represented authoritative origin; never fabricate a nominal TypeDef or infer origin from the declaring interface. |
| Values: identical typed bits for different D/B; required evaluation incomplete/failed; promised signed-zero/NaN/decimal observation changes | Preserve identity independently of value; materialization cannot silently succeed or replay runtime initialization; incompatible retained value facts fail replacement. |
| Replacement/remapping: row permutation, same keys under a new revision, folded value, direct-bound Import and one shared structure used by two consumers | Rebuild typed edges/ranges, retain expected/stronger dependencies and alias/owner/witness correspondence, clone changed contexts and invalidate stale comparison results. |
| Feature composition: declaration/witness without ImmutableData versus an admitted root-6, execution-dependency or input-view use of its `23` identity | This proposal alone adds no Feature-19 requirement; retained ImmutableData uses follow the companion's independent `19/1` detection and actual value/witness/graph closure. `25` never becomes an ImmutableData target. |
| Trust/support: false published typed value versus malformed references versus unsupported origin/access/value carrier | Distinguish producer truth violation, mandatory safe-consumption rejection and unsupported affected use; no mandatory body re-proof or success-shaped fallback. |
| Schema: equal `(1,0)`, known section or recognized candidate byte without exact combined draft/catalog agreement | Do not infer this codec, capability closure or executable support. |

## Remaining gates and status

No declaration-number, direct-versus-Import or selected-member comparison
choice within this recommendation is left to implementation. Approval may
still accept, amend or reject these candidates.

Genuinely separate missing decisions remain the complete witness
origin/expected-witness/context and projection/evidence carriers, richer
access/source-unit records where required, and value/evaluation forms not
already covered by a complete admitted codec. `0033` cannot fill those gaps
by carrying a name, scalar or guessed Module. StaticField physical/publication
choices already have their own unapproved recommendation; their adoption
and support, not another choice here, remain separate gates.

Acceptance still requires coordinated normative/registry integration of one
exact combined draft and catalog, complete per-Module capability support,
and implementation/qualification of declaration resolution, Import/Export,
WitnessAssociatedConst producer/reader, exact substitution/closure binding,
replacement, remapping and lowering. A structural reader or a passing
assignment-only fixture cannot establish that acceptance.

[Format versioning, lines 19–36 and 62–78](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
requires exact draft/schema selection out of band. Unpublished assignments
remain amendable without changing `(1,0)` or retaining compatibility readers.
Equal version bytes, a manifest or plausible operands do not select this
proposal; no sniffing, historical alias or byte-only conversion is introduced.
The document remains unapproved and provisional solely under the user's
direction.
