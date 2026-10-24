# Binding and replacement compatibility

## 1. Scope and selected identities

The logical `binding-compatibility/1` contract defines how a selected provider
satisfies a retained expectation. It applies to import resolution, witness
requirements and replacement, with each feature's complete supported records
and checking rules. It neither releases a byte format nor enables a missing
generic, witness, predicate, ownership, output or native representation.
[Requirements and admission](requirements-and-admission.md) separately owns
schema/rule support and the before-use boundary.

Resolve identity **before** comparing contracts. An Import selects one
Module identity and one export key, under the ordinal decoded-text rules in
[metadata](../il-metadata.md#metadata-logical-tables-and-row-semantics).
Resolution identifies one declaration of the required kind. It does not search
other modules, overloads or similarly shaped declarations when comparison fails.
The selected module revision must be unambiguous in the admitted dependency
set. Producer authentication is a separate environment obligation.

The consumer's expected local declaration is a proxy for that selected
declaration, not a new nominal type. Its complete references are resolved in
the consumer's owning module and formal/substitution context. Row numbers,
String encodings, parameter names, debug paths, bodies and ABI carriers do not
establish cross-artifact identity.

Distinct export keys may name the same public local definition. They are lookup
aliases, not new nominal identities or permission to export an imported
expectation. A cross-module forwarding API needs an actual admitted declaration
and its own contract; this contract adds no re-export or forwarder syntax.
Retained equality and selected-owner relationships must still hold if an alias
is changed. Equal layout cannot justify splitting one retained identity into two.

Every retained Import is checked against its own expected contract. Different
expectations for the same provider/key need not be logically equal: one provider
may satisfy both a broad effect bound and a stricter one. All must succeed;
there is no "weakest import wins" merge. One expected declaration cannot resolve
to conflicting selected definitions. Identical physical Import triples are
duplicates, not distinct choices; the metadata uniqueness rule still applies.

## 2. Directed satisfaction and exact dimensions

Write "provider satisfies expectation" only when every retained obligation
passes under the applicable supported checking rules. This is directed:
satisfying a broad expectation does not make that expectation a replacement
for the provider's stronger contract. Equality of all logical facts is sufficient
where the identities, context, support and other admission gates also agree.

The relation has three parts:

| Part | Required comparison |
|---|---|
| Selected identity and exact structure | Preserve the selected declaration/owner, complete normalized types, logical positions, modes and exact state/representation/phase contracts |
| Caller domain | Every caller admitted by the expectation still satisfies the provider's requirements; do not add an unsafe, separation, access, formation or protocol obligation |
| Published guarantees | Establish every expected guarantee, under its original conditions, subjects, phases and rule interpretation |

An extra guarantee or fewer caller requirements may satisfy the last two
parts. They cannot override the first part or change an independently promised
observation. In particular, a forbidden state is not a weak value guarantee.
An implementation cannot make postconditions vacuously true by eliminating a
normal completion that the API promises, nor erase specified validation failure.

The following dimensions are not widened or erased by binary assignability:

| Dimension | Retained obligation |
|---|---|
| Complete types | Exact normalized nominal/constructor identities, ordered arguments, constant domains/values, representation, optionality and every inner/outer access or pin qualification |
| Callable shape | Callable kind, owning type, receiver presence/authority, result and every ordered logical parameter, including zero-data `void`; exact input/init mode and any retained argument-formation contract |
| Formals | Owner/ordinal correspondence, type versus constant kind and constant domain; substitution preserves all complete arguments and selected witnesses |
| Output state | Exact destination, type, result/status condition and initialized/partial/vacant state on each admitted normal/failure/completion outcome |
| Ownership and phases | Transfer/consumption, responsible holder, roots, capture and publication phases, cleanup order/affinity, reservations and release/invalidation endpoints |
| Representation | Applicable fixed or coupled layout/Blittable commitments, field/carrier validity, call classification and target/runtime requirements |
| Declared behavior | Promised values, failure categories/ordering, protocol observations, selected evaluation-mode behavior and documented constant results |

There is no binary covariance of init storage, readonly-to-write authority
upgrade, implicit boxing, result discard or synthesized owner. Ordinary
[qualified-self forwarding](../language/interface-witnesses.md#source-equivalent-contract-adaptation)
can use its separately checked access attenuation; that adapter is not equality
of the unadapted signatures. A type name or compatible machine register does
not authorize it.

Matching actual reference dependencies does not make different declarations
interchangeable. A direct reference and a wrapper-carried reference can
designate the same place, while their declared source permissions/provenance,
retained extents, authority and invalidation obligations still require the
comparisons above. Descriptor copying does not erase any of those facts or
turn a reference to source storage into backing-only forwarding.

Formal names and local formal rows may differ. Map corresponding declaration
formals by kind and ordinal, then compare their substituted terms and
constraints; do not compare raw owner row numbers or invent a substitution
to make a mismatch pass. A callable's supported constraint implication may
relax its caller requirements while preserving every old-domain type, witness
and result obligation. Changing a constant domain or a nominal type's retained
formation/validity contract is not merely deleting a callable constraint.

A TypeDef comparison preserves its retained nominal validity, public operations,
ownership and representation commitments. It need not compare unrelated private
body details. This does not grant resilient layout: an inline/fixed/coupled
consumer still retains its full layout obligations. Additional unrequested
exports or operations are permitted only when they leave every retained
obligation intact, including closed cases or complete witness requirements.

Public-contract artifact contents must also support every published
independent-use promise. A reference artifact used for required constant
execution retains the checked `ConstEval` Body or an equivalent complete
admitted program and its dependencies; a null, empty or throwing stub does
not satisfy that promise. A published `Blittable` value retains the field and
representation facts needed for consumer validation. A non-Blittable
reference facade may omit private implementation fields when its contract
allows, but its implementation artifact cannot omit the actual fields.
Exact codecs and artifact-closure rules remain representation work.

For a PropertyDef, compare its selected owner and complete value type, then
its **referenced getter contract**: receiver, parameters/modes, result, access,
effects, state and dependencies. The property-selected accessor correspondence
does not need an independently exported getter key or matching `get_Name`
spelling. It does not expose that implementation through ordinary lookup.
Getter or property naming cannot conceal a different result or caller-unsafe
requirement. Other accessor kinds require their own admitted schemas.

## 3. Bounds, predicates and retained observations

Only an explicitly defined contract dimension has a strengthening relation.
Do not treat every attribute, flag, clause, capability or integer as an ordered
guarantee. A compatibility/support claim includes every required fact even
when another fact would make it unnecessary to a particular caller. The
specification does not prescribe how an implementation handles a fact it
does not support.

For the currently assigned MethodDef Effects promises in a valid artifact:

| Fact | Provider satisfies expectation when |
|---|---|
| `DoesNotReturn` | An expected promise is preserved; adding it is not permission to violate a promised normal completion or another outcome/phase obligation |
| Caller-unsafe | A safe expectation never selects a caller-unsafe provider; dropping the requirement is permitted while preserving the other contracts |

The masks are not compared for equality or numeric magnitude. The
[clause matrix](contract-clauses.md) governs owner/cardinality/applicability,
not this variance table.

[Published predicates and evaluation forms](../language/published-predicates-and-effects.md)
compare typed bound subjects and selected operation/witness identities.
Establish that the expected caller requirements imply the provider's, and
that the provider's checked guarantees imply the expected ones, using the
applicable structural/scalar rules and portable proof floor. Exact normalized
predicate identity can establish equality. An opaque predicate is not a
general numeric summary, and equal names, sample results or private bodies
cannot establish arbitrary implication. Compatibility is not established by
an unproven implication; the specification does not prescribe the
implementation's response.

Implication retains guards and phases. A guarantee only after a true result,
or only in a supported pure form, cannot satisfy an unconditional or ordinary
form guarantee. Additional clauses must be valid on the old caller domain,
survive their required cleanup and preserve the entire invocation's effects.
Adding a postcondition that may fail on valid old inputs is not strengthening.

Requirements that merely restrict caller authority/domain are distinct from
executable validation promises. Removing NoAlias or an unsafe-caller requirement
can admit more callers while preserving the old-domain contract. Replacing a
published `require` with a weaker check must additionally preserve any specified
failure, clause order, once-only evaluation, transferred-argument cleanup and
phase boundary. This relation supplies no new undefined-invalid-input rule.
Ordinary precondition checks are not automatically erased because the provider
has a broader useful domain.

Pure-form support and ConstEval authorization are retained capabilities, not
Effects bits. A provider must support every expected selected form and
preserve its domain, read/effect bounds, results, mode-query observations and
transfer obligations. More supported forms do not retrospectively change the
form selected by a retained caller. Constant execution still needs its actual
program, target/value codec, limits and dependencies. An algorithm remaining
pure does not make changed documented constant results compatible.

Read/effect/source bounds can become more precise only under their defining
rules. For [whole-source relationships](../language/dependencies-and-views.md#published-witnesses-and-generic-refinements),
a provider may exclude a permitted source or retain it with backing-only
permission. Exclusion satisfies that source's backing refinement without a
redundant annotation. Adding a source/capture, changing a destination/phase,
replacing backing-only forwarding with storage borrowing or weakening a
required validity/invalidation bound does not satisfy the expectation.
NoAlias maps exact formal/accessed regions; it does not prove fresh or
nonoverlapping backing from type qualification alone.

## 4. Complete comparison graphs

Compare the finite retained contract graph, not just the root MethodSig or
export row. It includes selected owners/types, accessor/requirement contracts,
associated bindings, witnesses/projections, constraints, values, layout,
cleanup, dependency and required rule/target/runtime edges. An unused required
member or declaration-only dependency still counts.

Comparison keys include both resolved declarations, their complete using
module/formal/substitution contexts and the applicable relation/rules. A pair
of raw row indexes is insufficient. A shared TypeSpec/MethodSig is valid only
if it means the same thing in every using context. If resolving one consumer's
import changes that meaning, clone the structural row and remap the affected
uses instead of rewriting a row still used by another context.

A worklist may deduplicate an already encountered **same complete pair**.
Each pair's local identity, exact dimensions, implications and outgoing
obligations must still pass. Acceptance occurs only after the entire closure
has succeeded; an in-progress pair is not a published successful comparison.
A mismatch behind a recursive nominal edge cannot be hidden by first seeing
the root. Recursive data may have a finite nominal anchor, but malformed
structural type cycles, infinitely expanding substitutions or recursive
transparent associated bindings do not become valid through memoization.

Graph-cycle handling establishes no predicate implication or body truth.
Circular claims that each unproved guarantee follows from another do not
establish a compatibility claim. The producer is responsible for authored
body facts; consumers validate selected record structure and references
before affected use, then rely on producer-published facts without semantic
re-proof. A binding compatibility claim still checks the declared identities,
requirements and complete comparison relation.
Applicable resource exhaustion explicitly declines comparison. No arbitrary
depth cutoff may count an unvisited obligation as satisfied.
[Resource accounting](resource-accounting.md) assigns separate structural
and semantic work/state scopes, with numerical portable minima unassigned.

## 5. Packing, replacement and relied-on facts

Retained internal and external imports use this same relation. A merger's
successful compatibility claim requires the expectation to hold for the
resolved producer definition. Module identities, required semantics and all remaining
declaration/value/code/load/layout/cleanup obligations survive that rewrite.
Omitting an unneeded Import is not omitting its required meaning.

Preserve the caller's retained expectation. A provider's extra guarantee is
not silently substituted into an interface requirement, generic contract or
consumer metadata. If compilation/optimization relies on a stronger published
provider fact, retain that stronger dependency obligation in the resulting
artifact. A replacement compatibility claim must still satisfy that
obligation; a later provider satisfying only the old broad expectation does
not establish it.
Private body specialization likewise retains its actual code/value dependencies.

Replacement compatibility is determined against the actual selected provider
revision and retained expectations. Keeping a module/key, same body bytes,
implementation digest or prior acceptance flag does not itself establish
that compatibility. Cached decisions remain applicable only while their
complete relied-on identity, contract/context, rule support, target/runtime
and producer facts remain unchanged.

Direct-bound definitions and native derived artifacts retain these obligations
too. A replacement must preserve affected uses, including embedded
constants, layouts, witness selections, code/load and cleanup dependencies;
updating a metadata import does not patch old machine code or invalidate its
facts. A semantically satisfying provider with an incompatible physical ABI
still cannot service an unchanged native caller.

This specifies compatibility before affected use, not live hot replacement,
unload, quiescence or owner migration. Those need separate code/data lifetime,
concurrency and activation protocols. Binary compatibility also does not prove
source rebinding compatibility: added overloads/defaults/names can change new
source selection. Already retained calls keep their selected declarations,
argument mapping/default values and witness bindings.

## 6. Representation and conformance boundary

The current schema's assigned String, Import/Export, TypeSpec, MethodSig/Param
and Effects shapes support their stated bounded inspection/comparison duties.
Their recognition does not activate general TRRM import execution or any
unrepresented language contract. In particular, generic/witness exports,
predicate programs, source-unit access, richer outputs/ownership, complete
layout commitments and target ABIs still need their complete physical
representations and admission rules.

Target/runtime and rule compatibility use exact supported identities or
expressly defined applicable mappings. Numeric revision ordering, a broader
host API, a portable label or producer correctness does not establish them.
The current ModuleRef requirement/policy fields must match the selected
producer as metadata specifies; this relation creates no alternate selector.
Admission never runs user getters, equality, initialization or cleanup to
discover whether a contract matches.

[Conformance cases](../conformance/il-binding-and-replacement.md) distinguish
identity resolution, exact dimensions, directed bounds, finite comparison
contexts and retained replacement obligations. Finite supplied-fact and row
models are not proofs of arbitrary predicates, source bodies, layout/ABI
implementations, linker transformations or native/live replacement.
