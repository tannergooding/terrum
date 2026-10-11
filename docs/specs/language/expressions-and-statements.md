# Values, expressions, and statement execution

This chapter defines ordinary expression categories, zero-data values,
result consumption, and statement completion. [Source productions](../grammar-spec.md)
define syntax; [names and binding](names-and-binding.md) select declarations;
[structural flow](initialization-and-flow.md) checks value availability.
An admitted expression still needs the selected operation's type, access,
ownership, effect, and lifetime contracts.
The [built-in scalar matrix](primitive-operators.md) supplies exact
primitive operand/results, count controls, comparisons and accessor updates;
it does not make a value getter a borrowable place.

## Expression categories

Classification precedes any use of an expression. Grouping preserves the
enclosed category; it cannot manufacture a place, value, or callable.

| Category | Meaning and eligible uses |
|---|---|
| Value | A complete typed result, including the zero-data `void` value. Use requires initialization and the applicable copy/transfer contract. |
| Place | Typed storage selected without reading its value. It can be tested for initialization, read, initialized, assigned, or borrowed only under the permissions for that operation. Not every place has an exposable data address. |
| Callable group | A selected declaration group, not a value or an arbitrary collection of overloads. Invocation or target-checked callable formation must resolve it under the ordinary applicability rules. |
| Type or namespace receiver | A lookup receiver for qualification, type inspection, construction, or a selected type call. It is not an ordinary runtime value. |
| Contextual structural projection | A matching/require projection of eligible omitted outputs, retaining each output's state and contracts. It is not an ordinary tuple created by reading vacant slots. |

