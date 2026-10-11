# Language contracts: calls, outputs and checks

This document records accepted execution and checking contracts that refine
[language](language.md) and [lifetimes](lifetimes.md). It does not assign
candidate syntax or a new semantic-IL representation.
Approved source forms are in the [source grammar](grammar-spec.md);
the [proof/evaluation chapter](language/proof-and-constant-evaluation.md)
owns the selected rule catalog and resource envelope. Remaining alternatives
belong in the [proof proposal](../proposals/static-proof-and-constant-evaluation.md).
[Implementation](../implementation.md) remains the executable support boundary;
concurrency follows the separately [accepted memory model](memory-model.md).
Implicit arguments expand to ordinary operations, conditional outputs carry
checked state facts, and executable contracts participate in ordinary
evaluation and cleanup. Convenience syntax does not create a second lifetime
system or a second arithmetic system for the prover.

## 1. One call and reference model

The canonical [reference chapter](language/references.md) defines descriptor
values, safe reference dereference, explicit indirect member access, and
parameter identity. This chapter owns call sequencing and output publication;
neither implicit adaptation nor an output contract introduces transparent
reference-value semantics.

### 1.1 Places, values, and materialization

A **place** is existing storage with a checked identity, type, access, lifetime,
and backing/ownership dependencies. A **value expression** computes a value;
it need not identify storage that the caller can borrow. These terms describe
semantic categories, not a new public type family.

| Argument expression | Meaning for `implicit ref<T>` |
|---|---|
| Already compatible `ref<T>` descriptor | Pass the descriptor with its complete dependencies; do not borrow its descriptor slot |
| Eligible local, parameter, field, or element place of the required type | Form an ordinary readonly reference to that place |
| Reference-returning access | Use the returned place and its published dependencies, as for explicit reference passing |
| Value-returning call, property, operator, or explicit conversion | Evaluate once into a local temporary, then form an ordinary readonly reference to that temporary |
| Place whose ordinary reference use is invalid | Diagnose the same access/lifetime conflict; do not silently copy it to evade the conflict |

These alternatives use complete types. When `T` is itself `ref<U>`, borrowing
its descriptor slot creates `ref<ref<U>>`; generic substitution does not
implicitly dereference an argument.

The distinction is the declaration's result category, not whether its source
looks like a field or an accessor. A value-returning property is not a reference
to hidden property storage. No new implicit conversion chain is introduced.
The complete `T` is preserved; a readonly reference to a handle slot does not
remove write authority from the handle value it contains.

Pattern matching does not invoke parameter materialization or reference
formation. `x is T t` follows the same matching/binding rules for concrete and
generic `T`, including pointer and reference forms admitted by constraints.
An existing reference retains its referent, authority and dependencies;
matching a non-reference value cannot manufacture a borrow of its slot.
Explicit formation must occur before matching when required.

The following is a semantic expansion, not proposed invocation grammar:

```text
Inspect(existingPlace)
    -> Inspect(explicit readonly reference to existingPlace)

Inspect(CreateValue())
    -> one initially vacant mutable binding in the existing lexical scope:
           init mut T temporary;
       at this argument's evaluation point:
           temporary = CreateValue();
           Inspect(ref temporary);
    -> reuse this binding on later evaluations; ordinary replacement applies
    -> its last live value remains through the supporting scope
```

