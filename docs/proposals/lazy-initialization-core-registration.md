# LazyInitialization Core registration

> [!IMPORTANT]
> **Unapproved; provisional recommendation under the user's authorized
> missing-contract workflow.** This document permits no inference of normative
> adoption, a production Core provider, a wire allocation or compiler/runtime
> support. Its recommendation may be implemented provisionally under that
> authorization; approval remains a separate review.

## Decision and scope

**Recommend one independently selected, Core-authorized LazyInitialization
schema definition L per using logical Module U.** Its candidate public source
name is `Terrum.LazyInitialization`; it is a public, nongeneric, primary
semantic attribute schema with zero arguments, nonrepeatable placement and
primary ownership on one ordinary authored non-`extern` static field.
Selection supplies L's exact declaration identity and checked provider
snapshot, not a name recognizer or a fixed Module/export key.

Reuse the per-using-Module registration and origin projection established by
[static trigger identities](static-initializer-trigger-identities.md#identity-authority-binding-and-replacement)
and the [Core provider origin carrier](core-provider-module-origin-carrier.md#proposed-host-input-and-adapter-shape).
**Choose a separate `LazyInitializationCoreRegistrationSet` sharing
`CoreProviderModuleBindings`; leave the existing
`StaticInitializationCoreRegistrationSet` and its required A/E/F/M constructor
closure unchanged.** L is not an argument or case of StaticInitializer. A host
may select it without selecting the constructor attribute, trigger enum or cases
when that Module authors no static constructor. Supplying those other roles
does not register L; selecting L does not manufacture them. A complete host
profile may supply both closures, including from different authorized providers
within one shared checked binding snapshot.

The genuine gap is the source catalog/registration selection. The
[accepted grammar](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage)
requires exact Core identity but does not complete that catalog.
[LCI-37](../specs/libraries/adopted-inventory.md#2-selected-declarations-and-compiler-roles)
is a family, not an export map. The trigger proposal explicitly excludes
LazyInitialization selection. The
[static persistence proposal](immutable-static-field-persistence.md#group-trigger-initializer-and-lazy-contracts)
encodes normalized lazy-unit relationships, not source attribute authority.
This proposal closes only L's source-registration deferral.

## What the accepted text already answers

These are preserved obligations, **not new proposals for their behavior**.

| Point | Existing answer and owner |
|---|---|
| Field eligibility and policy | The static grammar selects an individually demand-initialized ordinary non-`extern` static field, outside the ordinary group initializer sequence. Constants and foreign storage acquire no Terrum initialization obligation. The annotation configures no trigger or alternative policy. |
| Arity, repetition and ownership | The grammar supplies zero arguments; [SIID-14/15](../specs/conformance/source-static-initialization.md#declaration-identity-and-ordering) require nonrepeatable, primary-owned placement and rejection of ineligible/`extern` fields. |
| Identity and authority | Only the registered Core declaration selects semantics. [Inventory §1](../specs/libraries/adopted-inventory.md#1-status-and-identity) already requires public primary Core declarations, checked roles and identity-preserving transparent aliases; Core is an authority, not a fixed package binary. |
| Ordinary target selection | [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations) already permits direct and exact named-target placement, rejects misplaced/ambiguous targets and shares multiplicity across placements. Contract-changing schemas cannot move authorship to a secondary. |
| Partial-type contributions | [Grammar §5.12](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization) gives a new field contributed by a secondary type part its own primary declaration. Fields themselves do not admit member-level `partial`. |
| Initialization validity and execution | Ordinary type, effect, access and completion checks remain required. External lazy access completes the eager group first, then only the requested lazy unit. Closed generic owners and independent loads have distinct state. |
| Failure, concurrency and lifetime | Existing waiting, cycle, escaping-event/fresh-failure, cleanup, exclusive failed-unit retry, publication and checked-unload rules remain unchanged. Laziness grants no sharing, unsafe access, immutable publication or lifetime extension. |
| Physical representation | [Logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending) require distinct field/group/lazy/access/dependency identities. They leave physical attribute identities and runtime carriers open. |

The new recommendation supplies L's candidate public spelling, nongeneric
schema shape, concrete role selection and retained registration graph. It
does not duplicate the accepted execution rules or reopen either authored
constructor trigger, StaticInitializer's enum/member mapping, initialization
role tags, failed-unit recovery direction or immutable backing decisions.

## Candidate source schema

The following is a **proposed catalog contract**, not an existing Core export,
a runtime attribute class or runnable library definition.

| Axis | Exact proposed contract |
|---|---|
| Declaration identity | L is one exact checked semantic `AttributeSchema` definition, selected by provider logical Module identity, nonempty public export key and required kind, or equivalent checked handles in the same binding context. |
| Source declaration | Public primary schema; candidate qualified name `Terrum.LazyInitialization`; generic arity exactly zero. No attribute base type, object construction, inheritance or runtime instance is introduced. |
| Arguments | The complete ordered argument list is empty. No positional/named slot, default, trigger enum, variadic input, type recipe or runtime expression exists. `[LazyInitialization]` and `[LazyInitialization()]` both supply zero arguments. |
| Target | One ordinary authored `FieldDecl` with `static` and without `extern`. Untargeted placement or explicit `field:` on that declaration denotes the field. An exact `field(Name):` selector on its enclosing declaration is also valid under ordinary applicability/access/primary-ownership rules. |
| Excluded targets | No type-wide or Module-wide opt-in; no instance field, associated/interface constant, imported expectation, foreign field, constructor, method, accessor, parameter, result, property or generated property backing. A field selector cannot turn a property/backing declaration into an ordinary authored field. |
| Multiplicity | Optional on an eligible field; exactly one placement if selected. Nonrepeatable by resolved target and L identity, including equal annotations, transparent aliases and mixed direct/selector placement. |
| Primary ownership | The field's own complete declaration owns the contract. A field newly contributed in a secondary partial type part may carry it. A secondary cannot annotate another declaration to repeat/change its primary contract; repeating a field declaration does not create a second author. |
| Normalized meaning | Select that exact field's independent lazy initialization unit, associated with its owning closed type and load. Its initializer is excluded from the eager initializer sequence; ordinary external access retains prior eager-group completion. |

The attribute fragments in this table use candidate source names and accepted
target syntax. They are not evidence of current binding or execution support.
Placement on an enclosing type **without** an applicable field target does
not select all its fields; no missing selector guesses the only field or
falls back to the enclosing declaration.

Eligibility does not require a public, fixed, scalar, Copy or non-generic
target field. Ordinary admissible static types, `mut` storage and
caller-`unsafe` fields retain their existing contracts. A selected schema
does not waive representation, initialization, effect, ownership, backing,
cleanup or sharing obligations. A zero-data `void` field still needs valid
initialization and completion; lack of payload bytes is not an initialized
default. An absent or invalid initializer cannot be repaired by inventing a
value, moving the field back into eager sequencing or inferring authority
from the static constructor's call path.

LazyInitialization does not promise the whole-inline immutable publication
defined by the static persistence proposal. Conversely, an immutable sidecar,
readonly slot or pure initializer does not select laziness or discharge its
runtime timing, failure and lifetime obligations.

## Registration, lookup and admission

The proposed logical registration contains:

* U's exact using-Module identity and immutable checked binding context.
* L's selected declaration identity, complete original expected schema and
  actual selected public primary definition.
* Its agreeing provider origin: exact logical Module identity, unambiguous
  selected producer revision, explicitly selected whole schema/catalog,
  governing semantic requirements and actual target/runtime requirements.
* The fixed mapping from this registered definition to the lazy-field role.

These are logical host inputs, not wire fields. The embedding host authorizes
Core/provider selection and its adapter. The adapter supplies the real checked
definition and export map. An ordinary package cannot self-register merely by
referencing Core; descriptors, source spellings and serialized data do not
authenticate or authorize a provider. This adds no new Core self-registration
route or second provider resolver.

Resolve provider identity, key and required kind **before** checking the
complete schema and definition correspondence. Apply the existing
[ordinal identity and selected-revision rules](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities).
The selected declaration's argument list must really be empty, target set
exact, arity zero, access/primary facts valid and repetition false. A
StaticInitializer schema with its enum argument cannot fill this role.
Missing definitions/origins, conflicting selected revisions, wrong kinds,
changed schemas or unsupported required contracts fail admission; no partial
successful L binding is published.

### Chosen hosted registration and source selection

These are **candidate in-process API contracts**, not implemented declarations,
source syntax, wire records or authorization tokens.

| Carrier or entry point | Exact selected shape |
|---|---|
| `CoreLazyInitializationAttributeSchema` | One immutable typed descriptor of L with the complete schema axes above. Add its own arm to `CoreProviderDeclarationSnapshot`; do not reuse the one-enum-argument StaticInitializer schema. |
| `LazyInitializationCoreRegistrationSet.Create(CoreProviderModuleBindings moduleBindings, CoreLazyInitializationAttributeSchema lazyInitialization)` | A separate factory with exactly these two required inputs. Reject null/invalid inputs, missing or disagreeing selected schema/provider snapshots, and any schema that is not exact L. It accepts no StaticInitializer or trigger-enum input and creates no placeholder constructor closure. |
| Registration properties | `ModuleBindings` retains the supplied immutable snapshot; `UsingModuleIdentity` derives from `ModuleBindings.UsingModuleIdentity`; `LazyInitialization` retains the exact supplied checked L schema. Validation covers the complete L/origin/expectation closure. |
| `CompilationOptions.LazyInitializationCoreRegistration` | A separate nullable `LazyInitializationCoreRegistrationSet` option. Null means no configured L selection, not an inferred/default provider. Existing `StaticInitializationCoreRegistration` keeps its current constructor-only meaning. |
| `CompilationReference.LazyInitializationCoreRegistration` | A separate optional host-profile slot, accepted as `lazyInitializationCoreRegistration` when constructing the reference, with the same exposure convention as existing registration slots. It retains the registration object and its exact using context, separately from normalized provider declarations. |
| `BoundProgram.LazyInitializationCoreRegistration` | Retain the independently selected L registration, including its exact using-Module/snapshot association, through binding, shared rewriting and reference construction. Do not store it in the constructor registration slot. |

The present
[constructor factory](../../sources/Terrum.CodeAnalysis/Semantics/StaticInitializationCoreRegistration.cs)
requires both the trigger enum and StaticInitializer schema. Merely adding an
optional L parameter to that factory would still force A/E/F/M selection and
is **not** this recommendation.

Use the existing
[options/compilation](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs)
and [reference](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
single-supply and shared-snapshot conventions, with the following explicit
independent selection:

1. Validate every present L registration in options/reference construction.
   A missing constructor registration is valid; neither L lookup nor L's
   provider directory may populate it. Having constructor definitions in a
   shared directory is not selecting their compiler roles.
2. At each source-compilation entry point, select L from the new options slot
   or the reference's new slot. If both are nonnull, reject duplicate supply
   **even if they are the same object or have equivalent contents**. There is
   no options-over-reference precedence or merge. If exactly one is present,
   require its complete validity and retain it; if neither is present, select
   no L. Keep the constructor role's existing duplicate-supply check separate.
3. After independent role selection, check the combined selections from
   options **and** references before binding. Preserve the current rule that
   all selected native/layout/constructor/lazy Core registrations for this
   using compilation share the **same `CoreProviderModuleBindings` snapshot
   object**, not merely equal Module-name strings or copied origins. That
   snapshot fixes U and the unambiguous actual provider selections. L alone
   supplies this association without any constructor selection.
4. An options-supplied L and reference-supplied constructor registration, or
   the reverse, are valid when their required closures pass and they share
   that snapshot. Different using Modules or snapshot generations reject;
   conflicting provider revisions/schema/origins or declaration mappings
   also reject under the checked snapshot mechanism. Do not fix a conflict by
   choosing either source, dropping a role or manufacturing origins.
5. A host wishing to combine independently assembled snapshots must explicitly
   create one checked combined snapshot and registrations against it, retaining
   every original expectation. This is a new binding generation requiring
   revalidation, not an automatic compilation/reference merge. Constructors
   remain unselected if the host creates only L against that snapshot.
6. Reference copies and bound-program transformations preserve the selected L
   object and originating context. A reference's explicit L slot configures
   its designated using-source context; imported normalized lazy fields do not
   implicitly select their producers' registrations for the current Module.

This chooses separate role carriers and source slots, not optional constructor
members or a composite registration API. It reuses the existing provider-origin
mechanism and adds no general catalog, placeholder enum or serialized profile.

Actual provider Modules, export keys, revision/schema selections and
requirements are mandatory profile inputs. This proposal assigns none of
their production values. The candidate qualified source name is an ordinary
lookup spelling, **not** a Module identity or export key.

Source lookup uses the current namespace, imports, qualified names,
accessibility and finite transparent alias expansion. Resolve the attribute
to a checked definition, then compare it with U's selected L; do not apply
semantics from its short/qualified name alone. Qualified lookup and aliases
that reach L preserve its identity. Local shadows, ambiguous lookup,
unrelated equal-shaped schemas and aliases to another provider do not.
There is no special `Attribute` suffix removal, built-in synonym,
compatibility alias, namespace bypass or dependency-order tie-break.

A Module needing neither role need not select either registration. A Module
with ordinary eager fields and no authored constructor still uses the
accepted synthesized field-access trigger. If a host/input selects or retains
L's registration requirement, an incomplete binding is an error, not silent
eager fallback. A valid **unregistered** same-spelled ordinary attribute follows
its own schema and does not make its field lazy; its spelling alone does not
create a missing Core requirement. An unresolved name remains a lookup error.

## Downstream facts, persistence and replacement

Source binding retains the resolved L selection and originating U/provider
snapshot together with the field's normalized lazy fact. Shared traversal,
rewriting, reference construction and any continuing source binding preserve
that association; a name or context-free Boolean is not a replacement for a
surviving schema/registration dependency.

Two distinct retained graphs must not be conflated:

| Retained use | Required downstream contract |
|---|---|
| Source registration/schema use | Retain L's logical declaration identity, original expected schema, actual provider correspondence and originating using context. A consumer's different Core map cannot reinterpret it. Republishing/consuming this registration requires an admitted complete attribute-declaration/schema and origin/binding representation. |
| Normalized static-field use | Retain the exact StaticField, closed owner, containing EagerGroup, independent LazyUnit and initializer/role/effect/access/dependency contracts. A source-free field consumer relies on those checked records, not reconstructed annotations or its own LazyInitialization registration. |

The [static persistence candidate](immutable-static-field-persistence.md#static-identity-access-and-ownership)
already provides the normalized path through
`StaticFieldContract.lazyUnitRef`, LazyUnit and the referenced initializer.
Reuse that relationship when its exact combined schema is selected and
supported. Lazy-only owners still have their containing eager group; they
do not skip the external group-completion requirement. This source decision
adds no source-attribute payload to those static rows and changes none of
their candidate IDs, strides, reference sets or role tags.

Elaborating an annotation into a complete normalized static contract need not
serialize source syntax or add an attribute edge to each runtime static record.
It may discharge the lookup-only dependency only when no independently
retained source/schema/registration obligation remains. A route promising
continued registration or source-schema use must preserve L's logical edge;
if its destination cannot represent that edge, refuse the affected route
rather than drop it, serialize authorization or invent a definition token.
Current source registration does not establish source-free Core-schema codecs.

Packing and replacement preserve original using-Module contexts, actual
provider selection and every surviving expectation. Remap actual declaration,
field, owner/substitution, group, lazy, initializer and Import/Export references;
preserve source execution order independently of row order. Shared rows with
different meanings in different using contexts must be cloned/remapped, not
made into a global cache keyed by the attribute name.

Apply [complete directed comparison](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions)
and its [retained graph rule](../specs/representation/binding-and-replacement.md#4-complete-comparison-graphs):

* A retained L registration requires the exact zero-argument, nongeneric,
  public-primary, field-targeted, nonrepeatable, primary-owned schema and the
  same lazy-role mapping. Adding arguments/defaults, widening targets or
  repetition, or mapping L to eager initialization is not strengthening.
  Stable Module/key/name strings do not establish compatibility with the
  actual replacement snapshot.
* A retained static-field expectation requires exact eager/lazy classification,
  owner/group/unit correspondence and all retained initialization, access,
  effect, publication and lifetime obligations. Changing the field to eager,
  replacing its lazy unit with the group's state or changing constructor
  trigger is not strengthening.
* A consumer retaining only normalized field contracts need not acquire a
  source-schema dependency merely because the producer authored an annotation.
  A producer implementation using another authorized Core origin can satisfy
  that consumer only if the complete normalized contract comparison succeeds
  and no surviving expectation still selects the earlier L identity.
* A changed registration profile requires explicit rebinding/revalidation.
  Never relabel an existing bound field with another Module's selection or
  discard the original expectation after direct provider binding or folding.

Consumers perform structural, bounds, reference, support and binding checks;
they do not semantically re-prove producer-authored initialization or attribute
truth. Producer truth remains mandatory under
[consumer verification](../specs/representation/reader-trust.md).

No wire ID, new Feature, attribute object layout, Body operation, runtime unit
handle or ABI is selected here. Host `AttributeSchema` kinds are not TRRM
DefId assignments. Only `.trrm` is normative interchange; a private persistence
roundtrip does not supply a missing standard codec. Any affected binary route
selects an exact whole schema under
[draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts),
not matching version bytes, plausible rows or Feature presence.

## Alternatives and exact integration owners

| Alternative | Assessment |
|---|---|
| Recommended: separate `LazyInitializationCoreRegistrationSet` and options/reference slots, sharing checked `CoreProviderModuleBindings` | Smallest complete integration without changing the existing required constructor closure or its nonnullable members. L-only selection is expressible, duplicate supply is checked per role, and mixed options/reference roles compose only through the existing shared snapshot. |
| Minimal alternative: one composite registration with optional constructor and L closures | Coherent only if A/E/F/M are all absent or form one complete validated constructor closure, L is independently absent or complete, and an empty composite does not create a role. It keeps one top-level slot but changes the current factory/member invariants and must define per-role options/reference conflict checks to allow disjoint selections. This broader carrier/selection rewrite is not recommended; simply appending optional L to the current required factory does not solve independence. |
| Fixed universal Core Module/export keys or a serialized universal semantic-role catalog | A substantive alternative if uniform source-free discovery is desired. It requires authority/discovery, complete declaration/schema records, admission and replacement decisions. It is not necessary for this source binding and is not silently approximated by a guessed provider or host role byte. |

The minimal choice is recommended for its semantic scope, not draft-compatibility
obligations. A later catalog redesign is possible; this creates no legacy
alias, fallback or frozen unpublished allocation.

The following are coordinated edits **only after adoption**, not changes made
by this document:

| Owner | Exact proposed integration |
|---|---|
| [Static grammar](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage) and ordinary attribute rules | Add L's candidate source catalog and per-U registration/normalization contract. Clarify exact field target selection and the field's own primary. Preserve existing arity, eligibility and all execution rules rather than restating them as new policy. |
| [Adopted inventory](../specs/libraries/adopted-inventory.md) / LCI-37 | Record L's completed source registration separately from its unassigned physical schema and recovery/API gates. Core remains an authority, not a fixed package or inferred export catalog. |
| [Logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending) | Replace only LazyInitialization's source-catalog deferral with the adopted source registration. Keep its physical attribute identities/codecs and all independent runtime deferrals explicit; add no runtime attribute edges or row assignments. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md) | Record exact retained L-schema/mapping checks and originating context; distinguish them from normalized eager/lazy field comparison and producer truth. |
| [Static conformance](../specs/conformance/source-static-initialization.md) | Extend identity/placement/persistence cases below without altering SIID, SIEG, SISTA, SILIFE or SIREP outcomes. |
| [Trigger proposal](static-initializer-trigger-identities.md) and [Core reference](core-library-reference.md#9-integration-owners-and-remaining-decisions) | Close only their open LazyInitialization source-registration selection when adopted. Compose with, not supersede, the separate A/E/F/M recommendation; keep physical identifiers and recovery callable open. |

The [registry](../specs/representation/registry.md) requires no allocation for
this source decision. The immutable static persistence proposal remains a
separate unapproved physical-contract owner.

## Proposed conformance and integration cases

These are required future cases, **not tests run or support claims**. Use
unrelated synthetic providers and explicit selections; assume other required
contracts are valid unless their failure is the case.

| Case | Required outcome |
|---|---|
| Direct short/qualified L, with omitted or empty argument parentheses; explicit `field:` | Accept after ordinary lookup reaches exact L and the field is eligible. Select one independent lazy unit. |
| Exact enclosing `field(Name):` selector, finite transparent alias chain or public export alias | Preserve the same field/schema identities, access and primary ownership. No name-based role or new nominal identity. |
| Direct and selector/alias placements target the same field twice | Reject nonrepeatability even when both payloads are empty. |
| Positional/named/enum/type/runtime argument, defaulted slot, generic/repeatable/secondary schema | Reject the application or invalid registration; zero arguments cannot configure another policy. |
| Instance/`extern` field, associated/interface constant, property/backing, accessor, type or imported expectation | Reject L's ineligible target; create no Terrum lazy backing, foreign wrapper or consumer-local provider field. |
| Selected provider/key has wrong kind, nonpublic/nonprimary schema, absent snapshot or changed target/arity | Reject before publishing L binding. Do not search another provider/key or reinterpret StaticInitializer's schema. |
| No L selection and a valid same-spelled unrelated attribute; unresolved or locally shadowed name | Unregistered attribute supplies no laziness. Apply ordinary lookup/schema results; never bypass a shadow or invent role authority. |
| Ordinary eager source without L; lazy-only type without authored constructor | No unnecessary constructor/enum registration. Preserve synthesized eager-group trigger; lazy-only storage still has its containing group. |
| Valid L supplied only through its options slot or only through its reference slot; constructor slot null | Select L with its exact U/schema/provider snapshot, bind eligible lazy fields and leave A/E/F/M unselected. Construct no dummy constructor registration. |
| L supplied in both options and reference, including the identical object | Reject duplicate L supply independently of the constructor selection; neither precedence nor equality-based merging applies. |
| L in options and constructor registration in reference, or the reverse, with one shared checked snapshot | Admit both independently validated closures without a duplicate role. Missing constructor selection remains valid for L-only source. |
| Selected L and another Core role have different snapshot objects or using Modules, despite equal source/provider names | Reject composition before binding. Require explicit checked host rebinding to one snapshot; do not force a constructor or discard the conflicting role. |
| New lazy field in a secondary partial type part; duplicate declaration or attempted change to another primary | Permit its own complete primary declaration; reject conflicting authorship/duplicate members. Preserve merged order for remaining eager initializers. |
| Fixed/`mut`/caller-`unsafe` fields and admissible managed, ownership-bearing or generic field types | Keep ordinary eligibility and every independent access/type/cleanup/sharing obligation. No scalar-only or immutable-backing restriction is added. |
| Zero-data field; invalid/missing initialization or effect/access/completion contract | Preserve logical initialization and demand despite zero payload; reject invalid initialization rather than publish a default or suppress effects. |
| Two lazy fields, either authored constructor trigger, generic constructions and independent reloads | Group completion precedes the requested lazy unit; leave unaccessed siblings alone and retain distinct closed-owner/load states. Do not change constructor policy. |
| Competing permitted demands, caught/escaping cycle, lazy failure after/during group initialization, exclusive retry/unload | Preserve the existing SISTA/SILIFE state, event, cleanup, retry and lifetime outcomes. Registration introduces no new runtime policy. |
| U1/U2 select different authorized L definitions with identical names/shapes | Bind independently, retaining each origin and expectation. One Module's registry cannot normalize or replace another's retained selection. |
| L and StaticInitializer/enum roles use different providers; same provider selected at conflicting revisions | Accept coherent independent role closures; reject conflicting provider selection. No provider co-location or name coincidence supplies authority. |
| Registration replacement reorders rows or preserves a selected export alias and exact schema/mapping | Accept supported remapping/comparison while retaining original expectations. Physical indexes are not identity. |
| Replacement changes L's schema/mapping or loses an expected binding with stable Module/key/name | Reject affected compatibility; no exact-name success, alternate lookup or retrospective re-registration. |
| Static provider changes lazy to eager, loses its unit, changes group/trigger/effects or relied-on publication | Fail the complete retained static comparison, even if the observed scalar value matches. |
| Source-free consumer retains normalized LazyUnit records but has no L source registration, or selects a different L | Consume only with complete supported static/provider contracts; do not reconstruct annotations, create schema dependencies unnecessarily or relabel the field. |
| Source-free registration/schema use lacks the complete L declaration/schema/origin representation | Refuse that route, even if normalized static tables are supported. Private persistence or a host kind number supplies no standard identity codec. |
| Missing/dangling/wrong-owner group/lazy/initializer edges; unsupported static/runtime contract | Reject before affected use/execution. L binding alone is not persistence, emission or execution support. |
| Packing/trimming/folding retains a role/schema expectation or context-sensitive field dependency | Remap and preserve it, including original provider facts; decline an unrepresentable destination rather than drop the obligation or choose a historical decoder. |
| Fixed/pure/immutable field, equal header versions or recognized candidate static rows alone | Infer neither laziness, discharged initialization nor a complete codec/runtime capability. |

## Current evidence and remaining choices

The inspected [host registration](../../sources/Terrum.CodeAnalysis/Semantics/StaticInitializationCoreRegistration.cs)
models StaticInitializer plus its trigger enum/cases, not L; its factory
requires that complete constructor closure. Current options/references reject
duplicate supply per registration kind and require selected Core registrations
to share one checked provider snapshot. The separate L carrier/slots and
independent selection specified above extend, rather than silently replace,
those conventions.
The [shared declaration snapshot](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs)
contains the existing native/layout/static-constructor schemas, not a
LazyInitialization schema. The active
[static registration fixture](../../tests/Terrum.CodeAnalysis.Tests/Semantics/StaticInitializationCoreRegistrationTests.cs)
exercises provider snapshots, exact trigger selection, aliases and partial
constructor ownership; it does not establish lazy registration or execution.
[Implementation support](../implementation.md) and
[CG-008](../implementation/conformance-gaps.md#cg-008) independently retain
lazy/source production, persistence, replacement and runtime integration gates.
Those observations are implementation gaps, not amendments to accepted behavior.

Under this recommendation, no additional L source-schema, target, arity,
repeatability, primary-ownership, role-mapping, host carrier or
options/reference selection choice is left to invent.
The remaining requirements are precise:

1. **Approval and real provider inputs:** future normative review remains
   required. The authorized host must supply actual Module/key/revision/schema
   selections and checked declarations; this document supplies no production
   default, fixture identity or implicit Core registration.
2. **Independent schema persistence/delivery:** source-free attribute
   declaration/schema and registration-origin codecs, and any new CLI
   configuration delivery, require their own complete selected contract if
   needed. No unassigned ID, manifest, private revision or compatibility reader
   is chosen here.
3. **Static/runtime completion:** the existing static persistence candidate
   needs its own adoption/support; StaticPlace/recovery carriers, the exact
   recovery callable/signature/unit operand, runtime state/handle ABI and
   independently required ownership/sharing/failure/cleanup support remain
   with their current owners. No recovery or scheduling decision is reopened.
4. **Qualification:** binding, shared fact preservation, source-free routes,
   provider replacement, negative cases and eventual supervised native
   execution require final-input evidence separately. A document consistency
   check is not compiler conformance.

This document changes no normative specification, registry, compiler, test,
implementation inventory or other proposal.
