# Language contracts: calls, outputs and checks

This document records accepted execution and checking contracts that refine
[language](language.md) and [lifetimes](lifetimes.md). It does not approve
candidate syntax, a proof budget or a new semantic-IL representation.
Unresolved source forms are in the [grammar proposal](proposals/grammar-spec.md)
and the remaining proof rules and budgets in the
[proof proposal](proposals/static-proof-and-constant-evaluation.md).
[Implementation](implementation.md) remains the executable support boundary;
concurrency follows the separately [accepted memory model](memory-model.md).
Implicit arguments expand to ordinary operations, conditional outputs carry
checked state facts, and executable contracts participate in ordinary
evaluation and cleanup. Convenience syntax does not create a second lifetime
system or a second arithmetic system for the prover.

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

Whether ordinary references eventually need reservation/activation remains
an [open grammar and loan question](proposals/grammar-spec.md).
Implicit convenience syntax cannot silently change the ordinary loan
contract.

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

Postconditions are the normal source-authoring route for these outcome facts.
Their accepted expression form is `ensure <expr>`, where `<expr>` is an
ordinary Boolean expression and may use ordinary patterns and short-circuit
behavior. A value predicate such as `param > 0` itself requires and therefore
establishes that `param` is initialized; no redundant `param is init` test is
required. Source-body and path analysis must establish that every value read
by the predicate is legal, and must never read vacant storage to discover that
the predicate is false. A successful value predicate implies initialization.
Do not add a special `ensure-if` statement grammar or equivalent routine output
attributes. Exact clause placement, grammar, and metadata representation
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

Postcondition checks are ordinary code in the declared postcondition phase.
False predicates and exceptions from predicate evaluation follow ordinary
failure/unwinding; completed body cleanup and `finally` blocks are not rerun,
and body effects are not transactionally rolled back. This does not define a
separate all-output publication transaction or authorize reads of vacant
storage. Initialization, alias access and ownership remain governed by their
ordinary checked contracts.

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

### 3.2 Saved results and cleanup

Evaluate a return expression once. Its value remains available under ordinary
ownership and lifetime rules while the body's `finally` and scope cleanup
finish. A copied scalar result retains its computed value; a copied handle or
returned reference still observes its permitted backing and aliases. A finally
that changes backing can therefore affect a subsequent postcondition through
that reference without reevaluating the return expression.

