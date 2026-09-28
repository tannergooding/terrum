# Calls, conditional outputs, and executable contracts

**Proposal for review, not implementation authorization.** This document follows
the accepted directions in [language](language.md) and [lifetimes](lifetimes.md).
It records the clarified intent and proposes the remaining rules needed to make
that intent consistent across source, verification, separate compilation, and
execution. Proposed syntax is illustrative unless already accepted elsewhere.
[Implementation](implementation.md) remains the executable support boundary.
Concurrency follows the separately [accepted memory model](memory-model.md).
The [pending-decision index](language-library-review.md) collects the remaining
approvals across documents. Confirmed direction below is not awaiting approval
again; the proposed refinements and their [status checklist](#6-approval-and-conformance-checklist)
identify the remaining decisions.

The central recommendation is one semantic model: implicit arguments expand to
ordinary operations, conditional outputs carry ordinary checked state facts,
and contracts participate in ordinary evaluation and cleanup. There should not
be a second lifetime system for convenience syntax or a second arithmetic
system for the prover.

## Direction and decisions

| Area | Confirmed direction | Recommendation requiring review |
|---|---|---|
| Calls | `implicit ref<T>` is an ordinary readonly `ref<T>`; existing places are borrowed; value arguments initialize a hidden local at the original argument-evaluation point in the current enclosing lexical scope; evaluation is left-to-right within expression structure | Specify place formation and general reference/loan metadata; do not introduce a special reservation/activation rule or lifetime-only scope for implicit arguments |
| Conditional outputs | Postconditions are the normal authoring route, using ordinary pattern/expression composition without a routine output-attribute system; initialized, uninitialized and initialized-null states are distinct and correlated with the declared discriminator | Specify exact guard/pattern spelling and metadata for the accepted state relationships |
| Contract timing | Prologue, preconditions, method body (including body-local initialization, authored execution, `finally`, local cleanup and untransferred owned-parameter cleanup), postconditions, epilogue; preconditions/postconditions are public declaration contracts outside the body; exceptional departure has no normal-return postcondition | Specify exact output/result publication and postcondition-failure cleanup mechanics, and decide whether entry snapshots are supported |
| Proofs and evaluation | Static checks are compile-time-only; implementation choices must not silently change the contract | Version a mandatory conservative proof profile and deterministic evaluator, including their resource-accounting rules |
| Memory | Explicit checked opt-in for sharing, not an assumption that arbitrary aliasable objects are thread-safe | Accepted separately; see the [memory model](memory-model.md) for its specification deliverables and target-profile work |
| Documentation comments | Added as a topic for discussion; no documentation-comment syntax is selected | Structured API documentation with symbol-checked references and generated views of declared contracts, without making prose a source of semantics |

The accepted implicit-loan rule in [language](language.md) and
[lifetimes](lifetimes.md#parameter-only-implicit-readonly-loans) is ordinary
readonly-reference equivalence, including declared result dependencies.
Materialized arguments use an ordinary local in the current enclosing lexical
scope, initialized at the argument's evaluation point; they add no
lifetime-only region, unconditional call-scoped/noescape restriction, or
permission to widen existing dependencies. General reference/loan syntax,
metadata and implementation remain scheduled. The other proposed contract and
proof mechanisms below remain under review.

## 1. One call and reference model

### 1.1 Places, values, and materialization

A **place** is existing storage with a checked identity, type, access, lifetime,
and backing/ownership dependencies. A **value expression** computes a value;
it need not identify storage that the caller can borrow. These terms describe
semantic categories, not a new public type family.

| Argument expression | Meaning for `implicit ref<T>` |
|---|---|
| Eligible local, parameter, field, or element place of the required type | Form an ordinary readonly reference to that place |
| Reference-returning access | Use the returned place and its published dependencies, as for explicit reference passing |
| Value-returning call, property, operator, or explicit conversion | Evaluate once into a local temporary, then form an ordinary readonly reference to that temporary |
| Place whose ordinary reference use is invalid | Diagnose the same access/lifetime conflict; do not silently copy it to evade the conflict |

The distinction is the declaration's result category, not whether its source
looks like a field or an accessor. A value-returning property is not a reference
to hidden property storage. No new implicit conversion chain is introduced.
The complete `T` is preserved; a readonly reference to a handle slot does not
remove write authority from the handle value it contains.

The following is a semantic expansion, not proposed invocation grammar:

```text
Inspect(existingPlace)
    -> Inspect(explicit readonly reference to existingPlace)

Inspect(CreateValue())
    -> at this argument's evaluation point in the current lexical scope:
           initialize hidden local T temporary = CreateValue();
           Inspect(explicit readonly reference to temporary);
    -> temporary remains an ordinary local through the enclosing scope
```

The second expansion initializes a local at the original argument-evaluation
point. A fresh owning result follows ordinary initialization; materialization
does not require Copy. Merely borrowing an existing owner neither copies nor
moves it. If control flow skips evaluating the argument, no local is
initialized and there is no owner to clean up. Evaluation is never hoisted to
scope entry. The local remains within the already existing enclosing lexical
scope, and block exit/unwind uses ordinary initialized/moved-state cleanup.
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

### 1.3 Evaluation and loan timing

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

**Recommendation:** no distinct loan reservation/activation mechanism in this
increment. If a future ordinary reference or mutating-receiver contract needs
one, specify its observable timing, conflicts, reentrancy, and equivalent
explicit operations first. Syntactic convenience must not select it implicitly.

Completed owning value arguments are staged in evaluation order. If a later
argument fails before invocation, the caller cleans the staged owners on
defined unwinding; a moved source does not become live again. Ownership transfers
at the ordinary invocation boundary. Materialized borrowed temporaries remain
caller-owned. Initialization destinations are prepared at the same boundary
as explicit calls, not destructively cleared while earlier arguments are still
being evaluated. These rules preserve the current bounded staging behavior.

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

Postconditions are the normal source-authoring route for these outcome facts;
ordinary patterns and expressions compose the initialization tests with status
and value predicates. Do not define equivalent routine output attributes as a
parallel authoring mechanism. Illustrations such as `return is !init` and
`ensure return is init when true` describe intent only; neither is selected
source grammar. Exact guard/pattern syntax and its metadata representation
remain specification work.

The language-versioned default for a boolean-success output contract is
initialized on `true` and uninitialized on `false`. For other result types,
outputs must be initialized on every normal return unless a declaration
postcondition explicitly states a conditional relationship. These defaults do
not come from method names.

### 2.3 Checking and consuming the contract

The body must prove that every promised output is completely initialized on
the relevant normal paths. A contract cannot initialize storage merely by
claiming it is initialized. Null/range assertions may retain runtime checks,
but the safety of evaluating those checks is a static obligation.

**Pending proposal:** keep output publication provisional until normal
completion and all applicable postconditions succeed; do not expose
not-yet-published initialization storage through another alias. If a
postcondition fails, clean saved owning results and output candidates on
defined unwind, without rolling back unrelated writes or external effects.
Predicate exceptions propagate rather than becoming the false-predicate
failure. This concrete recommendation remains open for review and is separate
from the accepted body-cleanup endpoint below.

Caller flow keeps the correlation between a particular call's saved status and
its outputs. Reassigning the status, overwriting the destination, moving its
owner, or relevant aliasing effects invalidate the affected facts. Enum statuses
use declared predicates, including explicitly declared sets of success statuses;
neither enum zero nor member names supply a condition.

The `require TryGet(...)` lifting form still tests the declared success
condition, not whether some output happened to be initialized. On true it
extracts the designated output with its full optional/ownership type. True-null
remains a legitimate optional value if the API permits it. On false, even an
API that provides an initialized failure output still fails the requirement;
the temporary's cleanup remains due. Multiple outputs and failure payloads
remain explicit initially. No vacant read or manufactured default is needed.

Changing a publication outcome is not necessarily a harmless strengthening.
Publishing a new owning output on false adds a caller cleanup responsibility.
Compatibility must compare the publication protocol as well as logical
postconditions; a replacement needs an explicit adapter or a compatible
versioned representation, not just a theorem that its predicate is stronger.

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

| Phase | Proposed obligations |
|---|---|
| Argument staging | Evaluate once in source order; establish explicit references, staged values, and ordinary temporary lifetimes |
| Prologue | Establish frame/root/ownership responsibility and parameter storage before executing user contracts |
| Preconditions | Check published conditions in declaration order; body scopes have not been entered |
| Body | Perform body-local initialization and authored execution, save a return candidate, execute relevant `finally` blocks, then complete local-scope and untransferred owned-parameter cleanup |
| Postconditions | Check normal-return guarantees over the saved candidate and surviving state, in declaration order |
| Epilogue | Retire frame/root bookkeeping and return; the exact publication step and postcondition-failure handling remain separately unspecified |

On invocation, by-value owned arguments already belong to the callee before
its preconditions execute. A failed precondition therefore takes ordinary
callee-owned argument cleanup on a defined unwind, not transactional restoration
of the caller's moved variables. A `finally` inside the body has not been
entered and does not run for that precondition failure. If the selected policy
is fail-fast, no cleanup is promised.

### 3.2 Saved results and cleanup

Evaluate a return expression once into checked result storage. It remains
rooted and owned while the body's `finally` and scope cleanup finish. A copied
scalar result is a snapshot; a copied handle or returned reference still
observes its permitted backing and aliases. A finally that changes backing can
therefore affect a subsequent postcondition through that reference without
reevaluating the return expression.

**Accepted ownership endpoint:** semantic body completion includes body-local
initialization and authored execution, relevant `finally` blocks, local-scope
cleanup, and cleanup of callee-owned parameters not transferred elsewhere.
These complete before postconditions. Preconditions and postconditions are
public declaration/signature contracts outside the authored method body. On a
successful return, no user-observable ownership cleanup is deferred to the
mechanical epilogue.

Postconditions can inspect surviving values and results retained for the
declared contract. A moved or consumed parameter is unavailable through its
vacated source, and a destroyed value cannot be dereferenced merely because
its old bytes remain in the frame. A transferred result is observed through
its destination, not the consumed source. Exact output/result publication and
failure cleanup remain open; this decision does not select their machinery.

The same applies to locks and leases: if the body releases a guard, a
postcondition cannot inspect its former protected mutable state without valid
access. State that may already change concurrently is not a stable output
guarantee. Check it inside the protected body, return a snapshot, or declare
an actual retained capability; do not implicitly extend user scopes.

An entry-state facility is useful but not yet accepted syntax. Recommend
explicit, statically enumerated snapshots captured after successful
preconditions and before the body. An `old(...)`-like operation must identify
what is copied and retain its ordinary dependencies. It must not clone an
arbitrary graph, move a unique value to satisfy a predicate, or turn a handle
copy into an immutable snapshot of its referent. A count can be captured as a
scalar; comparing a disposed owner's later property with its entry value cannot.
Allocation, owned snapshots, and general heap-history capture are outside the
initial proposal.

### 3.3 Normal and exceptional exits

| Path | Run declaration postconditions? |
|---|---|
| Return expression and all body cleanup complete normally | Yes |
| Exception is caught inside the body, which later completes normally | Yes |
| Exception escapes the body, even after successful finally cleanup | No |
| Cleanup replaces a pending return with an escaping exception | No |
| Failure while checking a precondition | No |
| Failure while checking a postcondition | The pending proposal is defined failure departure with candidate cleanup; see below |
| Fail-fast, process termination, or a non-returning operation | No normal-return guarantee and no cleanup promise |

Searching the stack for a handler is not execution of a method's normal
epilogue. Neither handler search nor unwind itself runs `ensure`. A check after
the authored body is also outside that body's `try`/`catch`; only an enclosing
caller recovery scope could handle its propagated failure.

**Pending recommendation:** a failed postcondition takes defined failure
departure, cleans saved owning results and unpublished outputs, does not rerun
completed body `finally` blocks, and does not roll back body side effects.
Exceptions from predicate evaluation propagate unchanged rather than becoming
the false-predicate failure. These failure mechanics have not been accepted;
the accepted cleanup endpoint requires only that ordinary body and
owned-parameter cleanup has completed before normal postconditions run.

An outer-body `static ensure` uses this same normal-completion boundary.
It proves all such paths; it is not registered only on paths that execute its
source location. A body `static require` remains a point obligation. Neither
automatically becomes a published declaration guarantee.

Async eventually needs the analogous boundary at operation-body completion,
before publishing task success, not merely when returning the startup handle.
Recommend that model, but keep startup/completion contracts and coroutine
lowering separately scheduled; this document does not select task start policy,
an async ABI, or permission for stack loans to survive suspension.

## 4. A reproducible proof and evaluation foundation

### 4.1 Separate three operations

| Operation | Required result | Failure behavior |
|---|---|---|
| Ordinary executable contract | A runtime boolean, unless safely eliminated | False takes the declared runtime failure; proven failure also receives the agreed frontend diagnostic |
| `static require` / `static ensure` | Proof under the selected language proof profile | False or unproven is a compile error, in every build |
| Required `const` evaluation / literal parser | A concrete target-semantic value, with successful declared publication | Invalid operation, unsupported effect, or exhausted budget is a compile error; never runtime fallback |

An optional optimization is none of these. Optimizer cleverness cannot change
whether required source is legal.

For ordinary checked operations, the frontend should classify both a literal
zero divisor and a local provably containing zero through the same proof
machinery. Introducing a local must not hide a provably failing operation.
Required constant evaluation is an error; proven failure in an otherwise
runtime operation follows the agreed warning/promotable-to-error policy.
Unknown runtime input keeps its check. Implementation of this policy must
reconcile existing diagnostics explicitly, not silently reclassify every
current error as a warning.

### 4.2 Mandatory conservative proof profile

**Recommendation:** define a versioned portable profile, rather than accepting
whatever the current compiler's optimizer or theorem prover can establish.
Every conforming implementation of that profile must reach the same
proved/disproved/unknown classification for its required checks.

The first profile should deliberately be small:

| Component | Proposed baseline |
|---|---|
| Values | Target-width scalar constants, immutable value identities, booleans, optional presence, initialization/vacancy, and ownership state |
| Numeric facts | Closed intervals; exact target operations on constants; conservative interval transfer only where defined; explicit checks retain their overflow/failure edges |
| Branches | Refine supported built-in comparisons on their true/false edges; use interval hull and intersection of must-facts at joins |
| Loops | Before analyzing a cyclic region, forget numeric/alias facts for values and storage that region may change; preserve unaffected facts and refine again on branch edges |
| Calls | Use the selected declaration's checked pre/postconditions and effect footprint, not arbitrary inspection of a replaceable implementation |
| Aliases | Invalidate facts for potentially affected places before relying on later loads; preserve captured values, not mutable-storage observations |
| Unsupported reasoning | Produce unknown; do not assume a predicate, unroll according to optimizer heuristics, or consult a nondeterministic solver |

Loop handling is intentionally conservative: no implicit induction, guessed
invariant, or machine-speed-dependent unrolling in the first profile. Richer
loop-invariant syntax and proofs require a new specified capability. Public
checked facts may still let a caller prove a result without seeing the loop.

Comparisons must use actual language arithmetic. Rewriting an overflow-prone
integer expression as an unbounded mathematical expression is not proof of
its executable safety. Float predicates cannot borrow integer identities:
NaNs, signed zero, rounding, and the prohibition on implicit reassociation/FMA
still apply. Start with exact constant floating evaluation only when its
target evaluator is specified; otherwise classify such proof requests unknown.

Before implementation, this table needs a machine-checkable transfer-rule
catalog: canonical pre-optimization input form, comparison orientation, integer
division/remainder/shift rules, joins, cycle detection, and deterministic
traversal. Implementations cannot fill gaps with private acceptance rules.
The catalog and conformance vectors are part of the profile, not a backend
optimization pass.

More capable tools may offer explanatory suggestions. They must not cause the
same declared profile to accept a required proof another conforming compiler
rejects. A future stronger profile or portable proof-certificate extension
can add reasoning explicitly. Optimizers remain free to perform stronger
sound analysis after source validation, without changing evaluation effects.

### 4.3 Effects and stable observations

Published predicate evaluation needs more than an attribute called `Pure`.
The checker needs a declared effect/dependency footprint covering reads,
writes, captures, allocation, failure, blocking, suspension, synchronization,
and lifetime/ownership changes. The spelling of that footprint remains open.

The initial contract/proof subset should admit scalar operations and checked
readonly observations with valid stable access. It should exclude I/O, ambient
state, volatile/atomic observations, synchronization, suspension, and
user-visible mutation. A readonly member can still observe mutable storage;
readonly alone does not prove repeatability or stability.

Opaque imported calls can publish checked facts through validated declarations.
They cannot be constant-executed without an eligible implementation or a
specified trusted intrinsic. An unannotated call conservatively invalidates
facts about storage it might affect; it is not a magic clobber of independent
copied scalar values.

If a predicate can fault, retain that failure behavior unless it too is proven
absent. Eliminating a known-true check is not permission to remove argument
evaluation, an effectful getter, a needed root/loan, temporary cleanup, or a
possible exception. Ordinary body `require` continues to accept general
executable boolean expressions; it is not restricted to the published subset.

### 4.4 Deterministic constant execution

Execute a specified, unoptimized semantic instruction set with target types
and arithmetic, not host-language overflow, host `nint`, host floating defaults,
culture, filesystem, clock, addresses, or process state. The same source,
declared dependencies, target profile, and evaluation budget must produce the
same value or diagnostic regardless of AOT/JIT choice or optimization level.

Private mutation of evaluator-owned temporary storage is compatible with
determinism. It does not authorize ambient writes or escaping a pointer into
the evaluator. The first serializable results should use defined value encodings;
general managed graphs, native resources, or identity-bearing host objects
need separate representation contracts, not an automatic fallback.

Required execution has explicit, deterministic resource accounting:

| Resource | Proposed unit / initial candidate ceiling |
|---|---|
| Evaluation work | 1,048,576 semantic steps; charge each defined scalar operation, branch, call entry, and element/code-unit operation |
| Call depth | 256 active evaluator frames |
| Temporary storage | 16 MiB of live abstract payload storage, measured without host allocator overhead |
| Serialized result | 1 MiB in the target-defined value encoding |

These are review candidates, not measured limits or existing compiler behavior.
Validate them against representative literal parsers before accepting defaults.
A build can explicitly request a different supported budget; it becomes a
recorded input, not an undocumented compiler setting.

Aggregate/bulk intrinsics need a profile-defined expansion or exact cost
function; treating an arbitrarily expensive native helper as one scalar step
would defeat the budget. Allocation accounting must include the model's
defined payload/state rather than accidentally depending on a host GC.
Memoization may avoid work physically, but must preserve the specified budget
outcome and diagnostics. Wall-clock timeout is a tool cancellation/resource
failure, not an alternative language proof result.

Budget exhaustion is unproven/unevaluated and diagnoses at the requesting static
or constant context. It does not authorize runtime fallback, a guessed value,
or discarding required checks. Optional compile-time optimization can instead
decline to optimize because the source did not require it.

### 4.5 Versioning and independent implementations

Record the source language profile, proof/evaluator rule version, target value
semantics, published contract/effect dependencies, and required evaluation
budget in the producer's semantic artifacts. Source acceptance must not depend
on whether an imported body happens to be present today.

AOT, JIT, specialization, interface witnesses, and replacement validation use
the same checked contracts. The verifier must reject forged state guarantees
or unsupported rule versions; an attribute or producer assertion is not a proof.
Foreign declarations enter through an explicit trusted boundary with compatible
contracts, not by silently weakening safe consumers.

A new proof profile can establish more programs, but selecting it is an
explicit language/toolchain capability change. It must not silently reinterpret
an unchanged consumer's publication, lifetime, or arithmetic contract.

## 5. Documentation comments

**Open proposal for a subsequent review.** Documentation comments need an
explicit language/tooling contract rather than a formatter-specific convention.
The following questions are scheduled for discussion; they do not select syntax
or authorize implementation.

Recommend a distinguished documentation-comment form with readable prose and
a small structured vocabulary for API documentation. Markdown is a candidate
for prose and examples; C#-style XML is an alternative to assess, not an
inherited requirement. Ordinary line/block comments should remain ordinary
trivia, and documentation must not alter program behavior.

| Topic | Proposed direction / question to settle |
|---|---|
| Delimiters and attachment | Choose `///`, a distinguished block form, or both; define attachment across attributes, blank lines, multiple declarations, and generated/partial declarations |
| Prose and structured content | Choose Markdown versus XML, or a precisely defined combination; cover summaries, remarks, parameters, type parameters, return values, conditional outputs, examples, and links without competing representations of the same field |
| Symbol references | Check parameter/type-parameter names and API references against actual declarations; represent overloads, qualified types, and generic members without guessing from display text |
| Declared contracts | Generate documentation for access, ownership, lifetime, publication, effects, pre/postconditions, and selected failure behavior from checked declaration metadata; prose explains intent, costs, and usage |
| Inheritance and forwarding | Decide explicit documentation inheritance, interface-member documentation, aliases, and compatibility forwarders; inherit from a selected declaration rather than an arbitrary name match |
| Diagnostics | Define malformed markup, unresolved references, stale parameter names, and missing public documentation separately; choose warning/build-policy behavior without changing executable semantics |
| Examples | Support explicitly marked compilable examples with a declared language/target context; distinguish complete programs, fragments, and design-only pseudocode; execution is opt-in, not a side effect of rendering comments |
| Distribution and tooling | Specify stable symbol identities, source versus compiled-package documentation, visibility/trimming, and IDE/site rendering; determine whether documentation is a separate artifact rather than retained runtime data |

The key boundary is **declared semantics versus explanation**. A comment saying
"non-null on true" must not grant initialization or nullability facts.
Likewise, an exception description cannot authorize catching, change a
requirement's failure selection, or imply cleanup after fail-fast. The
corresponding language contracts remain authoritative. Generated API
documentation should expose those contracts so authors do not have to maintain
an independent annotation system inside comments.

Doc inheritance must preserve that boundary: inheriting an interface's prose
does not import extra effects or strengthen an implementation's guarantees.
An implementation-specific note can explain behavior only within the checked
contract it actually publishes. Automatically generated sections and authored
prose need a defined presentation order and conflict policy.

Documentation extraction should be deterministic and must not execute embedded
code, fetch remote includes, or expose private declarations merely to resolve
a link. Explicit documentation builds may enable separately configured example
checks or dependencies. Changing prose alone should not invalidate executable
ABI compatibility or change optimization decisions; documentation artifacts
can have their own content identities.

Before implementation, settle the source grammar, semantic reference format,
attachment/inheritance rules, and package representation together. Conformance
coverage should include exact comment preservation, declaration attachment,
renamed parameters, overloaded/generic links, inherited documentation,
unavailable dependencies, visibility filtering, and reproducible example
validation.

## 6. Approval and conformance checklist

| Decision | Status | Contract or recommended choice |
|---|---|---|
| Reference adaptation | Accepted baseline | Ordinary readonly-reference semantics; no implicit-only escape prohibition |
| Materialized temporary endpoint | Accepted design | Hidden local belongs to the current enclosing lexical scope and is initialized at the argument-evaluation point; no lifetime-only scope, hoisting, or widening of existing dependencies |
| Loan timing | Accepted equivalence | Same as explicitly written reference operations; convenience syntax does not introduce a second loan model |
| Conditional output state | Accepted semantics | Live, complete initialization is distinct from uninitialized storage and initialized null; callers retain a checked relationship to the declared discriminator and cannot access or clean up a vacant arm |
| Conditional output authoring | Accepted direction; exact grammar remains work | Postconditions are the normal authoring route; ordinary patterns/expressions compose initialization, status and value facts. Do not add equivalent routine output attributes as a parallel mechanism |
| Undiscriminated maybe-live owner | Not permitted | A possibly live owner must remain correlated with a checked discriminator; a caller cannot receive, access or clean up a maybe-live owner without that relationship |
| Language-versioned output defaults | Accepted | Boolean-success output initializes on `true` and remains uninitialized on `false`; other result types initialize on every normal return unless a declaration postcondition specifies a conditional relationship |
| Output contract syntax and metadata | Specification work, not policy review | Formalize exact guard/pattern spelling and encode correlated initialization facts; preserve the current bounded bool/out behavior until coordinated migration |
| Normal-return ownership endpoint | Accepted | Body includes body-local initialization/execution, `finally`, local cleanup and untransferred owned-parameter cleanup before postconditions; no successful-return ownership cleanup is deferred to the epilogue |
| Consumed parameter observations | Accepted boundary; snapshots remain open | Moved/consumed/destroyed values are unavailable through their former source; surviving values and retained results remain available to applicable checks |
| Entry snapshots | Needs review of snapshot facility | Decide whether any values may be captured after preconditions and before the body; no implicit graph snapshot |
| Output publication and postcondition failure | Specification/review work | Exact publication point, candidate cleanup and predicate-failure behavior remain open; the cleanup endpoint does not select this machinery |
| Proof acceptance | Needs review | Mandatory versioned conservative profile, independent of optimizer sophistication |
| Constant-evaluation budget | Needs review of accounting contract | Semantic accounting with declared limits; candidate defaults require parser-workload validation |
| Documentation comments | Needs review | Discuss a distinguished comment form, prose format, checked symbol links, declaration-derived contract documentation, inheritance, and packaging before selecting syntax |

Approval of a remaining recommendation should be followed by canonical documentation changes and
scheduled implementation, not immediate execution of the illustrative grammar.
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
- Equivalent direct/local constant facts; checked overflow, division, signed
  zero and NaN boundaries; effect/alias invalidation; loops classified unknown;
  exact budget-limit cases; identical fixed-target profile results across
  compiler hosts, optimization levels, source and persisted modules, with
  separate expected results where target widths or representations differ.

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
- Terrum's [lifetime defaults](lifetimes.md#declaration-defaults-exceptions-and-published-compatibility),
  [numeric contracts](conversions.md), and
  [module contracts](modules.md) provide the existing integration boundaries.
