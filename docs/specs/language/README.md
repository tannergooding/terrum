# Source language

The source language defines checking, binding, and abstract evaluation
independently of any compiler's current support.

| Chapter | Canonical responsibility |
|---|---|
| [Source design overview](../language.md) | Selected direction, integration summaries and explicitly unselected extensions |
| [Source grammar](../grammar-spec.md) | Productions, contextual vocabulary and grammar-specific semantic constraints |
| [Lexical grammar](../lexical-grammar.md) | Tokens, spelling, escapes, separators and literal lexical structure |
| [Type families](../type-families.md) | Primitive and qualified family obligations distinct from physical layout and candidate library breadth |
| [Conversions](../conversions.md) | Exact numeric/character conversion domains, rounding, failure and representation distinctions |
| [Call/output contract overview](../language-contracts.md) | Integration of call timing, outcome/state, checks, cleanup and canonical proof/effect owners |
| [Source input](source-input.md) | Strict UTF-8 decoding, signatures/preservation, malformed-byte extents, immutable UTF-16 snapshots, and origin coordinates |
| [Literal construction](literal-construction.md) | Target-owned parser records, signed tokens versus operators, raw/decoded input, primitive bit/range/rounding and decimal cohorts |
| [Construction handlers](construction-handlers.md) | Target-owned builder schemas, TextTarget parameters, constructed recipes, eager capture/acquisition/drain, dependencies and independent construction admission |
| [Value iteration](iteration.md) | Nominal synchronous factory/direct-cursor sources, single-output advancement, item ownership/backing, loop order, mandatory close and eager spreads |
| [Async value iteration](async-iteration.md) | Synchronous async-cursor acquisition, pending initialization reservations, exact completion/output correlations, forwarding and awaited loop close |
| [Lending iteration](lending-iteration.md) | Distinct readonly/writable sync/async roles, element-place aliases, reference outputs, cursor-bound defaults and checked backing/invalidation refinements |
| [Async execution](async-execution.md) | Eager execution, logical operation establishment, suspension-independent failure channels, completion/cleanup and responsible observation |
| [Task responsibility](task-responsibility.md) | Affine Core Task<T>, consuming await with named move, distinct observation/payload/output facts and explicit every-exit settlement/transfer |
| [Ownership and cleanup](ownership-and-cleanup.md) | Structural partial field state, write/init-authorized transfer and repair, complete Cleanup receivers, replacement and complete versus failed-construction retirement |
| [Local failures](local-failures.md) | Nominal Exception capability, complete payload/category subjects, explicit reference/Copy catch bindings, active rethrow and retained responsibility |
| [Requirement failures](requirement-failures.md) | Explicit runtime selectors, ordinary `new E()` on failure, primary callable defaults, Core fallback, actual constructor failure and output preservation |
| [Captured exceptions](captured-exceptions.md) | Exact opaque affine Core owner, lexical Capture, handler/borrow responsibility, same-event consuming throw and transitive retirement/admission |
| [Generated producers](generated-producers.md) | Owned named affine cursor shells, generic/capture mapping, lazy body/yield, creation versus operation clauses, required close and async lifecycle |
| [Dependencies and views](dependencies-and-views.md) | Whole-source identities, DependsOn/Captures/ForwardsBacking, stored and returned values, retained destinations, phases and checked member/witness constraints |
| [References and explicit indirection](references.md) | Reference descriptors, formation, dereference, member access, parameter forms, and signature identity |
| [Local declaration spaces](local-declarations.md) | Local/parameter collisions, scopes, declaration points, and implicit names |
| [Names and binding](names-and-binding.md) | Namespace/member collisions, generic formal names and arity, imports, accessibility, candidate groups, and type-call classification |
| [Initialization and structural flow](initialization-and-flow.md) | Live/vacant states, joins, pattern scopes and mutable bindings, negative paths, retained loop matches, contract-phase scopes, and invalidation |
| [Values and statement execution](expressions-and-statements.md) | Expression categories, unit values versus discard, explicit result use, conditions, loop transfers, and return/fallthrough |
| [Interface witnesses](interface-witnesses.md) | Nominal member identity, inherited/default and associated binding, qualified-self contract adaptation, and publication |
| [Complete types](types-and-normalization.md) | Type terms, qualifier positions, actual-write constraints, finite substitution, optional and requested-access boxing normalization, payload selection, and signature identity |
| [Thread capabilities](../runtime/sharing-and-handoff.md) | Resolved primary-type opt-in, capability-owned ordinary where conditions, complete-form eligibility and separately checked current protocol authority |
| [Constant generics](constant-generics.md) | Initial domains, derived Transparent primitive-wrapper eligibility, canonical nominal/carrier value identity, checked construction, forwarding and independent contracts |
| [Registered queries](registered-queries.md) | Exact Core identities, SizeOf/AlignmentOf value subjects, runtime type evidence, target values and fixed/coupled retained observations |
| [Calls and inference](calls-and-inference.md) | Argument mapping, finite typed and delayed callable evidence, contextual completion, candidate ranking, and reusable omitted-output destinations |
| [Built-in scalar operators](primitive-operators.md) | Mandatory signatures, exact data/count contexts, integer and floating edge domains, comparisons, and checked place/accessor updates |
| [Absolute-value members](absolute-value.md) | Same-type numeric Abs signatures, checked integer magnitude, floating value classes, failure/result states and explicit-variant admission |
| [Numeric selection members](numeric-selection.md) | Same-type Min/Max/Clamp signatures, NaN/zero/cohort selection, ordered-bound preconditions, InvalidBoundsException and call/state/admission |
| [Floating environment](floating-environment.md) | Sign-only Abs/negation, quiet processing-result NaNs, deterministic admitted constant recipe and no ambient status facility |
| [Numeric capabilities](numeric-capabilities.md) | Exact coherent Core groups, combined selection, self/Copy constraints, primitive witnesses and optional composition |
| [Scalar checked failures](scalar-failures.md) | Exact zero-data Core payloads, checked overflow/unrepresentability, integer zero division, original-count validation and event/state/phase admission |
| [Proof and constant execution](proof-and-constant-evaluation.md) | Local scalar facts/transfers, conservative invalidation, executed-path eligibility, reproducible NaNs, dependencies, and portable resource accounting |
| [Published predicates and pure evaluation](published-predicates-and-effects.md) | Unified logical input-state purity, forms/modes, finite reads/effects, NoAlias parameters, initialization results and published predicate admission |

