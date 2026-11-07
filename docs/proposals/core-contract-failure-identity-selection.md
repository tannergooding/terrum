# Core contract-failure identity selection

> [!IMPORTANT]
> **Unapproved proposal; provisional direction under the explicit
> missing-contract authorization.** This document does not amend accepted
> specifications, approve another proposal, implement a compiler feature or
> establish runtime support.

## Decision and scope

Recommend a narrowly separate, nonserialized
**`CoreContractFailureIdentitySelection` per using logical Module**, selecting
three exact Core declarations through explicit provider Module identities and
export keys:

- The `Terrum.RequirementException` managed class.
- The `Terrum.PostconditionException` managed class.
- The resolved `RequireException` semantic attribute schema.

Use ordinary checked declaration binding after authority selects these
anchors. Feed the selected complete class directly into ordinary
zero-supplied-argument constructor binding; select no constructor key, factory
delegate, failure-factory interface or implicit diagnostic arguments in this
host record. An explicit `require<E>` still selects its ordinarily resolved E,
not a Core role discovered by examining E.

**One narrow semantic amendment is also proposed:** declaration runtime
`ensure` constructs its selected PostconditionException through the same
ordinary zero-supplied-argument binding rules owned by
[requirement failures §2](../specs/language/requirement-failures.md#2-ordinary-zero-supplied-argument-construction).
The [runtime owner §1](../specs/runtime/standard-failures.md#1-exact-core-identities-and-construction)
currently selects common creation paths without guaranteeing a constructor
binding for this class. This candidate supersedes that creation-path selection
**only for normally false declaration ensure**, and adds the corresponding
ordinary constructor availability obligation to PostconditionException.
It does not require a literal zero-parameter signature or change the other
runtime failure categories. Its selected ordinary construction must honor
the common default-creation obligations, including the type-owned
DefaultMessage, through declared constructor/default rules, not injected
arguments. The complete common exception contract remains required; its
factories are not the selected invocation for these checks.

The existing
[Core exception identity proposal](standard-trrm-core-exception-identity.md)
remains limited to E1's IException nominal anchor, its derived existential
view and UnreachableException payload. This document neither extends that
bounded decision nor changes
[E1's region schema, Feature or provider-schema restriction](standard-trrm-register-body-exception-regions.md).
Reuse its already checked IException anchor where exception eligibility needs
it; do not select another interface or change its Unreachable obligations.
Supplying either host record does not select E1 or a composite artifact schema.

No source production, physical metadata ID, Feature ID, opcode, well-known
type code, private alias, fallback encoding or production Core package/key is
selected here.

## Owners and current gap

The [specification index](../specs/README.md) reaches these distinct owners:

| Owner | Preserved rule and integration boundary |
|---|---|
| [Requirement failures](../specs/language/requirement-failures.md) | Explicit E, then the originating callable's resolved default recipe, then exact Core RequirementException. Ordinary zero-supplied construction, schema ownership, substitution and actual construction failure already belong here. |
| [Standard runtime failures §2](../specs/runtime/standard-failures.md#2-runtime-postconditions) | Normally false declaration ensure selects exact PostconditionException. Predicate/helper/cleanup failures remain themselves. The constructor-path refinement above is an explicit candidate amendment, not an already accepted guarantee. |
| [Call phases](../specs/language-contracts.md#3-contracts-bracket-semantic-body-completion), [grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | Separate grouped pre/post lexical scopes, declaration order, once-only evaluation, saved results, cleanup and failure/unwind. [Static constructors](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage) use the same ordinary runtime clauses. |
| [Clause conformance ICOW-16..18](../specs/conformance/il-contract-clauses.md) | MethodDef-owned Contract kind `0x000B`, phase tags and contiguous phase-local ordinals, and the 20-byte PredicateGraph attachment are assigned. Complete Feature registration/reference integration remains a separate gate; this proposal supplies neither a replacement attachment nor failure operands in that attachment. |
| [Library authority](../specs/libraries/adopted-inventory.md#1-status-and-identity), [local exceptions](../specs/language/local-failures.md#1-the-common-exception-contract) | Core is a designated authority, not a namespace or fixed binary. Exact class/self-interface evidence and full selected source identities remain required; an ordinary referencing package cannot grant Core roles. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding/replacement](../specs/representation/binding-and-replacement.md) | Resolve exact Module/key/kind before directed comparison, in each original using/owner context. Retain all expectations, aliases, selected members and stronger relied-on facts. |
| [Logical failure/default records](../specs/representation/logical-records.md#runtime-requirement-construction-and-defaults-encoding-pending), [instruction construction plans](../specs/il-instruction-set.md#runtime-require-instruction) | Identity selection does not complete schema/default/phase/constructor publication or executable failure support. Existing typed construction-plan shapes do not authorize missing codecs or postcondition lowering. |
| [Reader trust](../specs/representation/reader-trust.md), [admission](../specs/representation/requirements-and-admission.md), [format selection](../specs/representation/format-versioning.md) | Consumers check structure, references, supported exact schemas and binding before affected use. Producers establish semantic truth; consumers need not re-prove arbitrary provider Bodies. |

Direct inspection establishes an implementation gap, not a defect in the
accepted declaration-clause grammar:

| Evidence | Present boundary |
|---|---|
| [`CoreExceptionIdentitySelection.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CoreExceptionIdentitySelection.cs), [its tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/CoreExceptionIdentitySelectionTests.cs) | The host record carries only IException and Unreachable provider/key pairs. The tests cover transport and selection-table validation, not these missing roles or declaration enforcement. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) | Existing selection transport and normalized interface/accessor ContractClause text are not typed declaration predicate binding. RequireException identity/schema/default handling and exact default failure-class/constructor binding are missing. |
| [`TrRmPredicateGraphTables.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmPredicateGraphTables.cs), [`TrRmGeneralPredicateTables.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGeneralPredicateTables.cs), [graph proposal](general-predicate-graph-schema.md#phase-execution-and-failure) | Draft graph codecs exist. Their source/Feature integration and executable failure enforcement are separate from this selection decision. A Boolean graph is not a constructor plan. |
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | Its native enum/schema and separate layout/initialization registrations are different mechanisms. Their kind values, matching names and checked native/layout roles cannot designate these classes or RequireException. |

The [support inventory](../implementation.md) remains the implementation
owner. No existing test result is claimed as validation of this candidate.

## Selection carrier and authority

The candidate host record has this logical shape. The selector pairs are
host binding inputs, **not new serialized Import/Export layouts**.

| Field | Selected declaration |
|---|---|
| `UsingModuleIdentity` | Exact logical Module whose authored or reconstructed semantics use the selection. |
| `RequirementExceptionClass`, optional | Exact `(producerModuleIdentity, exportKey)` pair selecting the public primary nongeneric RequirementException TypeDef of managed class kind. |
| `PostconditionExceptionClass`, optional | The equivalent exact pair for the distinct PostconditionException managed class TypeDef. |
| `RequireExceptionSchema`, optional | Exact provider Module/key pair selecting the public primary Core semantic schema declaration, **not an exception class or attribute constructor**. Its required declaration family is semantic AttributeSchema; no physical DefId kind is implied. |

Require at most one immutable record per using Module, with at least one
selected role. An absent role is legal only when that Module retains no
dependency on it: RequirementException for a fallback check, PostconditionException
for a runtime ensure, and RequireException for a registered/published schema
or callable default using that semantic schema. Explicit-only requirements
need no fallback-class selection merely because they implement the exception
interface; their independent exact eligibility evidence is still required.
Retained defaults and proof dependencies count even when enforcement is
relocated or a failed path is eliminated.

Reject duplicate records, partial selector pairs, conflicting option/reference
selections and malformed supplied selections. Every supplied role must be
valid even if unused. Missing required selection declines affected use; it
does not erase a published default or downgrade a required compiler-semantic
schema to an inert attribute.

Identities and keys are nonempty well-formed scalar text without embedded
zero, compared ordinally without trimming, case folding or normalization.
They are not filenames, namespace-derived keys or dependency search requests.
Changing physical rows or String encodings does not change a decoded identity.

The designated Core authority/profile supplies the selectors and the
unambiguous actual provider snapshots. Each snapshot retains its logical
Module, selected revision, applicable source contract/catalog or explicit
artifact schema, governing edition, requirements and target/runtime context,
plus the actual declaration and original expectations. The three roles may
be supplied by different authorized Modules. A package cannot self-authorize
a role by exporting a plausible declaration, implementing IException or
referencing an authorized provider. Authentication remains the environment's
obligation; the record authenticates no bytes.

An authorized source Core build may select its own public primary declarations
through an explicit checked local export map. An external selection must
resolve the supplied key in the selected provider, not search source names.
An imported expectation is not a provider-local declaration. Source spelling
is checked where the accepted class contract requires
`Terrum.RequirementException` or `Terrum.PostconditionException`; that check
occurs **after selection** and cannot select a provider. The schema's short
role name introduces no guessed fully qualified attribute-class identity.

For source-independent consumption, the originating Module's selection is
required accompanying binding context, not optional debug information or a
serialized grant of Core authority. Carry or reconstruct the same selection
from explicit profile inputs; do not substitute the consumer's current Core
defaults. An incompatible host/reference context rejects rather than
overriding a retained selection.

### Extend the existing record or separate it?

| Candidate | Assessment |
|---|---|
| **Separate companion record — recommended** | Three role slots supply exactly the missing decision. Join with existing checked exception-interface context by exact using Module. E1-only users retain their current pair; schema/default consumers do not acquire a new Unreachable or E1-schema requirement from this carrier. |
| Add optional roles to `CoreExceptionIdentitySelection` | A viable API implementation choice, but couples the E1 pair's requiredness and transport to source defaults and a semantic schema with different publication gates. If chosen later, preserve the distinct role validation/requiredness above; it must not silently broaden the E1 proposal. |
| Expand native/layout registration or introduce a universal Core registry | Requires new class/schema descriptors and the same authority/binding checks, without a demonstrated need for a larger catalog. Reusable origin and validation helpers are suitable; native/layout admission is not. |
| Select factories, recognize names/shapes or try constructor candidates across classes | Rejected. Availability checks validate a selected declaration; they do not select Core identity or authorize substitution. |

## Source binding and ordinary construction

1. Resolve each required anchor in its exact using-Module/provider context.
   Check public primary ownership and the declared role contract. For either
   selected class, check its exact self-typed witness to the already selected
   Core IException anchor with that complete class as self argument.
   An unrelated interface witness or implementing class cannot establish a
   fallback role. Keep the two class identities distinct.
2. Bind written attributes through ordinary resolved lookup. Only identity
   equality with the selected RequireException schema supplies this default.
   Validate its declared schema against the accepted mapping: one required
   direct `typeof(E)` argument named `ExceptionType`, ordinary positional/named
   mapping, nonrepeatability and primary-callable/accessor ownership.
   No extra/defaulted argument, computed type object, changed target set or
   secondary-contract permission can redefine that mapping.
3. Bind the recipe in the primary declaration's environment. Preserve exact
   enclosing/own type and constant formal identities, finite alias
   normalization and ordinary forward substitution. Each admitted application
   must establish a complete concrete eligible class. The annotation itself
   neither runs construction nor proves a constructor exists.
4. For each runtime requirement, choose ordinarily resolved explicit E first;
   otherwise the semantic owning callable's substituted recipe; otherwise
   its using Module's selected RequirementException class. For each declaration
   runtime ensure, choose that originating Module's PostconditionException
   class independently of RequireException. Bind the predicate and phase
   separately from this failed-path selection.
5. Resolve the ordinary constructor for the selected complete class with zero
   **supplied** arguments in the check's original binding/access environment.
   Retain the exact selected callable/owner, substitutions, optional-default
   or admitted empty-params records, modes, effects, safety, initialization and
   cleanup obligations. Missing, inaccessible, ambiguous or unsupported
   binding rejects; no retry with another class or common factory occurs.

Type aliases preserve their complete normalized class identity and
qualifications. Thus an explicitly written `require<RequirementException>`
can select a same-spelled eligible user class under ordinary lookup; it is
not implicitly redirected to Core. Conversely, an unqualified fallback never
looks up that spelling. Ordinary qualified/imported schema references, or
already legal alias views, have default meaning only when they resolve to the
same exact schema. This proposal adds no attribute-alias syntax.

Check every written default even when a check has an explicit E. Getter-
targeted and inline placements share the same effective occurrence key; a
whole-property, type, module, parameter, result or secondary-part placement
cannot supply the callable default. Preserve the
[owner's full target/partial/generated rules](../specs/language/requirement-failures.md#3-callable-owned-defaults-and-the-core-exception-class)
rather than copying an annotation to generated declarations.

Grouped clause scopes and ordering are unchanged. Execute the already selected
construction binding only after a reached predicate normally completes false,
once; success constructs nothing.
Actual predicate or construction/initializer/constructor-clause/cleanup
failure propagates itself, not a fabricated outer failure. The selected
constructor's own authored checks use their own semantic owner and Module
selection, not an ambient outer default.

Inherited interface clauses, outlining, inlining, generated producer code
and suspension preserve the originating clause/default, phase and failure
channel. Static constructors use the same selection rules after their grouped
initializers; their separate initialization policy is not a failure factory.
Static require/static ensure remain compile-time proof requests; failure is a
diagnostic, with no runtime failure selector. Existing cleanup, saved-result,
output-state and effect rules remain with the owners above; this selection
grants no unsafe, Pure or recoverable-publication capability.

## Module binding and consumer checks

External class anchors use ordinary ModuleRef and TypeDef Import/Export
relationships: the using Module owns a bodyless expected declaration, the
exact provider/key selects a public provider-local class, and its constructor
members retain their ordinary checked callable ownership/binding. An authorized local
selection instead uses the direct declaration and its checked public export
mapping, without manufacturing an Import proxy. No independent
failure-factory export or mandatory new constructor export key is added.
Any callable Import required by the selected ordinary execution representation
still needs its actual key and full expectation; the class selector cannot
invent one.

Check ModuleRef target/runtime expectations and every original type, schema,
member, default and witness expectation in its own context. Several legal
keys or Import views may resolve to one definition, but all contracts must
succeed and retained alias equality must survive. Reject duplicate Import
triples, one expectation resolving inconsistently, wrong declaration kinds,
wrong owners, missing/nonpublic exports and proxy re-exports. A matching key
or correct Core role does not excuse an incompatible contract.

**The current assigned DefId/Export target set does not provide a semantic
AttributeSchema declaration carrier.** The schema selector above therefore
does not authorize writing it as a TypeDef, a new Export kind, an attribute
constructor, a Blob, optional debug data or a native/layout schema.
Source/host binding must supply the actual declared schema and provenance.
Standard exchange of a retained schema/default additionally requires an
explicitly supported publication/reference representation. If unavailable,
decline that affected consumption. Choosing that codec is a remaining gate,
not an encoding selected by this proposal.

Before metadata or runtime use, consumers must establish:

- Exact artifact schemas, selected Modules/revisions and complete retained
  requirement/target/runtime support; no schema composition from graph
  sections, Features, magic/version bytes or E1 identity.
- Required role selections, authority, selected declaration kinds/owners,
  complete class/self-witness identities, exact schema/default recipe and
  original semantic owner/phase correspondence.
- Safe record extents, bounds, reserved fields, typed references, ownership,
  substitutions and exact constructor target/type/default-plan consistency.
  The 20-byte predicate attachment contains no substitute construction plan.
- Actually supported construction, partial-state cleanup, complete exception
  publication, event/cause/root/backing and code/provider lifetime contracts.
  A decoded graph, eligible class, available constructor or terminal-policy
  flag alone supplies none of that execution support.

These are structural/support/binding checks, not mandatory semantic re-proof
of provider predicates or constructor Bodies. Source producers check authored
source and publish truthful facts. Structural-only inspection remains a
narrower operation than accepted binding or execution.

## Context, replacement and runtime origins

Selections belong to the logical Module that authored the check/default, not
the first Module in an artifact, importing caller or physically generated
helper. Co-located Modules can have distinct explicit contexts. Do not add a
global singleton package or enlarge E1's existing propagation-family equality
test with these roles. Existing required interface/delivery compatibility
still applies; exact catches observe the actual originating class, never a
category-name conversion to the caller's fallback.

Replacement selects an explicit actual revision under the original Module
identity and retained keys, then rechecks every original expectation and
stronger fact actually used. Preserve class/schema identity correspondence,
alias equality, primary/default ownership, complete substituted recipe,
selected constructor member and bound defaults, phase/effects/failure-channel
contracts. A new overload does not rerun lookup for an existing construction
plan. Removing its target, splitting aliases or changing the semantic schema
does not trigger another constructor/class/schema search.

Rewriters and packing visit explicit and implicit fallback dependencies,
schema recipes, constructor plans/default values, witness and predicate
dependencies, phase-local captures and original owner contexts. Remap decoded
typed references and enclosing ranges/extents, not coincident bytes.
Contextual shared rows must be cloned when their resolved meanings differ.
An Import-to-direct-definition rewrite may not erase an independently
replaceable original expectation or its accompanying Core context.

JIT/AOT lowering consumes the checked selection and construction plan.
Live objects, pending causes and retained code keep their actual class,
witness, constructor, artifact/load and cleanup providers until last use.
Rebinding does not restamp an existing event or authorize live migration,
hot unload, a new exception ABI or outward native unwinding.

## Proposed integration and remaining gates

If adopted, integrate only these deltas into the owners; no such edits are
made here:

| Owner | Candidate delta |
|---|---|
| [Requirement failures §§3, 7](../specs/language/requirement-failures.md) | Specify the exact schema/fallback authority-to-binding handoff and originating Module context. Preserve existing priority, ordinary construction and schema ownership. |
| [Runtime failures §§1, 2, 7](../specs/runtime/standard-failures.md) | Apply the exact PostconditionException anchor and explicitly replace only false declaration ensure's common creation-path invocation with ordinary zero-supplied constructor binding. Preserve all other categories and actual-failure propagation. |
| [Library identity](../specs/libraries/adopted-inventory.md#1-status-and-identity), [binding](../specs/representation/binding-and-replacement.md), [logical records](../specs/representation/logical-records.md) | Cross-link selected role/context retention and the postcondition construction refinement without extending current physical target sets. |
| [Clause conformance](../specs/conformance/il-contract-clauses.md), [requirement cases](../specs/conformance/source-requirement-failures.md), [runtime cases](../specs/conformance/runtime-standard-failures.md) | Add the selection-specific cases below after adoption. Preserve clause shapes, ordinals, grammar and independent evidence boundaries. |

The remaining gates are distinct from approval:

1. **Decision review/adoption:** review this companion selection and the
   narrow ensure constructor refinement. Provisional authorization is not
   normative approval; E1 and general-predicate decisions remain separate.
2. **Real provider declarations:** supply authorized exports, exact declared
   schema and class contracts, checked self witnesses and actual ordinary
   constructor members. No production provider identity/key is invented here.
3. **Host/source integration:** carry selections through compilation options,
   references, shared bound-tree traversal/rewriting and mandatory verification;
   bind grouped declaration clauses and default recipes at their real owners.
   Any CLI transport must preserve the same explicit context, not introduce a
   default provider or private manifest/alias here.
4. **Interchange integration:** complete supported schema/default/recipe,
   constructor/default-value, phase and provenance publication/reference
   codecs and ordinary provider/alias/replacement binding. Retain the existing
   predicate attachment; complete its separate Feature/reference registration.
   This proposal allocates none of these physical identities.
5. **Execution integration:** preserve exactly-once phase enforcement,
   construction effects, argument/partial-state cleanup, saved results and
   actual output states, real event publication/transport and provider/code
   lifetimes through JIT/AOT lowering.
6. **Evidence:** qualify the final implemented inputs through public source
   APIs, independently supplied metadata/provider contexts, rewriting and
   replacement cases, then supervised runtime cases. Binding, codec success
   and emission are not runtime evidence or milestone completion.

## Candidate conformance cases

These cases supplement, not duplicate, the linked source/runtime corpus.
They are proposed outcomes with supplied eligible declarations and admitted
support, **not tests executed for this document**.

| Case | Candidate outcome |
|---|---|
| Authorized public Core declarations at non-name-derived keys, with a non-first selected provider Module | Select the exact anchors after ordinary binding; preserve their accepted source identities. |
| A package exports same-spelled classes/schema with identical signatures or witnesses | No Core role without authorized exact selection; reject substitution for fallback/schema anchors. |
| Explicit E resolves to an unrelated eligible user class named RequirementException | Use that class's ordinary constructor. The same class cannot stand in for the unqualified Core fallback. |
| Type alias or several export keys resolve to the selected class/schema | Preserve legal exact identity and every expectation; reject alias splitting, conflicting bindings or weakest-view merging. No new schema-alias syntax. |
| Fallback class is a struct/enum, generic declaration, wrong complete self type or implements another IException | Reject the selected role, regardless of constructors, layout or names. |
| Schema identity is correct but its arguments, requiredness, labels, repeatability or primary/target rules changed | Reject schema mismatch before default use; explicit E does not excuse it. |
| Valid positional/named direct typeof recipe, versus computed query object, user @typeof call, hole or cyclic alias | Bind the former under original formals; diagnose the latter without reflection or name conversion. |
| Getter has duplicate inline/targeted defaults, or a secondary/type/property placement attempts an override | Apply exact effective occurrence/owner rules; reject an invalid callable contract. |
| A substituted default family yields an eligible closed class with published constructor evidence | Resolve ordinary construction per application/check; interface eligibility alone supplies no constructor. |
| Parameterless, optional-default and admitted empty-params constructors; absent/inaccessible/ambiguous binding | Apply ordinary ranking and retain the unique plan; otherwise reject with no factory or class retry. |
| Selected constructor requires unsafe authority or exceeds the check's admitted effects | Reject absent ordinary authorization/bounds; false-path selection supplies no permission. |
| Normally false ensure with a callable RequireException override | Use exact selected PostconditionException's ordinary constructor and common default-creation contract, not the override or another requirement default. |
| True predicate, false predicate, actual predicate failure and constructor/initializer/constructor-clause failure | Construct zero times on true or predicate failure, once on false; propagate actual creation failure without fabricating the outer class. Preserve real outputs and completed cleanup. |
| Relocated inherited clause or generated/suspended check, versus the constructor's own authored requirement | Keep each original default/Module/phase; do not transplant caller or enclosing defaults. Static constructors follow the same runtime clause rules. |
| Two using Modules share an artifact or independently select authorized class/schema providers | Resolve each originating context, with existing delivery compatibility; physical row order and caller defaults select neither. |
| Required role/context is missing, duplicate, partial, malformed, unauthorized or conflicts with a reference | Decline affected binding/use; no first-provider default, inert-schema downgrade or caller-context replacement. |
| Correct selected TypeDef with incompatible member/default contract, wrong ModuleRef requirements or proxy re-export | Reject ordinary binding even though the role label or ABI matches. |
| Graph/E1 decoding succeeds but schema/default/construction publication or runtime support is missing | Decline affected execution; do not infer a composite schema, extend Export kinds, suppress enforcement or use private/debug bytes. |
| Explicit compatible revision preserves all original expectations despite changed physical rows; a new overload or changed default is supplied | Accept only the preserved directed contract/plan. Do not reselect an overload/default or restamp an existing event; unrelated additions are not universally breaking. |
| Typed remapping/packing and live provider replacement | Preserve implicit role/default dependencies, owner contexts and actual lifetime origins; reject lost expectations, byte substitution or premature unload. |
