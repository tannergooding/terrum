# Static-initializer trigger identities

**Status: unapproved; explicitly provisional implementation direction.**
The authorized missing-contract workflow permits proceeding with this
recommendation provisionally. This document does not approve a specification
amendment, allocate metadata, or establish implementation support.

## Finding and bounded decision

**Exact source registration is a genuine missing contract for authored static
constructors. Recommend one Core-authorized registration per using logical
Module, identifying the attribute schema, its dedicated enum and both enum
member definitions.** Keep the two accepted trigger behaviors unchanged.

The [grammar's static-initialization owner](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage)
already requires one explicit positional compile-time enum argument and exact
registered attribute, enum and case identities; names or underlying bits cannot
select policy. It leaves their source catalog details unspecified.
[Logical records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending)
expressly leave catalog/metadata identifiers unassigned.
[LCI-37](../specs/libraries/adopted-inventory.md#2-selected-declarations-and-compiler-roles)
is a family, not a complete registration.

This is distinct from missing compiler support:
[Binder.Declare](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L1764)
currently rejects a constructor with `static`
([modifier check](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L1977))
and has no StaticInitializer admission in its
[callable attribute handling](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L1917).
Removing those rejections would not supply the required identity authority.
The [implementation inventory](../implementation.md) and
[CG-008](../implementation/conformance-gaps.md#cg-008) independently retain
static representation/runtime gates.

No existing proposal supplies this registration.
The [Core-library reference's static-initialization row](core-library-reference.md#9-integration-owners-and-remaining-decisions)
preserves the exact-identity requirement but leaves its catalog open.
[Constructor no-return applicability](constructor-no-return-applicability.md)
requires a valid explicit static trigger without selecting its identities.
The [text-profile identity proposal](text-construction-profile-identities.md)
provides a compatible out-of-band identity-context approach, not these roles.
This proposal fills only the StaticInitializer registration gap; it does not
select LazyInitialization or the recovery callable.

## Proposed source catalog and schema

These are **candidate catalog declarations**, not existing exports or runnable
library definitions. Core remains the
[designated registration authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
not a fixed package identity.

| Role | Exact declaration contract |
|---|---|
| A: StaticInitializer | Public nongeneric primary semantic attribute-schema declaration; proposed public spelling `Terrum.StaticInitializer`. Its complete schema is below. No runtime attribute object, base class or constructor protocol is introduced. |
| E: trigger enum | Public nongeneric integer-backed enum; proposed public spelling `Terrum.StaticInitializationTrigger`, with exact `int32` base and exactly the two declared members below. No Flags contract or additional/duplicate-valued members. |
| F: field-access case | Exact enum member definition owned by E; proposed spelling `GroupedStaticFieldAccess`, ordinary enum constant `0`. |
| M: member-access case | A different exact enum member definition owned by E; proposed spelling `RuntimeStaticMemberAccess`, ordinary enum constant `1`. |

The closure is a requirement of this registered selector schema, not a new
closed-enum grammar or an application of the union-only ClosedCases attribute.
The constants `0` and `1` are proposed public enum values, **not** metadata
identifiers or encoded trigger tags. Neither number grants initialization
semantics.

The exact schema for A is:

| Axis | Required contract |
|---|---|
| Target | The primary authored static-constructor declaration itself. Untargeted placement and explicit `method:` placement on that declaration denote the same target. No named member selector or placement on its enclosing type, instance constructor, ordinary method, field, accessor, result, formal, Module or assembly. |
| Multiplicity | Exactly one on the composed constructor's primary, including a nonpartial constructor. Nonrepeatable even with the same case or through aliases. A partial secondary carries none; duplicating the primary's policy is invalid even when identical. |
| Parameter | One required positional-only schema parameter `Trigger` of exact E type. No default, named argument, additional argument, variadic form or runtime evaluation. |
| Argument eligibility | One registered enum-member reference, resolved to F or M, also valid under ordinary compile-time constant checking. Parentheses and transparent type aliases/ordinary qualification may preserve that reference. No computed-value substitute. |
| Semantic result | The exact member identity selects one logical trigger below. The annotation is not an ordinary runtime call and grants no initialization-phase authority by itself. |

The proposed short-form attribute fragments, assuming ordinary lookup of the
registered declarations, are
`[StaticInitializer(StaticInitializationTrigger.GroupedStaticFieldAccess)]`
and
`[StaticInitializer(StaticInitializationTrigger.RuntimeStaticMemberAccess)]`.
They illustrate candidate catalog spellings, not executable support.

**Case identity must be resolved before constant evaluation erases declaration
origin.** Bind the argument, resolve transparent aliases and grouping, require
an enum-member reference owned by exact E, then compare that declaration with
F and M. Perform ordinary type/constant validity checks as well; an identity
match does not waive them. Reject runtime values, numeric construction or
conversion, bitwise/arithmetic expressions, and value-constant forwarding in
place of that member reference, even if their E-typed result has bits `0` or
`1`. An alias of E used to qualify its actual member is not such forwarding.

This is an explicit, schema-local restriction of the existing “compile-time
value” wording, not a change to
[ordinary enum value identity](../specs/language/constant-generics.md#initial-domains).
Ordinary enums identify values by nominal type and integer value and permit
duplicate-valued members; they do not generally retain member provenance.
Requiring a declaration reference here satisfies the already accepted
case-identity rule without adding provenance to every enum constant or
recovering a case from bits. It changes neither trigger's behavior.

### Exact normalization

| Resolved declaration | Normalized logical trigger | Preserved source obligation |
|---|---|---|
| F | `GroupedStaticFieldAccess` | Access to a grouped static field demands its group; unrelated static-member access does not itself choose the other policy. |
| M | `RuntimeStaticMemberAccess` | Runtime static-member access demands the group under the accepted policy. |

These are symbolic semantic alternatives, not assigned byte values. There is
no third case, combination, numeric ordering or “stronger trigger” relation.
Type naming, metadata inspection and compile-time binding remain nontriggers.
No authored constructor still means the accepted synthesized field-access
trigger; an authored constructor missing A remains invalid. Lazy access,
initialization-phase authority, ordering, completion, effects and failure
continue to follow the existing static-initialization owner.

## Identity authority, binding and replacement

The embedding compiler/linker supplies an immutable registration for each
**using logical Module U** before affected source binding. It contains exact
resolved declaration handles A, E, F and M and the schema/mapping above.
F and M must be distinct members of E, and A's parameter must refer to that
same E. A coherent authorized selection may span provider Modules; namespace
coincidence or dependency order cannot establish it.

Provider selection uses exact logical Module identity, public export key and
required declaration kind, or already resolved handles in the same checked
binding context. Concrete provider identities, keys and the unambiguous
selected revisions are mandatory profile inputs, not global constants to
guess. Apply the existing ordinal decoded-text identity and
[identity-before-contract rules](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities).
An ordinary package cannot self-register these roles merely by referencing
Core. Source imports and transparent aliases perform ordinary lookup only;
they cannot manufacture role authority.

Check the selected definitions' public access, primary ownership, kinds,
enum base/member closure/values and complete attribute schema before admitting
A. Reject an absent, partial, conflicting or unsupported registration for
affected use; unrelated source need not acquire this dependency. A same-named
unregistered attribute follows its own schema but cannot satisfy the required
annotation. Wrong-kind, missing, ambiguous or incompatible provider bindings
do not trigger a search for another provider, overload or same-shaped type.

Retain U's registration selection and any surviving role references in their
originating context; another Module cannot reinterpret them with its own Core
map. A normalized constructor/group contract preserves its selected trigger.
This does not require serializing source annotations or adding attribute edges
to static records. Packing preserves logical Modules and remaps actual
references, not case-name text or coincident integers. Export aliases resolving
to one definition preserve identity; expected declarations do not bypass their
Imports.

Replacement rechecks the actual selected providers against the complete
retained graph under
[binding and replacement §§2–5](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions).
For retained registration dependencies, preserve A's complete schema, exact E
and case-owner correspondence, the closed member/value contract, and **each
case-to-trigger mapping exactly**. Normalized group dependencies preserve the
trigger itself. Switching trigger, swapping case roles, adding an enum case or
relaxing lookup into name/bit matching is not contract strengthening. Unchanged
Module/key strings or previous validation do not establish compatibility.
A changed profile requires explicit rebinding/revalidation, never retrospective
relabeling of an existing constructor.

This is logical addressing, not a claim that current TRRM can publish these
definitions. Source-free Core registration lookup additionally needs admitted
declaration/schema codecs and binding support. Missing codecs decline that
route, not fabricate definition tokens. Consumers of admitted normalized static
contracts need not reconstruct source annotations or re-prove producer body
facts; required structural, support and binding checks remain mandatory.

## Allocation boundary and alternatives

**No new numeric allocation is needed for this source-registration decision.**
The [registry](../specs/representation/registry.md#4-admission-status-and-namespace-boundaries)
assigns separate section, DefId, type-token, clause and Body namespaces.
Current DefId kinds are TypeDef `0x13`, FieldDef `0x14`, MethodDef `0x16`,
GenericParam `0x19`, PropertyDef `0x1E` and UnionCaseDef `0x21`; the latter is
not an enum-member identity. Current TypeDef kind bits admit
struct/class/interface, not a complete enum/schema representation. Thus neither
a TypeDef reference alone nor borrowing FieldDef/UnionCaseDef publishes this
registration.
No free byte, pending constant/static definition kind, optional section,
well-known type code or inventory row is appropriated here. There are no
candidate wire IDs to collide with existing assignments.

| Alternative | Assessment |
|---|---|
| Recommended: per-using-Module Core registration with exact semantic handles | Reuses existing authority and provider binding. Supplies the source decision without changing static records or inventing a package identity. Independent interchange remains honestly gated. |
| Fixed universal Core Module/export keys or a serialized role catalog | Could make identity discovery uniform, but requires new package/authority or catalog fields, kinds and admission rules. It is a broader integration decision, not necessary for source recognition. |
| Recover a case from enum values, or add global enum provenance | Value recovery weakens the accepted case-identity requirement; global provenance changes ordinary constant semantics. The bounded member-reference argument avoids both. |

A future serialized catalog may carry this same logical mapping once its exact
schema is reviewed. It must not become a parallel provider resolver.
[Draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
still covers exact semantics as well as allocation; equal `(1, 0)` bytes do
not admit an improvised enum/schema codec.

## Exact integration owners

These are proposed normative edits **after adoption**, not edits made here.

| Owner | Text to amend or add |
|---|---|
| [Grammar §3.1, static initialization](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage) | Replace “Their catalog/metadata identifiers and source case spellings remain catalog details” with the candidate source catalog/schema and exact normalization above. Explicitly add: “The positional argument must resolve to one registered enum-member declaration through ordinary qualification, transparent type aliases and grouping; equal evaluated enum bits alone do not identify a case.” Keep metadata IDs unassigned, both trigger choices and all existing execution rules unchanged. |
| [Adopted inventory §§1, 2 and 4](../specs/libraries/adopted-inventory.md) | Apply the per-using-Module authorized A/E/F/M registration and complete provider checks. Keep LCI-37's incomplete-family boundary, distinguishing this source registration from unassigned LazyInitialization/recovery identities and physical codecs. Reference the grammar rather than duplicating its schema. |
| [Logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending) | Replace only the combined catalog deferral with: “StaticInitializer source registration and case normalization follow the source catalog; its physical declaration/schema identifiers remain unassigned. LazyInitialization catalog/metadata identifiers remain unassigned.” Do not amend the logical record families, candidate shapes, role tags or runtime model. |
| [Binding and replacement §§2–5](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | Add the exact schema/enum/case-owner/mapping checks for retained registration dependencies, originating-Module obligations and exact normalized-trigger comparison above. Do not require source-attribute reconstruction. Trigger changes have no strengthening relation. |
| [Static conformance, SIID-09–16](../specs/conformance/source-static-initialization.md#declaration-identity-and-ordering) | Add the registration, selector-expression, collision and replacement cases below; preserve SIEG/SISTA/SILIFE/SIREP behavior. |

The [metadata limitations](../specs/il-metadata.md#unspecified-schema-details)
and registry require no assignment change for this decision. On adoption this
supersedes only the StaticInitializer source-catalog deferral, not pending
physical codecs or any independent proposal.

## Proposed conformance cases

These are acceptance requirements, not reports of tests run.

| Case | Required outcome |
|---|---|
| Each exact case on a valid nonpartial primary, or a partial primary with one implementing secondary | Normalize to the corresponding symbolic trigger; retain primary policy ownership. Test both direct and `method:` placement. |
| Parenthesized case; transparent enum-type alias; qualified/ordinary imported lookup | Accept when lookup resolves the same E/member definitions; spelling is not authority. |
| Missing/empty/defaulted/multiple/named/runtime argument; wrong target or secondary placement; repeated A through different aliases | Reject; identical arguments do not waive multiplicity or primary ownership. |
| Numeric reconstruction of E, arithmetic/bitwise result or forwarded constant equal to a case | Reject as a selector: ordinary constant equality is not member-definition identity. |
| Same-named attribute or enum from another provider; unrelated enum with matching values | No role authority; the static primary still lacks a valid annotation or its argument has the wrong identity. |
| Registration maps both cases to one member, uses a member of another enum, adds an enum member, or changes A's schema | Reject the incoherent registration, not merely the later numeric value. |
| Two using Modules select different authorized Core providers; packed rows are permuted | Validate separately and preserve originating selections, expectations and case mappings; no global name/row cache. |
| Provider uses an export alias or replacement reorders rows while preserving exact selected contracts | Preserve identity through supported resolution/remapping. Re-exporting an imported expectation is not an alias mechanism. |
| Replacement swaps F/M policy, changes enum values/schema or loses a retained provider contract despite stable Module/key | Reject affected compatibility; neither trigger is a monotone strengthening of the other. |
| Authored static constructor also has a truthful DoesNotReturn promise | A valid explicit trigger remains required. The separate constructor-applicability proposal governs that promise; do not copy it to unrelated members. |
| Registration succeeds but a required metadata/static/runtime/target capability is missing | Decline affected independent use or execution. Source recognition is not storage, emission or runtime evidence. |

## Implementation boundary and remaining gates

The bounded next source step can resolve/validate the registered schema and
normalize the primary declaration's trigger. It must preserve that fact through
any supported shared representation rather than dropping it on a successful
path. This is an identity input, **not** a new StaticInitProfile or permission
to claim a reduced conforming static-initialization feature.

Unchanged gates include enum/schema declaration publication and case identities;
constructor MethodSig/role representation; static sections/DefId/clause and
Feature registration; exact group/access/dependency references and remapping;
the pending Body/runtime carriers; and initialization state, sharing, failure,
cleanup, recovery, unload and native execution. The
[remaining IL proposal](il-extensions.md) and CG-008 keep their separate scope.
No runtime scheduling, state-cell ABI, eager/lazy codec or native support is
selected here. Constructor no-return applicability remains independently
unapproved and supplies none of those missing contracts.

Under this recommendation no additional source-registration choice is left
open: the catalog, schema, case resolution, authority and mapping are concrete.
Approval, actual profile-supplied provider bindings, implementation and
qualification remain separate requirements. Physical identities/codecs and
runtime integration are deliberately unresolved prerequisites, not implicit
provisional assignments.