[Reference conformance cases](../conformance/source-references.md) specify
positive and negative source, access, signature, and metadata outcomes.
[Input cases](../conformance/source-input.md) cover exact scalar/invalid-byte
boundaries, signatures, preservation, UTF-16/byte mapping and decoded-input APIs.
[Vocabulary cases](../conformance/source-vocabulary.md) cover reserved/contextual
recognition, exact escapes, type-query evidence and the three `via` selector roles.
[Literal cases](../conformance/source-literal-construction.md) cover checked
registrations, kind-first selection, signed magnitudes versus bits, direct
rounding/cohorts, validated text and frozen output/dependency admission.
[Builder-recipe cases](../conformance/source-builder-type-recipes.md) cover
exact schemas/authority, target-formal binding and forward substitution,
conflicts, independent identities and operation/value admission boundaries.
[Builder-completion cases](../conformance/source-builder-completion.md) cover
the consuming Core requirement, exact output states, transferred/residual
cleanup and explicit reusable adapters.
[Builder-acquisition cases](../conformance/source-builder-acquisition.md) cover
the parameterless Core factory, eager order, output/cleanup state and
ordinary explicit runtime-context APIs.
[Construction-operation cases](../conformance/source-construction-operations.md)
cover standard typed writer/value roles, int32 Unicode-scalar alignment,
once-only drain, collection item/result relations and admitted spread boundaries.
[Value-iteration cases](../conformance/source-value-iteration.md) cover exact
nominal sources, vacant publication, copy/transfer and backing, loop close
and eager factory/direct-cursor capture/drain/abandonment.
[Async-value-iteration cases](../conformance/source-async-value-iteration.md)
cover phase-bound nominal requirements, pending reservations, terminal cleanup,
actual/observed completion, saved output versions, forwarding and async loop exits.
[Lending-iteration cases](../conformance/source-lending-iteration.md) cover
mode/witness selection, writable acquisition, exact descriptor publication,
element aliases, surviving backing/refinements, async reservations and close.
[Async-execution cases](../conformance/source-async-execution.md) cover
caller/establishment versus established failure, inline and suspended execution,
completion checks/cleanup, NoThrow attribution and responsible observation.
[Task responsibility cases](../conformance/source-task-responsibility.md) cover
affine identity, guarded owner transfer, consuming waits, terminal and payload
observation, exceptional settlement, cancellation boundaries and admission.
[Owning-field cases](../conformance/source-ownership-and-cleanup.md) cover
construction/acquisition, partial moves and repair, exact cleanup order,
replacement, surviving dependencies, exceptional holders and admission.
[Local-failure cases](../conformance/source-local-failures.md) cover registered
nominal opt-in, closed subjects/witnesses, ordinary Copy/reference inputs,
incremental selection, active ownership and retained capture/admission gates.
[Requirement-failure cases](../conformance/source-requirement-failures.md)
cover source selectors, exact defaults/owners, zero-input binding, real failed
paths, output/supporting-scope preservation and independent admission.
[Captured-exception cases](../conformance/source-captured-exceptions.md) cover
exact Core/context selection, handler publication/vacancy, preserved input
lifetimes, consuming propagation, causes, every-exit retirement and admission.
[Sharing/handoff cases](../conformance/source-sharing-and-handoff.md) cover
explicit/conditional eligibility, alias reservations, publication, reader/
exclusive tokens, captures, effects, unsafe/trust and retained admission.
[Generated-producer cases](../conformance/source-generated-producers.md) cover
owned schema/role/formal mapping, capture, lazy yield and state, clause/effect
phases, exact close/failure schedules and independent representations.
[Dependency/view cases](../conformance/source-dependencies-and-views.md) cover
exact source recipes, retained destinations, broad/backing-only relationships,
copies/joins/versions, capabilities, witness conditions and independent admission.
[TextTarget cases](../conformance/source-text-targets.md) cover resolved
parameter schemas, eager typed formatter formation, forward inference,
module-local complete-shape identity, ordinary formatting/retention and
independent parameter contracts.
[Eager construction cases](../conformance/source-eager-construction.md) cover
source-order captures, after-capture acquisition, drain-only formatting and
iteration, refusal, ownership/cleanup and unadopted incremental semantics.
Plain-construction Boolean refusal follows ordinary `require` failure;
explicit Try/writer outcomes remain separate.
[Materialization cases](../conformance/source-materialization.md) give
source-equivalent loop expansions, scope boundaries, and exact owner traces.
[Local-name cases](../conformance/source-local-declarations.md) distinguish
name lookup from initialization, capture, and lifetime permission.
[Binding cases](../conformance/source-name-binding.md) cover declaration-kind
collisions, arity, import ambiguity, access filtering, and recorded targets.
[Initialization/flow cases](../conformance/source-initialization-flow.md)
cover conditional initialization, repeated patterns, saved outcomes, and
explicit initialization-destination projections.
[Value/execution cases](../conformance/source-values-and-execution.md)
cover unit positions, explicit discard, scope cleanup, and exact loop order.
[Witness cases](../conformance/source-interface-witnesses.md) cover nominal
origins, associated identity, safe qualified-self adaptation, and retained contracts.
[Type cases](../conformance/source-types-and-normalization.md) cover finite
aliases, exact qualification, actual-write constraints, normalized boxing
access, and tuple/callable shape.
[Constant-generic cases](../conformance/source-constant-generics.md) cover
domain/Transparent admission, canonical value identity, validity, construction,
forward binding/defaults and independent contract preservation.
[Registered-query cases](../conformance/source-registered-queries.md) cover
exact authority/signatures, complete subjects, target layout, required
execution and explicit observation dependencies through replacement.
[Call cases](../conformance/source-calls-and-inference.md) cover exact/access
evidence, delayed callbacks, signature-first literal acceptance, overload
quality, output availability, supporting scopes, and ownership-safe extraction.
[Primitive cases](../conformance/source-primitive-operators.md) cover every
scalar family, checked ranges/counts, exact rounding and remainder,
NaN/signed-zero observations, and once-only update/accessor timing.
[Absolute-value cases](../conformance/source-absolute-value.md) cover numeric
owners/signatures, all 8-bit inputs, wider/native boundaries, floating classes,
once-only calls, no-result failure and independent effect/variant admission.
[Numeric-selection cases](../conformance/source-numeric-selection.md) cover
exact members, integer/floating extremes, signed zeros and supplied decimal
cohorts, valid/reversed/unordered bounds, source-order staging, moves,
requirement-event ownership and complete retained support.
[Scalar-failure cases](../conformance/source-scalar-failures.md) cover exact
Core types/construction, arithmetic/conversion/count mappings, no-result
state, event/cause ownership, source phases and complete required support.
[Floating cases](../conformance/source-floating-environment.md) cover exact
sign/NaN/quantum behavior, ordinary versus constant recipes and target duties.
[Numeric-capability cases](../conformance/source-numeric-capabilities.md) cover
group membership, primitive witnesses, independent evidence and retained bounds.
[Standard runtime cases](../conformance/runtime-standard-failures.md) cover
fixed categories and original versus fresh initialization-event delivery.
[Proof/evaluation cases](../conformance/source-proof-and-constant-evaluation.md)
cover bounds/exclusions, Boolean relationships, conservative control/effects,
constant paths, frozen values and exact budget boundaries.
[Predicate/effect cases](../conformance/source-published-predicates-and-effects.md)
cover form authorization, guarded effects, memory dependencies, NoAlias and
output obligations, predicate phases and independently consumed contracts.

The remaining source chapters currently reside in the parent partition:
[source productions](../grammar-spec.md), [lexical rules](../lexical-grammar.md),
[type/operation context](../language.md#types-and-operations),
[call and output contracts](../language-contracts.md),
[type families](../type-families.md), and [conversions](../conversions.md).
Their rules remain authoritative until relocated; this index does not create
another copy of them.
