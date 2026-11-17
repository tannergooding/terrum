# Core Pure schema identity selection

> [!IMPORTANT]
> **Status: UNAPPROVED; provisional direction only; non-normative.**
> This selects an authority-to-binding handoff, not new evaluation semantics,
> normative adoption, compiler support or permission to bypass verification.

The authorizing user instruction is:

> “At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction. I will go and appropriately review/approve those proposals at a future point; this allows you to continue work without being blocked.”

## Decision and evidence

Recommend one nonserialized **`CoreEvaluationFormIdentitySelection` per using
logical Module**, with independently optional exact `PureSchema` and
`ConstEvalSchema` selectors. For a source-local role, supply an explicit
**`CoreEvaluationFormSchemaDeclarations` host declaration/export catalog in
that selection's `LocalSchemaDeclarations` field**. The catalog itself
declares the schemas; it does not claim that annotation occurrences or
otherwise nonexistent source schema declarations define them. External roles
still resolve only through the using Module's ordinary checked reference.
Live Count consumes the resulting checked pure-form capability; it does not
own another Pure registration.

[LCI-24 and Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity)
already select the roles, public primary declarations and exact-identity
requirement. They expressly leave missing Module/schema selectors to the
selected profile. That establishes authority, not a concrete source-binding
input or its reference handoff. The missing decision is that handoff, **not
whether Pure exists or what it means**.