**Accepted ownership endpoint:** semantic body completion includes
initialization within the method body (including local and output values),
authored execution, relevant `finally` blocks, local-scope cleanup, and cleanup
of callee-owned parameters not transferred elsewhere.
These complete before postconditions. Preconditions and postconditions are
public declaration/signature contracts outside the authored method body. On a
successful return, no user-observable ownership cleanup is deferred to the
mechanical epilogue.

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
ensure output is init;
ensure output > 0;
ensure written <= capacity; // capacity is an unchanged value input
```

Historical relations such as “the count increased by one” require state-history
support and remain outside this initial model. Proving or folding a predicate
may optimize enforcement only while preserving the published guarantee.

Published clauses may mention only declarations visible at the method's
effective accessibility and expressions permitted by the ordinary contract
language. A public `Count` abstraction may be referenced if it is ordinarily
accessible and evaluable there; a private representation invariant remains
internal and cannot become a hidden caller precondition. This adds no getter-
purity syntax. Caller facts come from published guarantees checked or proved
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

Postconditions are ordinary code in the postcondition phase. A false predicate
fails there and follows ordinary failure/unwinding; predicate evaluation that
throws propagates through ordinary failure handling. No transactional rollback
of body effects occurs, and completed body `finally` blocks are not rerun.
Any still-live values are handled by their ordinary ownership and failure
contracts; this does not introduce a separate candidate-cleanup or output
publication transaction.

An outer-body `static ensure` uses this same normal-completion boundary.
It proves all such paths; it is not registered only on paths that execute its
source location. A body `static require` remains a point obligation. Neither
automatically becomes a published declaration guarantee.

For an asynchronous declaration, the same normal-completion contract applies
before successful completion is published; [asynchronous scheduling and
lifetimes](language.md#storage-concurrency-entry-and-boundaries) retain their
separate rules.

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

**Accepted policy:** standardize a versioned portable floor of minimum
supported proof rules/complexity and a minimum guaranteed resource budget.
The floor promises that conforming compilers can establish its required
proofs independent of optimization level; it does not require identical
acceptance beyond the floor or impose a fixed maximum proof budget. An
implementation may soundly prove more complex obligations, use larger
budgets, and accept additional programs. A required proof that is disproved or
not established by that compiler's supported analysis is a compile error, not
a runtime fallback. Diagnostics distinguish false from not established
(including complexity or analysis-limit failure where appropriate).

Comparisons must use actual language arithmetic. Rewriting an overflow-prone
integer expression as an unbounded mathematical expression is not proof of
its executable safety. Float predicates cannot borrow integer identities:
NaNs, signed zero, rounding, and the prohibition on implicit reassociation/FMA
still apply. Start with exact constant floating evaluation only when its
target evaluator is specified; otherwise classify such proof requests unknown.

The minimum catalog and conformance vectors are still to specify; the
[proof proposal](proposals/static-proof-and-constant-evaluation.md#portable-proof-floor)
collects candidates, not a required floor. Sound beyond-floor analysis does
not change the versioned minimum or source semantics. No proof-certificate
format, metadata carrier, ABI or solver is selected.

### 4.3 Effects and stable observations

Published predicate evaluation needs checked effect and dependency contracts;
readonly access alone does not prove an observation stable or repeatable.
Opaque imports can publish checked facts without exposing bodies, but their
constant execution requires an eligible body or separately specified trusted
intrinsic. Exact effect footprints and the initial admissible predicate subset
remain [proposed](proposals/static-proof-and-constant-evaluation.md#effect-footprints-and-observations).

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
the evaluator. General managed graphs, native resources, and
identity-bearing host objects need separate representation contracts; none
acquires an automatic constant-evaluation fallback.

The exact work units and resource ceilings are not approved. The
[evaluator proposal](proposals/static-proof-and-constant-evaluation.md#deterministic-evaluator-and-proposed-budgets)
records candidate budgets and accounting; no implementation may treat a
candidate number as the language limit.

Budget exhaustion is unproven/unevaluated and diagnoses at the requesting static
or constant context. It does not authorize runtime fallback, a guessed value,
or discarding required checks. Optional compile-time optimization can instead
decline to optimize because the source did not require it.

### 4.5 Versioning and independent implementations

Identify the source language profile, versioned proof floor, target value
semantics, published contract/effect dependencies, and required evaluation
budget when needed for reproducibility. Whether those identifiers/budgets are
recorded in producer artifacts, and how they are encoded, is a separate
metadata/wire decision not selected here. Caller-side required proofs use the
callee's published contract and do not depend on an imported implementation
body being present. Required constant evaluation that executes a declaration
still requires its constant-evaluable body and evaluated dependencies to be
available under that separate contract; the availability/persistence mechanism
remains to specify.

AOT, JIT, specialization, interface witnesses and replacement validation use
the same published contracts. Source compilation checks authored bodies
against their declared state and effect guarantees. Ordinary binary consumers
safely decode and structurally validate artifacts, resolve references, check
binding/contract compatibility and validate caller use, then rely on published
metadata contracts for external dependencies rather than re-proving their
implementations. A caller still proves its obligations from imported
preconditions, access, ownership and effect contracts. Unsupported rule
versions and malformed structures reject;
this default does not treat malformed bytes as trusted or require dependency
body inspection. Hand-authored IL that lies about an implementation is not
excluded by mandatory whole-program proof. Optional deeper audit tooling may
be considered later, with no v1 or all-behavior certification promise.
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