The second expansion assigns at the original argument-evaluation point.
A fresh owning result follows ordinary initialization or replacement;
materialization does not require Copy. Merely borrowing an existing owner
neither copies nor moves it. A skipped evaluation creates no new value and
leaves any previously initialized value unchanged. A never-initialized binding
remains vacant, with no `T` cleanup. Evaluation is never hoisted to scope
entry. The local remains within the already existing enclosing lexical
scope, and block exit/unwind uses ordinary initialized/moved-state cleanup.
The [reusable-local expansion](lifetimes.md#repeated-evaluations-and-reusable-locals)
defines loop scopes, control transfers, old-value replacement, and final
cleanup. It does not allocate a retained local for every iteration.
For example, a reference returned from `Foo` may be used by `Baz` later in the
same scope if its declared dependencies permit:

```text
Foo(CreateValue()).Bar();
Baz(); // A valid returned reference may still refer to the hidden local.
```

This does not widen the lifetime of an existing borrowed place or the backing
dependencies inside `T`; capture, suspension, storage and escape remain subject
to the same declared contracts as explicit `ref<T>`. It adds no new implicit
noescape rule. Lowering may eliminate physical storage only if value, alias,
address/lifetime, rooting and cleanup behavior remains unchanged. This endpoint
is accepted; general source syntax, loan metadata and runtime implementation
are not thereby implemented.

### 1.2 Reference dependencies do not change

An implicit argument has the same parameter contract as its explicit readonly
reference counterpart:

- An ordinary no-retention parameter remains no-retention.
- A declared result relationship may return a reference to an eligible caller
  place. Its lifetime is bounded by that place, not automatically by the call.
- A reference into a materialized local remains bounded by that local's
  enclosing lexical scope; this does not widen its backing dependencies.
- Capture and suspension require the same explicitly permitted dependencies as
  ordinary references; the modifier grants no capture permission.
- Copying a Copy value out of a referenced slot preserves dependencies already
  carried by that value. Copying a span descriptor is not returning a reference
  to its descriptor slot.

Readonly is a restriction on this access path. It is not a snapshot, a promise
that aliases cannot mutate, or an exclusive loan. An owner/guard may independently
require exclusion of competing accesses; that requirement must be expressed
and checked for explicit and implicit references alike.

There is no new `implicit` lifetime bit in the semantic signature. Source-call
metadata records the convenience of argument adaptation; the reference's
access, permitted sources, capture, and result relationships carry the actual
safety contract. Explicit and implicit readonly-reference forms still cannot
distinguish overloads. The already accepted value-over-borrow preference for
ordinary `F(value)` is unchanged.

[Whole-source result and capture schemas](language/dependencies-and-views.md)
publish exact receiver/formal sources, retained destinations and backing-only
permissions. Binding occurs against the selected declaration and staged
arguments, not names in a callee body. Surviving stored state can retain a
capture after exceptional call completion; normal-output vacancy is not
rollback of ordinary receiver/holder mutation. Each relationship preserves
the existing invocation/completion/producer phase and cleanup ordering.

The [Core task ownership contract](language/task-responsibility.md) carries
these relationships through affine transfer and consuming await.
Responsibility, terminal observation, payload ownership and published output
cleanup are distinct; scope exit cannot silently abandon or join a task.

### 1.3 Evaluation and loan timing

The [ordinary execution contract](language/expressions-and-statements.md)
defines categories, unit values, explicit non-unit result discard, loop
transfers, and return completion. Dropping a result requires its ordinary
cleanup; neither `_` nor a `void` target permits ownership or effect erasure.

Precedence and associativity determine the expression tree; evaluation order
determines how that tree executes. For ordinary eager operators, evaluate the
left operand before the right operand and apply the operator after its
operands. Thus `A() + B() * C()` calls `A`, then `B`, then `C`, computes the
multiplication, and finally the addition. Precedence does not call `B` before
`A`. Short-circuit and conditional operators execute only their selected
operands; the rule does not turn them into eager evaluation.

Evaluate the callable/receiver, then argument expressions in source order.
Named arguments do not reorder effects into parameter-declaration order.
Place formation evaluates its receiver, indices, and required validation once
at that argument's position. Form the reference when the equivalent explicit
reference operation would form it. Reading through that reference in the
callee is not an early value snapshot taken during argument evaluation.

For a call conceptually like `Observe(place, ChangePlace())`, the answer is
therefore the same as explicitly forming the reference before evaluating
`ChangePlace()`. If ordinary aliasing permits the mutation, the later callee
read observes the changed storage. If the place's owner/loan contract excludes
the mutation, the call is invalid in both spellings. The compiler must not
silently substitute a copy, delay the reference, or invent a special two-phase
loan to make the implicit spelling succeed.

Ordinary writable access is not an exclusive-loan declaration. For
`target.Update(target.Read())`, where `Read` returns an independent scalar,
the inner read completes before the outer update begins. Deriving readonly
access from an existing writable reference does not itself conflict: both
paths can alias the same storage. No general reservation/activation protocol
is required merely to allow this call. Exclusive owner/guard contracts may
independently prohibit invalidation or overlapping access; they do not redefine
ordinary `ref<T>` or `write ref<T>`.

The outer receiver is still evaluated once before its arguments. Preserve
place-formation checks at their source evaluation point and invocation-only
checks, including a receiver null check when applicable, at invocation after
arguments. Do not expand a complex receiver into repeated evaluation or
reevaluate a captured class handle after an argument rebinds its source slot.
For an unchanged local struct receiver, the nested call has the effects of
reading its scalar result and then updating that same place.
The ABI may share an identical byref for the two calls when doing so preserves
evaluation, checks, access attenuation, lifetimes and dependencies. This
permits address commoning, not treating `Read` as pure or memoizing its result.
Implicit convenience syntax uses this same ordinary call/loan contract.

Completed owning value arguments are staged in evaluation order. If a later
argument fails before invocation, the caller cleans the staged owners on
defined unwinding; a moved source does not become live again. Ownership transfers
at the ordinary invocation boundary. Materialized borrowed temporaries remain
caller-owned. Explicit initialization destinations must already be vacant;
forming their loan does not destroy a live value. Reached omitted-output
adaptation [prepares its reusable hidden locals](language/calls-and-inference.md#hidden-destination-instances-and-retirement)
at the invocation boundary after argument staging and required invocation-only
receiver checks, not destructively during argument evaluation. Preparation
must not invalidate staged arguments, loans or other dependencies.

## 2. Outcome, initialization, and nullability are separate

### 2.1 The state contract

The following are different facts:

| Fact | Meaning |
|---|---|
| Status | The operation returned true, false, or a particular declared domain status |
| Initialization/publication | A live, complete value exists in an output destination; explicitly uninitialized means no live `T` value/lifetime |
| Presence/nullability | An initialized optional value is null or contains a value |
| Value relationship | An initialized value satisfies a range, length, backing, or other checked predicate |

Uninitialized storage has no live `T` value: it cannot be read, accessed as a
`T`, or cleaned up as a `T`. Its physical storage may still exist. Vacancy is
not null. An initialized nullable value containing null has a live value and
ordinary cleanup, even when its representation uses zero bits. A null test
cannot discover whether storage is initialized, because evaluating that test
already requires a readable value. GC-safe private zeroing is not
source-visible initialization.

The accepted inferred-binding direction makes that separation explicit:
`value is init` tests initialization, `value is var item` binds only an
initialized non-null payload, and `value is var? item` also permits an
initialized null. Neither inferred pattern matches vacant storage. These
checks use declared initialization facts before value access; they do not
turn vacancy into null or test bytes whose value is uninitialized.

For `bool TryGet(..., init ref<Customer?> value)`, true can publish either null
or a non-null customer; false may leave it uninitialized. An initialized null,
an initialized non-null value, and an uninitialized destination are distinct
states even if representations overlap. A declaration's postconditions state
which outcomes provide which facts. The implementation must construct every
promised live value; neither compiler nor runtime may manufacture a default.
Adapters must preserve status and initialization relationships or explicitly
declare a lossy mapping; no new nested-optional type rule is necessary.

### 2.2 Declared outcome relationships

| Declared output contract | True | False |
|---|---|---|
| Conditional success contract | Initialized; may be null if `T?` | Uninitialized |
| Success plus non-null refinement | Initialized, non-null | Uninitialized |
| Declared initialized-null failure outcome | Initialized, non-null | Initialized, null |

This table describes distinct contract choices, not defaults inferred from
method names. If an output may be live on one path and uninitialized on another,
the caller may carry that known relationship to the discriminator before
testing it. Once the discriminator is tested, flow analysis permits value access
or cleanup only on an initialized arm. An API cannot transfer an undiscriminated
maybe-live owner to the caller, and no caller may access or clean up a vacant
arm.

Postconditions are the normal source-authoring route for these outcome facts.
Their accepted expression form is `ensure <expr>`, where `<expr>` is an
ordinary Boolean expression and may use ordinary patterns and short-circuit
behavior. A predicate over an ordinary value such as `param > 0` requires
and therefore establishes that value's initialization. For an
initialization-reference parameter use `*param > 0`: `param is init` tests
the descriptor, not its destination. No redundant `*param is init` is needed
after a legally established referent-value predicate.
Source-body and path analysis must establish that every value read
by the predicate is legal, and must never read vacant storage to discover that
the predicate is false. A successful value predicate implies initialization.
Do not add a special `ensure-if` statement grammar or equivalent routine output
attributes. Named callables group `require` clauses before `ensure` clauses
after parameters and generic constraints; accessors place them after their
keyword. A direct expression-bodied property/indexer places getter clauses
before `=>`. Target-checked lambdas use the same grouping after parameters.
A top-level unparenthesized `=>` delimits the body; contract lambdas must be
grouped or nested in another expression delimiter. The
[source grammar](grammar-spec.md#31-source-file-structure-and-declarations)
defines these forms; correlated-output metadata remains specification work.

Within a declaration postcondition, contextual `return` observes that
declaration's result through its live destination, not a consumed source or
an entry snapshot. It is unavailable in preconditions and ordinary body
expressions. A nested lambda does not rebind it, and captures obey ordinary
lifetime and ownership rules. For an ordinary async callable it denotes the
completed payload, not its task handle. Instance constructors use receiver
initialization facts instead; static constructors use their owning group's
initialization facts. Producer phase mapping remains open.

The [selected async cursor contract](language/async-iteration.md) separately
attaches its init destination to the returned advancement operation's
completion Boolean. Invocation reserves caller storage; observing completed
true/live or false/failure/vacant establishes its availability after cleanup.
This inherited member relationship does not reinterpret an arbitrary
Task<bool> signature, non-async `ensure`, or ordinary call-return default.
Its pending state, exact operation/output correlation and forwarding
obligations are explicit, not body-inferred publication.

[Reference-output lending](language/lending-iteration.md) separately relates
the initialized inner descriptor of an exact init formal to permitted existing
element places, access, backing and advance/close invalidation. The ordinary
Boolean/output initialization relation alone supplies none of those facts.
Its cursor-bound default and checked published refinements retain actual
source/root/guard obligations. Async lending composes that element relationship
with the selected completion-output reservation, without changing arbitrary
Task-returning outputs or adopting a return-targeted receiver annotation.

Instance constructors publish a minimum set of guaranteed initialized properties
with clauses such as `ensure P is init`. Callers initialize the remainder
before publication. Declaration field/property initializers execute before
constructor preconditions, body and postconditions; caller initializer entries
follow the constructor, then independent completion validation precedes
publication. Constructor postconditions are not automatically rerun after
caller initialization; assignments invalidate affected facts under the
ordinary mutation/dependency rules. Init-property assignment may repeat during
unpublished construction, in source order with ordinary replacement/cleanup checks; it
is not permission to initialize an already live object. Removing a guarantee
breaks callers that relied on it; strengthening one can retain older callers'
explicit assignments. See the [construction rules](grammar-spec.md#510-properties-and-accessor-local-effects).

The language-versioned default for a boolean-success output contract is
initialized on `true` and uninitialized on `false`. For other result types,
outputs must be initialized on every normal return unless a declaration
postcondition explicitly states a conditional relationship. These defaults do
not come from method names.

Implementation restrictions and historical `out` profiles are
[informative implementation inventories](../implementation/output-profiles.md).
They do not narrow the canonical initialization-destination contract, remove
eligible complete generic types, or introduce an additional parameter mode.

### 2.3 Checking and consuming the contract

The body must prove that every promised output is completely initialized on
the relevant normal paths. A contract cannot initialize storage merely by
claiming it is initialized. Null/range assertions may retain runtime checks,
but the safety of evaluating those checks is a static obligation.

Postcondition checks are ordinary code in the declared postcondition phase.
False predicates and exceptions from predicate evaluation follow ordinary
failure/unwinding; completed body cleanup and `finally` blocks are not rerun,
and body effects are not transactionally rolled back. This does not define a
separate all-output publication transaction or authorize reads of vacant
storage. Initialization, alias access and ownership remain governed by their
ordinary checked contracts.

Caller flow keeps the correlation between a particular call's saved status and
its outputs under the required
[structural-flow rules](language/initialization-and-flow.md).
Reassigning the status, overwriting the destination, moving its
owner, or relevant aliasing effects invalidate the affected facts. Enum statuses
use declared predicates, including explicitly declared sets of success statuses;
neither enum zero nor member names supply a condition.

[Calls and inference](language/calls-and-inference.md) defines candidate
selection and source-equivalent execution of contextual outputs.
Output omission selects lifting in matching and value-producing `require`
contexts. Explicit destinations remain ordinary arguments; only omitted
eligible outputs participate in the projection, in declaration order.
For ordinary call-completion contracts, the explicit `init ref<T>` parameter
mode establishes eligibility; no additional attribute, registration, or
`Try` naming convention is required. Its complete `T`, declared normal-output
relations, ownership and dependencies must be checked and publishable.
The caller must be able to provide ordinary initially vacant local storage
of that type under the selected profile. Omission does not make a sizeless,
unsupported or otherwise ineligible destination storable.

This rule does not omit readonly/writable reference-value parameters,
required inputs, or destinations whose publication requires later task
completion or producer advancement. Ordinary calls and statement-form
Boolean `require` still supply their required destinations; only matching
and value-producing `require` request this projection.
Later-completion lifting needs its separate phase/ownership contract rather
than treating invocation as completion or implicitly joining a task.

Partial omission is permitted. With all destinations supplied, matching
examines the ordinary return value. Required inputs remain required, and
arm types do not select a channel or influence callable selection. This also
disambiguates Boolean payloads from Boolean status without new arm qualifiers
or a meaning change when a generic payload specializes to `bool`.

In an omission-permitting context, an ordinary form and an eligible
omitted-output form participate under the same supplied-argument ranking.
Omitted destinations do not provide comparison arguments or an extra
specificity advantage; neither form receives a preference merely for having
or avoiding hidden outputs. If ordinary argument quality, binding priorities
and declared specificity leave both tied, the call is ambiguous.
Return types and pattern arms do not break that tie.
Supplying a destination explicitly can select the output-taking declaration.
Outside these contexts, omitted-output forms do not participate.

Contextual structural patterns such as `(var x, _)`, `(_, var? y)` and
`(var x, var y)` evaluate one call and establish the selected outputs'
initialization/presence facts. `_` does not read or bind an ignored output;
live ignored owners remain subject to cleanup. Matching follows declared
availability, so initialized failure outputs may match. Boolean conditional
init contracts have true publication and false vacancy; enum and richer
statuses require their own checked per-output relations. Output availability
does not imply a successful status. Correlations with explicitly supplied
destinations remain available to caller flow.

Bare lifted `require` demands initialization of every lifted output and yields
those values with their complete optional/ownership types. An initialized
null remains a legitimate nullable value, not an implicit unwrap. There is no
separate default success/output group. An explicit pattern requirement, such
as `require (Read() is (var value, _));`, can request narrower availability or
non-nullness without requiring the ignored output to be initialized.
No vacant read, fabricated default, ordinary tuple of vacant slots, mandatory
library `Option`/`Result`, or general implicit conversion is introduced.
Eligibility follows the initialization-destination contract above.
Hidden destinations and saved observations are reusable ordinary locals in
the original supporting scope, not owners retired at full-expression or
`if` completion. Repeated invocation conditionally retires prior live
hidden values before passing vacant initialization destinations, preserving
ordinary dependency and cleanup checks. Skips retain old storage but do not
make it a newly evaluated output. Binding patterns remain Copy-only;
bare lifted requirements transfer owning outputs only after all required
initialization checks. Explicit destinations and body-level moves provide
selective unique extraction without a new move-pattern form.
With no omitted outputs, a value-form `require` instead checks its ordinary
Boolean operand and yields `void`/`()` on true. False uses the selected
requirement failure; the call's actual destination states are not rolled back.
A non-Boolean status requires an explicit predicate. This does not reinterpret
an initialized lifted Boolean payload as a status check.
[Runtime requirement failures](language/requirement-failures.md) add only
the failed-path selector/construction: explicit `require<E>`, the semantic
callable's RequireException recipe, then exact Core RequirementException.
Ordinary `new E()` uses zero supplied arguments only after failure. Constructor
failure propagates its actual event, without clearing initialized destinations,
replaying partial cleanup or retiring supporting-scope hidden owners early.
No failure type supplies inference evidence for the operand or its outputs.
[Core scalar failures](language/scalar-failures.md) select their own exact
mapped payloads; RequireException is not an ambient replacement for operand
overflow, zero division or invalid count. Failed result publication preserves
actual argument/output states, not transaction rollback.
[Clamp's published bound relationship](language/numeric-selection.md)
explicitly uses require<InvalidBoundsException> min <= max after ordinary
source-order staging. A caller default, outlined helper or physically moved
check cannot change its selected payload, precondition origin or failure
phase; valid clamping preserves the selected input's representation.
Richer-status contract encoding, later-completion lifting, object-backed
observation and concrete metadata remain separate work. Current executable
call/output support is unchanged.

[Call conformance cases](conformance/source-calls-and-inference.md) include
inference, ranking, destination eligibility, supporting scopes and extraction.

Changing a declared output-initialization relation can change caller
obligations and compatibility. Compare the published conditional facts and
ordinary ownership contract; do not infer an extra transactional publication
protocol from postcondition ordering.

## 3. Contracts bracket semantic body completion

### 3.1 The normal sequence

The confirmed model is:

```text
caller: evaluate and stage arguments
callee: prologue -> preconditions -> body -> postconditions -> epilogue
caller: receive published result/outputs and finish expression cleanup
```

This is a semantic order, not a requirement for a particular stack-frame
instruction sequence. Inlining, tail calls, AOT, and JIT must preserve it.
The assembly epilogue cannot hide user-observable destruction or callbacks
after a postcondition that callers are meant to rely on.

All preconditions execute in one isolated lexical scope; all postconditions
execute in another. Earlier clauses can establish pattern bindings for later
clauses in their own phase. Those names do not become implementation locals,
cross into the other phase, or become caller-visible declarations.
Phase locals and implicit materializations follow ordinary scope cleanup
before their phase completes. This is the effect of grouped source blocks
without requiring additional braces around declaration clauses.
Each runtime precondition can select E explicitly or use its originating
primary callable's RequireException/Core default. Physically checking a
clause in a caller, interface adapter or generated slot preserves that
semantic owner, ordinary zero-input construction and actual failure channel.
Runtime ensure uses fixed Core PostconditionException under the
[standard category contract](runtime/standard-failures.md#2-runtime-postconditions).
An actual predicate failure is not converted to that payload. Static proof
failure remains a diagnostic, not runtime construction.
The [binding/flow rules](language/initialization-and-flow.md#binding-pattern-declarations-and-scopes)
keep name scope independent of value or captured-type availability.

Phase cleanup cannot invalidate facts that the body or caller is entitled
to rely on. Remaining clauses and cleanup participate in ordinary effect
invalidation; published normal-return guarantees must hold after the complete
postcondition phase. A guarantee must be reestablished or proved to survive
those effects, otherwise the declaration diagnoses. Do not replay an
effectful predicate or extend its local's scope to repair the guarantee.

| Phase | Contract obligations |
|---|---|
| Argument staging | Evaluate once in source order; establish explicit references, staged values, and ordinary temporary lifetimes |
| Prologue | Establish frame/root/ownership responsibility and parameter storage before executing user contracts |
| Preconditions | Check published conditions in declaration order; body scopes have not been entered |
| Body | Perform initialization within the body, including local/output values; execute authored code and evaluate any return expression once; execute relevant `finally` blocks, then complete local-scope and untransferred owned-parameter cleanup |
| Postconditions | Check normal-return guarantees over the return value and surviving state available under ordinary ownership/lifetime rules, in declaration order |
| Epilogue | Retire frame/root bookkeeping and return under the ordinary return/failure contract |

On invocation, by-value owned arguments already belong to the callee before
its preconditions execute. A failed precondition therefore takes ordinary
callee-owned argument cleanup on a defined unwind, not transactional restoration
of the caller's moved variables. A `finally` inside the body has not been
entered and does not run for that precondition failure. If the selected policy
is fail-fast, no cleanup is promised.

Instance construction additionally establishes an unpublished receiver and
executes declaration field/property initializers before constructor
preconditions. Their acquired values retain partial-state cleanup obligations
on defined unwinding, even when the constructor body was never entered.
Static initialization likewise places grouped field initializers before
static-constructor preconditions/body/postconditions. Its runtime entry and
publication effects participate in the triggering operation's contract; it is
not an exemption from declared no-throw, blocking or other effect bounds.
The [static initialization contract](grammar-spec.md#static-initialization-and-load-scoped-storage)
separates completed-group guarantees from internal unpublished initialization
authority and from post-publication concurrent access.

### 3.2 Saved results and cleanup

Evaluate a return expression once. Its value remains available under ordinary
ownership and lifetime rules while the body's `finally` and scope cleanup
finish. A copied scalar result retains its computed value; a copied handle or
returned reference still observes its permitted backing and aliases. A finally
that changes backing can therefore affect a subsequent postcondition through
that reference without reevaluating the return expression.

For `void`, the saved logical result is `()`, including a normal implicit
fallthrough or `return;`. It remains a value for contract/result typing even
without payload bytes. A non-unit return expression cannot be silently
discarded to manufacture that result.

**Accepted ownership endpoint:** semantic body completion includes
initialization within the method body (including local and output values),
authored execution, relevant `finally` blocks, local-scope cleanup, and cleanup
of callee-owned parameters not transferred elsewhere.
These complete before postconditions. Preconditions and postconditions are
public declaration/signature contracts outside the authored method body. On a
successful return, no user-observable ownership cleanup is deferred to the
mechanical epilogue.

[Owning-field cleanup](language/ownership-and-cleanup.md) preserves actual
complete/partial state and the responsible holder through these phases.
Complete authored Cleanup precedes reverse-declaration fields; failed
construction unwinds reverse acquisition without incomplete-parent Cleanup.
Postcondition failure does not replay field/body cleanup or restore a
transferred child. A partial aggregate cannot supply a complete receiver/result.
Normally false ensure establishes a fresh PostconditionException at the
originating clause; a failed predicate retains its actual event/channel.
Neither vacates already live explicit outputs or publishes successful
completion.

Postconditions can inspect surviving values and results retained for the
declared contract. A moved or consumed parameter is unavailable through its
vacated source, and a destroyed value cannot be dereferenced merely because
its old bytes remain in the frame. A transferred result is observed through
its destination, not the consumed source. This phase ordering does not add a
transactional output-publication protocol.

The same applies to locks and leases: if the body releases a guard, a
postcondition cannot inspect its former protected mutable state without valid
access. State that may already change concurrently is not a stable output
guarantee. Check it inside the protected body, return a snapshot, or declare
an actual retained capability; do not implicitly extend user scopes.

**Accepted deferral: no entry snapshots or state-history facility in the initial
contract model, with no promise to add one later.** Initial postconditions
describe observable method-boundary facts: initialization, nullability, ranges,
and relationships to unchanged value inputs. These are genuine behavioral
guarantees callers may rely on, not merely optimizer hints. For example, the
following expresses contract intent, not settled source syntax:

```text
ensure *output is init; // output is an initialization-reference parameter.
ensure *output > 0;
ensure written <= capacity; // capacity is an unchanged value input
```

Historical relations such as “the count increased by one” require state-history
support and remain outside this initial model. Proving or folding a predicate
may optimize enforcement only while preserving the published guarantee.

Published clauses may mention only declarations visible at the method's
effective accessibility and expressions in the
[checked published subset](language/published-predicates-and-effects.md#published-predicate-subset).
A public `Count` abstraction may be referenced through its accessible checked
pure getter; a private representation invariant remains internal and cannot
become a hidden caller precondition. Caller facts come from published guarantees checked or proved
for the callee, never from private body inference; dependent facts are
invalidated by relevant mutation, alias effects or lifetime changes. Init
safety is static: never read vacant storage to decide whether it is initialized.
Reconsider a narrow explicit value capture only if concrete APIs justify its
copy, lifetime and allocation costs. Arbitrary `old(expr)`, graph snapshots
and historical mutable-state contracts are not part of the initial model.

### 3.3 Normal and exceptional exits

| Path | Run declaration postconditions? |
|---|---|
| Return expression and all body cleanup complete normally | Yes |
| Exception is caught inside the body, which later completes normally | Yes |
| Exception escapes the body, even after successful finally cleanup | No |
| Cleanup replaces a pending return with an escaping exception | No |
| Failure while checking a precondition | No |
| Failure while checking a postcondition | Ordinary failure/unwinding begins at the failed check; completed body cleanup and `finally` blocks are not rerun |
| Fail-fast, process termination, or a non-returning operation | No normal-return guarantee and no cleanup promise |

Searching the stack for a handler is not execution of a method's normal
epilogue. Neither handler search nor unwind itself runs `ensure`. A check after
the authored body is also outside that body's `try`/`catch`; only an enclosing
caller recovery scope could handle its propagated failure.

On checked propagation, unwind exited inner scopes to the next candidate
handler's still-live scope before evaluating that scope's catches and filters
in source order. Cleanup effects and any replacement failure precede selection;
ordinary causal chaining preserves the earlier cause. A false filter tests
the next catch, or continues outward unwinding if none matches. A successful
type/filter match enters the body without first unwinding past that candidate.
Filters use valid pending-failure and live-scope state, not references into
retired frames. Their side effects are not rolled back on false or failure,
and completed cleanup is never replayed to restart selection. Committing to a
handler is not a publication transaction for the exception or cleanup effects.
Fail-fast policy remains separate and promises no cleanup.

[Local failure inputs](language/local-failures.md) use nominal Exception
eligibility and exact complete subjects/witnesses. A reference catch borrows
the active payload; a named value catch requires ordinary Copy and does not
consume the propagation owner. Candidate bindings initialize before their
filters and retire on false/failure as well as handler exit. Bare rethrow
ends handler-scope borrows/locals before continuing the same responsible event;
copying or freshly throwing a payload is not provenance-preserving capture.

[Core CapturedException](language/captured-exceptions.md) instead owns that
same event after successful lexical no-argument Capture in a source handler
body. The source activation then has no responsibility for bare rethrow or
normal retirement; surviving catch borrows still end at their ordinary extent.
A named consuming `throw move captured;` stages the original event once and
ends affected inputs/locals before further selection. Authored cleanup failure
can replace that requested propagation while retaining its cause; it cannot
duplicate a transferred event or replay cleanup. Ordinary captured-owner
retirement is separately checked, synchronous and nonthrowing/nonsuspending.

The [contained foreign bridge](runtime/foreign-failure-bridge.md) preserves
this selection order by returning normally across each language boundary
before the receiving runtime raises. Its packet/cause/mapping contracts
do not equate foreign and Terrum exception objects or extend exited scopes.

Postconditions execute the checked predicate at the postcondition phase.
A false predicate fails there and follows ordinary failure/unwinding;
partial-operation or invalid-call-domain failure follows ordinary handling.
A selected pure call cannot escape an exception on its established valid
domain, but this is not proof that every enclosing check succeeds.
No transactional rollback
of body effects occurs, and completed body `finally` blocks are not rerun.
Any still-live values are handled by their ordinary ownership and failure
contracts; this does not introduce a separate candidate-cleanup or output
publication transaction.

An outer-body `static ensure` uses this same normal-completion boundary.
It proves all such paths; it is not registered only on paths that execute its
source location. A body `static require` remains a point obligation. Neither
automatically becomes a published declaration guarantee.

### 3.4 Async execution and producer phases

[Async execution](language/async-execution.md) establishes its responsible
operation before user preconditions/body. Failure before establishment is
synchronous; later escaping check/body/cleanup failure is failed completion,
even with inline execution and no suspension. Normal postconditions precede
successful completion publication. Scheduling and real capture lifetimes
retain their separate rules.

[Generated producers](language/generated-producers.md) instead create a named
affine cursor synchronously without running its body. Source require/ensure
and creation effects govern that invocation; contextual return is the cursor.
Generated advance/close declarations govern their own results/output and
completion phases. Yield retains entered body scopes and is not normal body
completion; early close does not run normal body-completion ensures.
No creation annotation implies pure/no-throw enumeration or later NoAlias.

## 4. A reproducible proof and evaluation foundation

### 4.1 Separate three operations

| Operation | Required result | Failure behavior |
|---|---|---|
| Ordinary executable contract | A runtime boolean unless sound analysis proves the check redundant | If not proved redundant, evaluate it at runtime; preserve required evaluation, effects and failures when eliminating a check |
| `static require` / `static ensure` | A proof established by the mandatory portable floor or a sound implementation extension | Disproved or not established is a compile error, in every build; never fall back to runtime |
| Required `const` evaluation / literal parser | A concrete target-semantic value, with successful declared publication | Invalid operation, unsupported effect, or exhausted budget is a compile error; never runtime fallback |

These are separate obligations: optional proof/optimization cannot replace an
ordinary runtime check unless it proves that check redundant while preserving
observable behavior; a required static obligation must be established or
compilation fails; required constant evaluation must produce the exact
target-semantic value. Sound proof extensions may accept additional static
programs above the guaranteed floor, but they do not change runtime semantics.

Where a source context permits runtime values or checks, an author may
explicitly choose an ordinary expression or executable `require` instead of
requiring static proof or constant evaluation. That changes the source
requirement; a compiler must never silently demote it. Ordinary expressions
may still be folded as sound optimization, and an ordinary `require` remains
runtime-enforced unless proven redundant. No such switch is available where a
compile-time value is inherently required (for example, layout, type or
metadata formation), or for structural lifetime and initialization safety.
Any notation used to illustrate the choice is not selected grammar.

For ordinary checked operations, the frontend should classify both a literal
zero divisor and a local provably containing zero through the same proof
machinery. Introducing a local must not hide a provably failing operation.
Required constant evaluation is an error; proven failure in an otherwise
runtime operation follows the agreed warning/promotable-to-error policy.
Unknown runtime input keeps its check. Implementation of this policy must
reconcile existing diagnostics explicitly, not silently reclassify every
current error as a warning.

### 4.2 Versioned portable proof floor

The [canonical versioned floor](language/proof-and-constant-evaluation.md#scalar-proof-facts)
defines exact scalar/Boolean facts, integer intervals and exclusions,
predicate refinement, local/branch/loop transfer, and conservative
invalidation. Its [work/state accounting](language/proof-and-constant-evaluation.md#proof-outcomes-and-accounting)
and portable minimum are independent of optimization level.
False, not established and exhausted remain distinct; required obligations
never become runtime checks. Sound beyond-floor analysis and larger supported
envelopes may accept more without changing source semantics.

The required [structural initialization and pattern-flow system](language/initialization-and-flow.md)
specifies local live/vacant joins, finite loop backedges, Boolean matching
edges, saved output-state relations, and conservative invalidation. Those
rules are ordinary source safety obligations, not optional numeric invariant
proofs or license to infer facts from private imported bodies.

Numeric induction, general relational solving, private-call inference and
alias-sensitive advanced proofs are outside the minimum. Actual target
arithmetic governs proofs: mathematical endpoint analysis is not permission
to remove a possible overflow, use host floating precision, or invent fixed
runtime NaN bits. A constant evaluator result is not a runtime call summary.
No proof-certificate format, physical metadata carrier, ABI or solver is
selected by these logical rules.

### 4.3 Effects and stable observations

The [published-predicate and effect chapter](language/published-predicates-and-effects.md)
defines the checked subset, logical read/effect records, input-state purity
and selected evaluation modes. `Pure` excludes observable effects, including
external mutation and escaping exception effects on calls satisfying declared
preconditions. Input values and state read through `this` or explicit inputs
are logical inputs; mutable ambient state is not. Immutable published data
is permitted with initialization, publication and lifetime dependencies.
No `Stable` or read-scope modifier is required.

Readonly access alone does not supply a purity contract or establish that
two observations have the same input state. Ordinary references are aliasable;
relevant writes, captures, replacement and retirement invalidate dependencies.
Disjointness is needed only when a proof actually relies on it and does not
make writes to pre-existing caller state pure. Initialization destinations
construct logical results, with their ordinary output-state, ownership and
backing obligations.
The optional [NoAlias parameter schema](language/published-predicates-and-effects.md#noalias-parameters)
specifies a checked invocation-wide separation requirement over actual
accessed mutable regions, with immutable sharing exempt. It does not make
ordinary references exclusive or supply an escaping separation guarantee.

Declaration predicates select checked pure-expression evaluation. Ordinary
body `require` remains general executable Boolean code.
Consumers use published identities, forms and complete dependencies, not a
method name or private imported body. The minimum numeric analysis retains
its [conservative boundary](language/proof-and-constant-evaluation.md#joins-loops-and-effects);
these contracts do not add general numeric call summaries.

Retain partial-operation, precondition and requirement failures unless their
absence is proved. Eliminating a known-true check is not permission to remove
argument evaluation, a needed root/loan, temporary cleanup, static initialization
or a possible failure. Purity does not prove termination or authorize
unconditional motion, duplication or reuse.

### 4.4 Deterministic constant execution

The [constant domain and semantic actions](language/proof-and-constant-evaluation.md#constant-execution-domain)
use target arithmetic, inline eligible values, private state and ordinary
references/cleanup, with admissibility on the executed path.
`[ConstEval]` is explicit source-required-evaluation opt-in and implies
checked pure-form support, not ordinary-form `NoThrow`, termination or
all-input constant success. `[Pure]` alone can authorize safe backend folding,
not source-required interpreter admission. Author-guarded ordinary effects
can be omitted in a selected pure/constant form through
[evaluation-mode queries](language/published-predicates-and-effects.md#evaluation-modes).
Known runtime-only operations on unselected paths do not run; a reachable
external effect cannot be excused from the pure-form guarantee.
Unknown/malformed body schemas still reject before use.
There is no ambient I/O, host-address or runtime-static initialization fallback.

An emitted constant additionally requires its target-profile `Blittable`
commitment, frozen Copy validity and admitted typed value recipe.
Private evaluator state and borrowed inputs need not themselves be emitted.
Typed scalar/field values, not host object bytes or padding, preserve the
values and representation observations for target materialization.

The selected [resource envelope](language/proof-and-constant-evaluation.md#portable-resource-envelope)
has deterministic semantic/copy/bulk charges and inclusive boundaries.
The same bound inputs, executed dependencies, target/value ABI and actual
envelope produce the same value or diagnostic independently of optimization,
AOT/JIT, caching or host arithmetic.

[Constant arithmetic NaNs](language/proof-and-constant-evaluation.md#deterministic-nans-and-frozen-results)
choose a positive quiet zero-payload value where source representation is
unspecified. Copies and explicit bit-preserving operations retain their
stronger contracts. This does not canonicalize runtime arithmetic or prove
its payload bits. Required failure/exhaustion diagnoses without fallback;
optional folding may instead decline.

### 4.5 Versioning and independent implementations

Identify the source language profile, versioned proof floor, target value
semantics, published contract/effect dependencies, and required evaluation
budget when needed for reproducibility. The canonical
[dependency contract](language/proof-and-constant-evaluation.md#dependencies-availability-and-admission)
requires those logical inputs for new execution; their physical records and
encodings are a separate metadata/wire decision. Caller-side required proofs use the
callee's published contract and do not depend on an imported implementation
body being present. Required constant evaluation that executes a declaration
still requires its constant-evaluable body and evaluated dependencies to be
available under that separate contract. The exact required body/identity
availability is specified logically; its persistent representation remains
separately gated.

Pure forms, evaluation modes, read/effect bounds and documented constant
results are published compatibility promises. An incompatible result change
can break consumers even when the replacement remains pure; arbitrary
semantic equivalence is not generally checked automatically. Preserve
declared latitude such as unspecified arithmetic NaN payloads rather than
silently freezing it. New execution and reuse retain the actual code,
value, target and initialization dependencies.

AOT, JIT, specialization, interface witnesses and replacement validation use
the same published contracts. Source compilation checks authored bodies
against their declared state and effect guarantees. Ordinary binary consumers
safely decode and check accessed structures/references, resolve retained
dependencies, check binding/contract compatibility and exposed caller-use
obligations, then rely on published
metadata contracts for external dependencies rather than re-proving their
implementations. A caller still proves its obligations from imported
preconditions, access, ownership and effect contracts. Unsupported rule
versions and malformed structures reject;
this default does not treat malformed bytes as trusted or require dependency
body inspection. Hand-authored IL that lies about an implementation is not
excluded by mandatory whole-program proof. Optional deeper audit tooling may
be considered later, with no v1 or all-behavior certification promise.
The [reader-trust contract](representation/reader-trust.md) permits selective
indexed reads without preliminary Body scans. Producers establish index,
region and semantic truth; natural access/ABI/authorization checks still
reject exposed invalidity. Table-specific references do not select their
target table.
Signing/provenance is a separate distribution concern; signer identity is not
semantic proof. These target contracts do not change the current bounded
reader/verifier behavior. Foreign declarations still enter through explicit
trusted boundaries with compatible contracts, not by silently weakening safe
consumers.

Changing the versioned floor changes its minimum guarantee and is an explicit
language/toolchain capability change. A sound implementation extension above
the current floor may accept additional programs without changing the floor
version or consumer contracts. Neither may silently reinterpret an unchanged
consumer's publication, lifetime, or arithmetic contract.

## 5. Conformance coverage

[Thread capabilities and protocol authority](runtime/sharing-and-handoff.md)
are retained compatibility promises too. Preserve conditional caller domains,
actual transfer/capture/release obligations, synchronization, reentrancy and
cleanup affinity. A trusted implementation body cannot silently strengthen
conditions or weaken those promises; unsupported required protocols reject.

The minimum conformance corpus should cover:

- Explicit/implicit reference-equivalent calls; value versus reference-returning
  properties; later same-scope use of returned references; scope-exit cleanup;
  conditional/short-circuit non-evaluation; returned dependencies; alias
  mutation and reentrancy; source-order named arguments; failed later
  arguments and cleanup.
- True-null versus false-vacant; declared false-null; status/output correlations;
  non-null types rejecting null annotations; owning false outputs; malformed
  or contradictory metadata; require lifting without collapsing presence.
- Precondition failures before body entry; returns through finally; finally
  mutating returned backing; internal versus external catches; failed
  postconditions; owned parameter/result cleanup; roots through every boundary.
- Equivalent direct/local proof facts; checked overflow, division, signed zero
  and NaN boundaries; effect/alias invalidation; and loops classified as
  not-established when required by the floor. The mandatory floor's expected
  outcomes must hold across compiler hosts and optimization levels for source
  and persisted modules; sound beyond-floor acceptance may vary.
- Required constant evaluation has exact target results and deterministic
  value/diagnostic outcomes for the same target profile and specified budget,
  including budget-boundary cases; target-width/representation differences
  receive their own expected results.
- Selected ordinary/pure/constant forms, guarded effects, aliased input reads,
  pure initialization results, published predicates, immutable data and
  compatible independent consumption follow the
  [effect corpus](conformance/source-published-predicates-and-effects.md).

## References and comparison boundaries

- [C# expression specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions):
  expression classification, operand evaluation, and precedence are separate
  concepts. It is prior art, not Terrum's ownership or temporary-lifetime rule.
- [C# parameter modifiers](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/method-parameters):
  value, reference, and readonly-reference calling conventions are distinct
  source semantics, independent of optimization.
- [C# nullable-analysis attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis):
  `MaybeNullWhen` and `NotNullWhen` communicate conditional null-state facts.
  They do not supply Terrum's vacant-storage/publication or owning-output
  cleanup protocol.
- [GitHub Flavored Markdown specification](https://github.github.com/gfm/) and
  [GitHub writing guide](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax):
  CommonMark plus GFM extensions supplies the prose baseline; GitHub site-level
  postprocessing is separate from Terrum's structured headings and mentions.
- [C# XML documentation](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/),
  [Rustdoc documentation guidance](https://doc.rust-lang.org/rustdoc/how-to-write-documentation.html)
  and [item links](https://doc.rust-lang.org/rustdoc/write-documentation/linking-to-items-by-name.html),
  and [Swift markup](https://developer.apple.com/library/archive/documentation/Xcode/Reference/xcode_markup_formatting_ref/MarkupSyntax.html):
  prior art for declaration attachment, structured fields and symbol resolution,
  not adoption of their complete source markup or implicit reference rules.
- Terrum's [lifetime defaults](lifetimes.md#declaration-defaults-exceptions-and-published-compatibility),
  [numeric contracts](conversions.md), and
  [module contracts](modules.md) provide the existing integration boundaries.