| Inspected owner or source | Boundary |
|---|---|
| [Checked annotations and forms](../specs/language/published-predicates-and-effects.md#checked-annotations-and-forms), [attribute rules](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | Recognize resolved Core schemas, not matching names; validate their accepted argument, target, primary-authorship and multiplicity contracts. ConstEval implies pure-form support; Pure alone is not required-constant opt-in. |
| [Names and binding](../specs/language/names-and-binding.md#calls-conversions-and-recorded-binding) | Ordinary lookup selects declarations before role recognition. Aliases preserve identity; names, equal shapes and downstream lookup cannot replace the recorded binding. |
| [Logical evaluation-form records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms), [reader trust](../specs/representation/reader-trust.md#1-valid-artifacts-and-producer-obligations) | Published forms and their supported records are distinct from source schema authorization. Consumers check safe structure, support and binding, not arbitrary producer Body truth. |
| [Format versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts) | Catalog origin is not an artifact decoder. Independent exchange selects one exact complete schema; equal version bytes or recognizable payloads cannot choose a schema codec. |
| [Registered queries](../specs/language/registered-queries.md#identity-and-authority) | The synchronous Pure Count getter is already accepted. Neither Count proposal below selects its Pure schema. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | ConstEval currently has token-spelling recognition; there is no corresponding Pure binding or BoundFunction evaluation-form/schema-identity carrier. That is implementation debt, not acceptable identity selection. |
| [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) | `TryBindInterfacePropertyGetter` rejects member/accessor attributes and contract tokens. Admitting and checking Pure remains general getter/witness work. |
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | Other explicit Core selections have options/reference transport; neither provides a Pure/ConstEval schema selection. |
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | `CoreProviderModuleOrigin` already describes an exact provider/catalog origin. `CoreProviderModuleBindings.GetInvalidReason` rejects provider identity equal to its using Module. `CoreProviderDeclarationSnapshot` has only enum, native, layout and initialization/output schema payloads, with no evaluation-schema payload or separate export-alias-to-definition map. None is the missing local carrier. |

These are source-inspection observations, not executed tests.
[Contract-failure selection](core-contract-failure-identity-selection.md#selection-carrier-and-authority)
provides useful Module/export and checked-local-map policy, but its schema
slot selects RequireException only and leaves that local map abstract.
The [provider-origin proposal](core-provider-module-origin-carrier.md#proposed-host-input-and-adapter-shape)
permits actual in-process declaration catalogs without pretending artifact
rows exist; reuse that origin/expectation policy, not the native carrier's
external-only admission rule.
[Source-layout registration](source-layout-core-registration.md#candidate-host-api-and-complete-schema-projection)
also separates a complete schema descriptor from role selection.
[Intrinsic registration](intrinsic-registration-identity.md#decision-and-boundary)
identifies an admitted execution program/catalog entry, not an annotation
schema; it expressly supplies no source annotation.
[Count witness selection](core-countable-witness-selection.md) and
[Count declaration origins](core-countable-declaration-origin.md) remain
separate provisional decisions and are unchanged. Count can select actual
interface/property syntax occurrences; the accepted grammar has no analogous
schema-declaration node. Copying that source-occurrence route would leave
this gap unresolved.

## Complete candidate input

These are logical in-process fields, not existing APIs or wire records:

| Field | Meaning |
|---|---|
| `UsingModuleIdentity` | Exact logical Module whose source/schema semantics use this selection, associated with its immutable source/reference/options binding snapshot. |
| `PureSchema`, optional | Exact `(ProviderModuleIdentity, ExportKey)` selecting the public primary semantic AttributeSchema definition for Pure. |
| `ConstEvalSchema`, optional | The equivalent selector for the distinct accepted ConstEval schema. |
| `LocalSchemaDeclarations`, optional | The concrete catalog below. Required exactly when at least one selected role's provider equals `UsingModuleIdentity`; otherwise absent. It is not a second options property or an external provider attachment. |

Required declaration family is **semantic AttributeSchema**, not an attribute
constructor, TypeDef, MethodDef, intrinsic registration or a newly assigned
physical DefId kind. Each selected definition must match its complete accepted
schema, including zero arguments, applicable callable/accessor/eligible
initialization-constructor targets, primary authorship and nonrepeatability.
The role slot cannot redefine those rules.

Each selector has exactly one origin:

* **Local:** provider equals the using Module. Resolve the key in
  `LocalSchemaDeclarations` after its complete validation and association with
  this source binding snapshot, before binding affected annotations. The
  selected catalog definition is owned directly by that Module; manufacture
  no Import proxy, `CompilationReference` or external dependency for it.
* **External:** resolve the key through the selected provider's real checked
  schema export map in an already supplied ordinary `CompilationReference`.
  Add the bounded catalog projection below to that shared reference path;
  a consumer expectation or Module string alone is not an actual provider
  definition. `LocalSchemaDeclarations` cannot supply an external provider.
  The selector adds no second provider dependency or alternate binding context.

Both routes retain the actual declaration correspondence, complete original
expected schema and selected provider snapshot: logical Module, unambiguous
revision, exact artifact schema or in-process catalog/contract selection,
governing semantic requirements and target/runtime context. Source/catalog
maps are not a claim that Standard schema-declaration codecs exist.
[`CoreProviderModuleBindings`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs)
offers related checking machinery, but currently rejects a provider equal to
the using Module and has no evaluation-schema projection. Do not force this
selection through its native/layout roles or silently waive its invariants.

## Concrete declaration/export catalog

The following are **unapproved in-process API candidates** in
`Terrum.CodeAnalysis.Semantics`, not existing APIs, source syntax or serialized
records. Select a dedicated immutable `CoreEvaluationFormSchemaDeclarations`
class with a validating `Create` factory and these contents:

| Field | Exact carrier |
|---|---|
| `Origin` | Existing `CoreProviderModuleOrigin`, including explicit Module identity, producer revision, whole schema/catalog identity and revision, governing semantic edition and target/runtime requirements. No omitted selection is filled from the consumer. |
| `Declarations` | Initialized `ImmutableArray<CoreEvaluationFormSchemaDeclaration>` of actual catalog-owned schema definitions, not consumer expectations. At least one definition is required. |
| `Exports` | Initialized `ImmutableArray<KeyValuePair<string, CoreEvaluationFormSchemaDeclaration>>`, mapping exact export keys to those definitions. Accept a sequence so duplicate keys cannot disappear during dictionary construction. At least one export is required. |

`Create` takes those three fields, validates the complete input, and publishes
no partial catalog. Keep any lookup dictionary ordinal and immutable.
All declarations must have an export; every export target must be an actual
object in `Declarations`. Reject null entries, duplicate declaration objects,
duplicate export keys even with identical targets, dangling targets and
ordinary declaration-space collisions. Distinct keys may alias one actual
definition. Do not deduplicate separate definitions by equal fields or names.

`CoreEvaluationFormSchemaDeclaration` is an immutable reference class, not a
value-equality record. Each definition has the following complete bounded
schema projection; there is no independently configurable Pure/ConstEval role
tag on it:

| Field | Required value or meaning |
|---|---|
| `QualifiedName` | The actual catalog declaration's source-qualified name, valid under ordinary source naming rules. Supply it explicitly; derive neither it nor an export key from a role name. |
| `GenericArity` | Exactly `0`. |
| `ArgumentCount` | Exactly `0`: the complete empty argument schema, not merely zero required arguments. An optional/defaulted argument is still an argument and rejects. |
| `Targets` | Exactly the immutable set of the three in-process categories `OrdinaryNamedCallable`, `IndividualAccessor`, `EligibleInitializationConstructor`. Missing, extra or unknown categories reject. The accepted owner determines actual target eligibility. |
| `IsPublic`, `IsPrimary` | Both `true`, describing the schema definition. |
| `IsPrimaryTargetOnly` | `true`, independently restricting authorship of the annotated callable/accessor's contract. |
| `IsRepeatable` | `false`. |

Every definition has semantic declaration family `AttributeSchema`; no native
role enum or physical DefId is used. Zero arguments need no new argument-value
domain. The target categories introduce no source placements: ordinary
getter-targeting maps to that accessor, and whole-property, type, field,
parameter, result, lambda and static-initializer permissions are not added.
Eligible initialization constructors retain the accepted vacant-result
receiver rule; a live receiver's mutation does not qualify by this label.

Within one catalog snapshot, the actual declaration correspondence is
**the catalog object plus its declaration object**, not the key, qualified
name, array index, descriptor contents or a content hash. Aliased exports
therefore resolve to one definition, and Pure/ConstEval selecting that same
definition rejects even through different keys. This is only an in-process
handle: it assigns no cross-process declaration ID. Later correspondence
must use the checked declaration maps of the selected binding route.

### Source-local delivery

The authorized host adapter supplies this catalog as the **declaration source**
for these schemas in the current using Module. It is not a description of
presumed syntax declarations, nor an assertion that written attributes have
already been checked. No schema syntax, attribute class, constructor, source
node or public bound-symbol API is needed to construct it.

Require `Origin.ModuleIdentity == UsingModuleIdentity`. Validate the explicit
origin/catalog selection and applicable semantic/target/runtime requirements
against this invocation's other admitted inputs. The compiler captures the
catalog together with the actual source, options and ordinary reference
binding snapshot; equal Module strings do not establish that association.
Reusing immutable catalog input in another compilation requires a fresh
binding context and all checks again, not reuse of the earlier admission.
A publication Module identity must agree with the captured using Module.
Retain the supplied local declaration projection and the complete accepted
role-schema expectation; neither requires a fabricated consumer Import.

Install the checked declarations into ordinary schema declaration/lookup
spaces before affected annotations are bound. Their explicit qualified names
participate in normal visibility, namespace/import, collision and alias
rules. That is name lookup of actual declarations, not spelling-based role
recognition: only the resolved declaration selected by the exact key has the
role. An annotation occurrence, matching source type or `CreateCoreLibrary`
mode cannot supply a missing catalog or authorize one.

### External delivery

Add an internal candidate
`CompilationReference.CoreEvaluationFormSchemaProviders` projection containing
these same catalog/declaration/export facts for actual checked provider
snapshots. Build it only from the ordinary reference's selected provider
definitions and real export correspondence. An in-process checked provider
can supply the catalog as its actual declaration source; an artifact-backed
provider needs an independently admitted exact schema capable of representing
the declarations. Consumer expected schemas remain separate from both.

For each external selector, require exactly one agreeing provider snapshot in
that reference, with `Origin.ModuleIdentity != UsingModuleIdentity`; resolve
the key there and retain its actual definition correspondence and the using
Module's complete original expectation. Reuse an existing checked provider
handle across export aliases rather than creating one definition per key.
Conflicting revisions, independently inconsistent snapshot projections or
an imported expectation masquerading as a public provider-local definition
reject. Another Core registration using that provider must agree with the
ordinary admitted snapshot, not merely its Module label.

The compact `Origin` value does not replace the surrounding binding closure:
retain the complete governing requirements, actual source/catalog or artifact
schema selection, target/runtime context and every original expected schema
with the resolved selection. Extending an internal checked reference
projection does not add a public reference-construction factory, discovery
request or another dependency resolver.

## Authority, rejection and handoff

The designated Core authority/profile supplies the selectors and checked
origins. Referencing Core, exporting a same-shaped schema or compiling a
package called Core grants no authority. Select no production Module,
namespace, export key or default here. Identities/keys are nonempty,
well-formed ordinal decoded text without NUL; never derive them from names,
paths, rows or content hashes.

Allow at most one entry per using Module, with at least one role present.
Reject duplicate entries even if identical, option/reference overlaps,
malformed or unresolved supplied selectors, unauthorized origins, conflicting
provider snapshots and incompatible schema/owner correspondence, including
unused supplied roles and catalog entries. Validate catalogs before exposing
any selected schema to binding. Null required reference inputs fail with
`ArgumentNullException`; malformed supplied catalog/selection configuration
fails with `ArgumentException` at its validating entry point. Source lookup,
placement and body errors use ordinary compiler diagnostics; unsupported
required schemas or binding routes decline affected use. Authorization is
the host's obligation, not something these constructors authenticate.
Preserve every original expectation when aliases select one declaration.

An absent role is legal only without a required authored/retained dependency
on that schema. Missing required input diagnoses or declines affected binding;
there is no spelling fallback, import-order winner or silent downgrade of a
required semantic schema to an inert attribute. Ordinary same-named user
attributes supply no evaluation authorization.
The slots are independent: Pure use needs no ConstEval selection, and a
checked ConstEval contract implies pure-form support without an additional
Pure annotation or schema selection merely to express that implication.

1. Add optional candidate `CompilationOptions.CoreEvaluationFormIdentitySelection`;
   options-bearing source factories forward it, including its optional local
   catalog. Resolve before affected annotation binding, including early
   ConstEval-dependent declaration/type binding, then use ordinary schema
   lookup and exact identity equality, including identity-preserving aliases.
   Remove neither a required check nor an original expectation while replacing
   the current spelling recognition. Selection is not proof of a callable's
   promised form.
2. Retain the resolved selection, original expectations and originating
   using-Module context with `Binder`, `BoundProgram` and reference projections,
   including the actual catalog/definition/export correspondence. Candidate
   `CompilationReference.CoreEvaluationFormIdentitySelections` carries those
   originating contexts, not a provider's defaults for a new consumer. This
   is separate from the provider-definition directory above: retaining a
   provider's selection does not designate it as Core for new source.
3. Source-independent schema-role validation needs that same explicit
   accompanying context and checked declaration maps. Remapping, packing and
   replacement preserve identities and every retained expectation under
   [ordinary directed binding](../specs/representation/binding-and-replacement.md).
   They cannot reselect schemas using a later consumer's Core defaults.
4. An already admitted published evaluation-form contract is consumed under
   its existing structure/support/binding rules, not by repeating source-name
   lookup or auditing provider Bodies. Keep the
   [evaluation-form records and schema gates](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
   distinct from source authorization. Where a route must retain schema
   dependencies but cannot represent them, decline that route; do not
   serialize a host authority grant or erase the dependency.

### Replacement and source-independent use

Retain local-versus-external origin, every original key and expectation, actual
schema ownership and all relied-on identity equalities. Packing does not turn
the using Module's catalog declaration into an external expectation or merge
its owner with another Module. Remap actual definitions through checked
declaration correspondence; process object identity is not serialized.

Replacement explicitly selects a new actual provider snapshot and checks
every retained original expectation in its original using context under
[directed binding](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts).
A new producer revision can satisfy those obligations; neither an equal
revision string nor byte-identical descriptor contents establishes it.
Preserve the complete zero-argument/target/primary/multiplicity contract and
every alias equality: splitting two retained aliases to different definitions
rejects, as does collapsing the distinct Pure/ConstEval definitions.
Do not overwrite expectations with a replacement's contract or reselect roles
from current host defaults.

The assigned Standard Import/Export target families do not supply a semantic
AttributeSchema declaration codec. This catalog is not such a codec and adds
no TypeDef, MethodDef, Blob, optional debug attachment or private wire tag.
An already admitted published evaluation-form record may be consumed under
its own complete support/binding rules without repeating source authorization
or re-proving producer Bodies. A route retaining a schema-selection dependency
must preserve its complete supported accompanying context or decline.

## Alternatives and remaining gates

| Alternative | Assessment |
|---|---|
| Count-specific Pure selector inside Count selection | Locally smaller, but gives Count ownership of a general callable/accessor schema and duplicates the handoff for non-Count purity and ConstEval. |
| Bounded per-Module selector plus dedicated local declaration/export catalog | Recommended minimal amendment: preserves the existing role handoff, reuses `CoreProviderModuleOrigin`, and supplies the missing actual declarations and alias map without changing unrelated native/layout registrations. |
| Extend `CoreProviderModuleBindings` and `CoreProviderDeclarationSnapshot` into a common local/external Core catalog | Coherent larger redesign, but requires intentional changes to the self-provider invariant, payload union, ownership, alias mapping and all existing registration composition rules. It offers no required capability beyond the bounded catalog for this slice. Reuse validation/origin helpers without silently making that redesign. |
| Select source schema occurrences or new public bound-schema handles | No existing schema-declaration production or source symbol provides such an occurrence. Adding syntax or a staged semantic API is larger work and unnecessary when the host catalog itself declares the schemas. |

**Current implementation gap:** the catalog, evaluation-schema descriptor,
provider directory and evaluation selection/options/reference/BoundProgram
transport above do not exist. Implementing them is the identity-carrier slice,
not pure-form checking. Ordinary identity-based annotation binding, general
callable/getter/witness pure-form verification, bound traversal/rewriting and
mandatory verification, and independently admitted persistence/consumer/
lowering support remain dependent work. Existing token-spelling ConstEval
recognition is not evidence that these gates are met.

**Remaining selections/decisions:** production provider identities, revisions,
export keys and exact admitted catalog selections are host inputs, not defaults
chosen here. Physical schema declaration/reference codecs, CLI/manifests and
any public source-reference construction API remain separate decisions. The
accepted Pure target/form rules still require implementation, not a new
NoThrow or evaluation-semantics decision. No local-carrier policy is deferred
to a future unspecified declaration map.

Future qualification must cover these cases; none was run by this
documentation change:

| Case | Required outcome |
|---|---|
| Source-local catalog with exact origin and one or both independently selected schemas | Bind actual local declarations without a synthetic reference or schema syntax; Pure-only needs no ConstEval role and ConstEval-only implies pure-form support without a Pure selector. |
| External checked provider with the same supported schema shape | Resolve only its real reference export map, retaining actual provider origins separately from every consumer expectation; no local catalog is required. |
| Mixed local/external roles or distinct external providers | Validate each origin independently in one coherent using context; no requirement that both roles come from one provider. |
| Local selector without its catalog; external selector supplied only through a local catalog; local catalog with no local role | Reject supplied mismatches or diagnose missing required selection before affected binding; never search source names or attach an implicit provider. |
| Default/uninitialized arrays, null/duplicate definitions, duplicate keys, dangling/foreign catalog targets or malformed origin/schema selection | Reject the whole submitted configuration, including unused entries. No partial schema admission. |
| Empty/invalid text, embedded NUL or malformed scalar text; case- or normalization-distinct valid keys | Reject malformed fields; otherwise retain ordinal distinction without trimming or normalization. |
| Different export aliases selecting one definition; both role selectors aliasing that definition | Preserve identity for ordinary aliases, but reject Pure/ConstEval role collision. |
| Same-named/shape decoys, missing exports, wrong family/owner, nonpublic or nonprimary declarations | Supply no evaluation authorization; reject incompatible selected declarations rather than selecting another candidate. |
| Added optional argument, generic arity, changed target set or repeatability; duplicate or secondary annotation placement | Reject the changed schema or invalid authored occurrence; getter-targeted and inline occurrences use ordinary effective-target multiplicity. |
| Reparse/rebind, changed reference/options context, options/reference overlap or conflicting provider revisions | Capture and validate the actual new binding snapshot; reject duplicate selections even if identical and never reuse a stale admission. |
| Pure-only, ConstEval-only and redundant combined annotations on ordinary callables, accessors and eligible constructors | Retain distinct schema identities and accepted authorization/implication rules; the separate form verifier must still establish each claimed capability. |
| Remapping, source-free retained-role checking and provider replacement with several original expectations | Preserve owners, every key/expectation and alias equality; reject lost context, split/collapsed identities or incompatible schema changes. An unavailable codec declines affected exchange rather than losing semantics. |

If adopted, add this explicit local-catalog/external-reference selection
handoff to **Checked annotations and forms**, with references from LCI-24 and
the existing logical-record owner. Preserve the accepted names/binding,
evaluation-form records, reader-trust and exact-format rules; change no grammar
production, registry assignment or encoding. This concretizes only this
proposal's formerly unspecified local map, replacing no accepted Pure
semantics, ConstEval implication or Count rule and superseding neither Count
proposal. No NoThrow promise, body-effect design, Core default or runtime
policy is selected. This documentation change implements nothing.