A non-constant local or field denotes its place. Using it as an ordinary value reads its
live value; Copy types may copy, while unique values require an eligible
explicit transfer. A value-returning property or indexer invokes its accessor
and produces a value, not a borrowable backing place. A declared reference
result instead produces its descriptor; `*r` explicitly projects the place.
Property/indexer assignment uses an eligible setter/init operation and is
not reference formation through a value getter.
A named constant denotes its frozen value, not a place or an implicitly
addressable constant-data region. Permitted reference use follows ordinary
[materialization](../lifetimes.md#parameter-only-implicit-readonly-loans), including its
supporting scope and retained dependencies.

A live [lending iteration alias](lending-iteration.md#mode-selection-and-element-bindings)
denotes the original element T place with its selected immediate access,
not a descriptor slot or copied value. Its separate binding availability and
loan must survive each use; ending the binding releases that loan without
destroying the element. Ordinary reference-valued locals retain explicit
indirection, including by-value loop items of ref<T>.

[Result and capture relationships](dependencies-and-views.md) preserve exact
source instances, versions and backing through ordinary value use, wrapping,
copy/transfer and statement retirement. A by-value result's hidden ABI storage
does not create a loan of a caller source. Ending a descriptor releases its
own loan, not the referent, owner or surviving copies; no implicit lifetime
extension makes an otherwise invalid return/capture usable.

`ref place`, descriptor indirection, and `->` retain the
[reference rules](references.md). An uninitialized place is not a value,
even when it has zero data. A computed result cannot be assigned or borrowed
merely because another expression has the same type.
Callable groups need a checked target before becoming callable values;
`_ = Method;` supplies no such target. Likewise `_ = TypeName;` cannot
discard a type as if it were a runtime value.

## The zero-data `void` value

`void` is a first-class Copy value type with exactly one value, `()`.
It is inhabited, not an internal bottom type, an unknown type, or a spelling
for arbitrary ignored data. `default<void>` produces `()`.
Ordinary value positions admit it: locals, parameters, fields, tuple
elements, results, and eligible generic arguments.
An unrelated zero-data nominal type remains that type, not `void`; payload
size alone grants neither unit conversion nor implicit discard.

```terrum
void token = ();
Consume(()); // Consume has one void parameter and returns void.
(void, int32) pair = ((), int32(1));
```

`var token = ();` infers `void`. A `void` parameter still contributes one
logical parameter to arity, overload identity, named/default argument
binding, and metadata. `F()` and `F(())` are not the same call merely
because a target passes neither a payload register nor a payload stack slot
for `()`.

A `void` place follows ordinary initialization and lifetime rules:

```terrum
init void token;
// token is init is false; reading token is not permitted.
token = ();
_ = token;
token = _;
```

Vacancy is not `()`. Neither a compiler nor a runtime can manufacture a
live value from the absence of payload bytes. A logical initialization-state
bit, if needed, is not part of the unit value's payload.
An ordinary fixed binding cannot be reassigned after its lifetime ends;
`init void` can initialize proven-vacant storage, and `init mut void`
also permits replacement. These permissions remain meaningful even though
every live value equals `()`.

Equality of two live `void` values is true; inequality is false. Evaluate
both producing expressions in the ordinary order before obtaining that
result. Zero data is not permission to remove calls, checks, or failures.
There are no numeric arithmetic or ordering operations on `void`.

The logical place has no exposable payload address. `ref token` cannot
form a `ref<void>` merely because `token` is live; a `void` field likewise
does not expose a byte address. Aggregate storage and foreign opaque-pointer
contracts remain distinct. [Data layout](../data-layout-and-abi.md#zero-data-values-and-void)
separates logical value identity from payload representation.

An optional/response containing `void` can distinguish absence from a
present `()`; absence is not another unit value. `Task<void>` has a
zero-data completion result, not erasure of an arbitrary `Task<T>`.
The explicitly registered encoding-erased `TextSpan<void>` remains a
[family specialization](../type-families.md#text-views);
it is not a sequence of unit-sized code units or a universal `Type<void>`
projection.

## Using results and explicit discard

Every normally produced non-`void` result needs a use, destination, transfer,
binding, or explicit discard. An ordinary expression statement is legal only
when its complete result type is `void`, or the expression itself has no
normal value/completion under its checked contract.
No implicit conversion from arbitrary `T` to `void` supplies this rule.

```terrum
Notify();                    // Notify returns void.
_ = TryRemove(key);           // Deliberately ignore its bool result.
_ = count + int32(1);
_ = CreateOwner();            // Deliberately create and clean its fresh owner.
own<Resource> owner = CreateOwner();
```

Bare `TryRemove(key);`, `CreateOwner();`, `int32(1);`, and `count++;`
diagnose when they produce non-`void` results. Use `_ = count++;` when its
old value is intentionally unused, or an ordinary assignment/update statement
such as `count += int32(1);`. A conditional, switch, or `await` expression
does not acquire an implicit discard because its syntax is effectful.
The rule also applies when a generic result is not known to be `void`.
Specialization cannot silently make an invalid generic body valid.

`_ = expression;` evaluates one ordinary value expression exactly once and
then retires its unused result under its ordinary cleanup contract.
For a fresh owner, complete construction precedes cleanup at that statement's
expression boundary; failure cleans only obligations actually acquired.
Discarding a named unique owner needs explicit `move`, not an implicit copy:
`_ = move owner;` transfers and cleans the value while vacating its source.
Outstanding dependencies, operation-completion responsibility, roots, and
affinity still apply. `_` cannot make an illegal destruction or task
abandonment valid and does not implicitly detach, join, suspend, or collect.
[Core Task<T>](task-responsibility.md) owns that completion responsibility.
Ordinary await consumes a fresh task, or an explicitly moved named task,
and produces exact T. A pending/unobserved task cannot be made disposable
by `_`; authored cleanup or an explicit responsible transfer is required
on every exit that would otherwise abandon it.

[Owning-field retirement](ownership-and-cleanup.md) distinguishes a complete
value from partial acquired state. A complete owner runs its selected Cleanup
then automatic reverse-declaration field retirement; an eligible partial
structural owner retires only live fields. Explicit discard cannot bypass
the same dependency, protocol or complete-receiver requirements.
`name = _` can end an eligible named partial structural owner directly;
`_ = move name` cannot transfer an incomplete aggregate as a complete value.

| Form | Use it for | It does not mean |
|---|---|---|
| `void`, `()` | A genuine zero-data type/value, including generic composition or a zero-data result | Discard an arbitrary `T` or hide an ownership obligation |
| `_ = expression;` | Explicitly evaluate and ignore a typed result with its cleanup | Declare a local, assign `()`, or suppress effects/failure |
| `name = _;` | End that named local's live value and leave its storage vacant | Assign a wildcard/unit/default value or remove the name |
| `_` in a pattern | Explicitly ignore that pattern position without reading/binding it | Cancel a live ignored owner's cleanup |
| `_` in an eligible generic argument | Request checked inference for that position | Supply `void`, an existential value, or a runtime wildcard |

`void token = ReadInt();`, `return ReadInt();` from a `void` callable,
and assigning `_` as an ordinary expression diagnose. Explicit discard is
available even for `void`, but `Notify();` already consumes a genuine unit
result. Introducing a unit local merely to ignore some other result is not
an alternative to `_ = ...`.

An expression-bodied callable/lambda consumes its expression as the declared
logical result. A `void` target accepts `()` or another genuine unit-valued
expression, not an implicit discard of a non-`void` expression.
The named callable's explicit discard-body form, or a block containing
`_ = expression;`, instead completes with `()`.
Statement-bodied lambdas follow the same explicit-discard rule.
Assignments, discards, and checks are statements, not arbitrary assignment
expressions admitted into operands.

## Evaluation and expression boundaries

Evaluate ordinary eager operands left-to-right. Evaluate a callable/receiver
before its arguments, and arguments in source order, not formal-parameter
order. Form and validate a target place once before its assignment's
right-hand side; commit under the selected assignment/initialization contract.
Property/indexer assignment instead stages its receiver/index arguments and
right-hand side for the selected accessor invocation, preserving that
operation's invocation-only checks.
Compound updates likewise select the target once; their selected operation
and completed write cannot be replayed to discover a discarded result.
[Call staging](../language-contracts.md#13-evaluation-and-loan-timing)
preserves receiver identity, validation timing, and partial argument cleanup.

`&&`, `||`, conditional, coalescing, and optional access evaluate only
their specified reached operands. Tuple/aggregate and deconstruction order,
builder capture/drain order, and checked operations keep their canonical
contracts; syntax does not grant purity or rollback.

The full-expression boundary finishes the evaluation owned by one statement,
initializer, condition, or selected result expression. An untransferred
anonymous result retires at that boundary after its required uses, not at
an arbitrary SSA last use. Successfully bound/transferred values instead
follow their destination's lifetime. A saved return result survives body
cleanup through the declared postcondition/publication phase.

A construct retaining an observation must retain it through its specified
uses. A switch subject remains available through ordered arm selection and
the selected arm; an iteration's sequence/iterator state follows its header
scope through close. These are bounded locals/observations, not retained
collections of earlier evaluations.
An [implicit-reference materialization](../lifetimes.md#repeated-evaluations-and-reusable-locals)
is explicitly a supporting-scope local, not an anonymous result dropped at
the enclosing expression's end. Discard/condition rules do not shorten it,
keep its superseded values alive, or erase dependencies of a returned view.

## Conditions and ordinary statement completion

`if`, loop conditions, switch guards, catch filters, ordinary Boolean
`require`, and executable contract predicates require a checked primitive
`bool` value. A nullable value needs its ordinary checked presence/extraction
before it can supply that value. Integers, references, handles, and unit
values have no implicit truthiness.
The accepted paired `true`/`false` operator direction does not itself supply
condition conversion. Its registration/applicability must be selected before
it can participate in a condition.

A block enters its scope, executes reached statements in order, and on exit
performs the applicable cleanup exactly once. Completion is normal,
an enclosing-loop/switch transfer, result return, or failure; suspension
retains the outstanding continuation and obligations rather than completing
the block. An empty statement performs no operation.
Unreachable code does not acquire undeclared names, type conversions,
or authority; it is not permission to admit an otherwise malformed form.

Local/deconstruction declarations, assignment, discard, and lifetime-end
statements use their specified publication and cleanup rules.
An `if` evaluates its condition once and enters only its selected branch.
A statement switch evaluates its subject once, checks arms in order, and
normally completes after its selected block without fallthrough.
Its explicit cross-arm transfer retains the
[target and cleanup restrictions](../grammar-spec.md#33-statements-and-local-declarations).

Ordinary `require predicate;` consumes its Boolean predicate as a check,
not an ignored Boolean expression statement. The
[value-form Boolean check](calls-and-inference.md#boolean-requirements-and-logical-unit)
likewise evaluates once and yields `void`/`()` on true, not a Boolean result.
Without omitted outputs, it checks the ordinary predicate even when every
destination was explicitly supplied. Matching/value-producing
output projections instead retain their
[output-lifting rules](../language-contracts.md#23-checking-and-consuming-the-contract):
a projected non-unit value must be used or explicitly discarded.
The statement form does not silently request an omitted output projection
and throw away its values.
Those [contextual outputs](calls-and-inference.md#omitted-outputs-and-retained-observations)
have reusable supporting-scope locals, not full-expression owner lifetimes.
Ending a copied pattern view does not retire a separate hidden owner.
Unique outputs require ordinary transfers after availability checks, not
an implicit move merely to match a pattern or test a guard.

`try`/`catch`/`finally` use the selected incremental unwind, filter, and
failure-chaining rules. A `defer` registers only when reached; its expression
or block evaluates on exit, with reverse registration order, not a snapshot
of operands at registration. Deferred expressions obey the same expression-statement result rule.
A normally completing deferred non-unit computation needs a block with
explicit discard.
Deferred reads and captured dependencies must remain valid on every reached
execution of the action. Control cannot leave a cleanup action.
`unsafe` and policy scopes do not change statement result types or authorize
missing initialization, implicit discard, or a different cleanup endpoint.

### Ordinary loops

| Construct | Entry and normal repeated execution | `continue` | `break` |
|---|---|---|---|
| `while` | Condition, then body when true, then condition again | Exit body scopes, then evaluate condition | Exit the loop without another condition |
| `do` | Body once, then condition; repeat body when true | Exit body scopes, then evaluate condition | Exit without evaluating the condition |
| `for` | Enter header; initialize once; condition; body; step; condition again | Exit body scopes, then step and condition | Exit without step or another condition; clean header |
| Synchronous/async iteration | Evaluate sequence once; acquire iterator; advance; enter body only for an available item | End body/item obligations before the next advance | Leave through the required iterator close and header cleanup |

An omitted ordinary `for` condition is true. Omitted initialization/step
performs no operation. Expression initializers/steps obey the same result
rule as expression statements; `_ = expression` is the explicit header
discard without an internal semicolon. Assignments remain statement-level
updates with no implicit value result.

Bare transfers select the innermost enclosing loop. A label selects its
eligible enclosing loop, not a switch, arbitrary block, or future statement.
Leaving intervening bodies/headers performs their cleanup; labelled
`continue` then enters the selected loop's continuation point without
executing an exited inner loop's step.
Body reentry starts a new braced-scope activation. Header/supporting-scope
locals retain the [selected lifetime and flow boundaries](initialization-and-flow.md).
Iteration acquire/advance/close, failure, and asynchronous close retain their
registered contracts; cancellation alone is not completion.

### Results, return, and fallthrough

On every normally completing invocation, a non-`void` callable must publish
one complete value of its declared logical result type under the ordinary
conversion/ownership rules. A normal path reaching its closing brace without
that value diagnoses. `return;` is not a default-valued return for `T`.
Throwing and structurally nonterminating paths have no normal completion;
they do not need or fabricate a result.

A `void` callable's normal fallthrough and `return;` complete with `()`.
`return ();` is also valid. `return expression;` consumes a genuine result
expression, so a non-unit expression does not become valid merely because
the callable returns `void`.
Constructors/accessors and [producer return/yield](generated-producers.md)
keep their distinct completion/publication contracts; a constructor's
`return;` cannot publish an incompletely initialized receiver. Producer
creation returns its named cursor, while its lazy body completes with
return unit/fallthrough and yields in the selected output type.
Yield does not exit entered lexical scopes or run normal-completion ensures.

Evaluate a returned expression once and retain its selected result.
Exit relevant scopes and execute authored `finally`/deferred actions and
body ownership cleanup before postconditions. A cleanup failure replaces the
pending normal return under ordinary failure/chaining, and discharges any
still-owned unpublished result without replaying completed cleanup.
For async declarations, result checking uses the registered completion
payload, not an implicit conversion of some `Task<T>` to `Task<void>`.
A no-return guarantee removes a normal edge but does not imply NoThrow,
weaken source typing, or waive failure cleanup.

### No-return contracts

A checked `[DoesNotReturn]` is applicable to a callable with any declared
logical result type. It promises that the actual invocation cannot complete
normally: returning its declared value, returning unit or normally falling
through violates the promise. Escaping failure and structural divergence have
no normal edge; `[NoThrow]` is an independent bound.

The declared result remains the call expression's ordinary type. No public
bottom type, conversion from `void` or implicit non-unit discard is introduced.
An expression-bodied `func<void>` cannot therefore adapt an `int32` no-return
call merely because its result is never delivered. Source typing, binding and
validation still apply to unreachable code.

The promise applies to the invocation, not a later operation that it returns.
A task/cursor factory, including an async call that establishes and returns
a responsible Task, normally completes that call even if subsequent work
never completes or throws. Generated operation contracts keep their exact
identities and execution extents. Failure/cleanup edges remain; removing a
published no-return guarantee breaks retained callers relying on its absent
normal continuation.

[Conformance cases](../conformance/source-values-and-execution.md) specify
value positions, explicit result handling, and observable control-flow order.
The [clause cases](../conformance/il-contract-clauses.md#no-return-signatures-flow-and-calls)
also specify no-return applicability without changing those typing rules.
