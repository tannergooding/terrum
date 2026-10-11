# Calls and generic inference

This chapter owns ordinary source-call applicability, finite generic inference,
argument ranking, and omitted-output adaptation. [Names and binding](names-and-binding.md)
selects the callable group first; [complete types](types-and-normalization.md)
supplies identity, substitution, and constructor eligibility.
[Reference formation](references.md) and [call contracts](../language-contracts.md)
govern places, argument staging, initialization, and dependencies.
No phase executes candidate bodies or searches for an implementation type.

[Dependency-source binding](dependencies-and-views.md) maps the selected
receiver/formals to staged arguments and the exact result/retained destination.
Every permitted source constrains the call; a private branch cannot remove
one. Member-contract ForwardsBacking requirements are checked after normal
complete-type/witness selection. They create no inference evidence, conversion
preference or constraint-only overload, and failed capture admission is not
repaired by a copied descriptor, hidden owner or candidate-body inspection.

[Core tasks](task-responsibility.md) transfer completion responsibility through
ordinary by-value staging, returns and retained holders. A later argument's
failure must not strand an earlier staged task. Every consuming callee must
settle or preserve/transfer its affine inputs on normal and exceptional exits;
a by-value signature does not automatically authorize task abandonment.

[Owning-field contracts](ownership-and-cleanup.md) also govern a child
transferred into staged arguments or saved results. Later failure cleans
only its actual responsible holder, without restoring the old field.
An incomplete aggregate is not ordinary T input/result; structural field
state supplies no new inference evidence or conversion.

[Thread capabilities and protocol calls](../runtime/sharing-and-handoff.md)
also require their published conditional facts and actual authority at use.
Neither private specialization nor a capability condition supplies new
inference evidence or an overload preference. Staging/failure preserves
already adopted guard, handoff and cleanup holders without sender revival.

These rules apply to named calls, supported callable values, and constructors
after declaration selection. A constructor is not a type-call conversion;
`Target(expression)` retains its [destination/source-owned conversion rules](../grammar-spec.md#513-operators-assignments-and-expression-gaps).
Operator protocols retain their separately declared operand/result contracts.
An unsupported family, builder, representation, or execution feature does not
become supported because source inference produces a complete term.

[Runtime `require<E>`](requirement-failures.md#2-ordinary-zero-input-construction)
uses these ordinary constructor rules for `new E()` with zero supplied
arguments. Defaults/empty params may participate when ordinarily eligible;
the failure type supplies no operand/output inference evidence, constructor
capability, hidden argument, factory or alternate binding retry.

## Argument mapping and call forms

Resolve the accessible callable group, receiver/provider, and statically visible
labels before applicability. Competing imported groups do not merge through
argument types. A failed invocation does not retry a hidden type name, another
lookup level, inaccessible declaration, or arbitrary extension provider.
Eligible ordinary members and extensions retain their selected lookup rules.
Substitute the already-closed declaring type before inferring method formals.
Where an admitted callable/extension contract declares a logical receiver
parameter, its actual receiver contributes the same permitted typed evidence
and binding quality. It is not a written argument that shifts source labels
or positions. No private receiver field/capture supplies another type argument.

Map supplied arguments before filling omissions. A positional argument names
its declaration position; an in-position label permits later positionals,
but an out-of-position label requires subsequent arguments to be named.
Duplicate/unknown labels reject. Do not search for the next unfilled slot,
skip an initialization parameter to bind an ordinary expression, or evaluate
arguments in parameter order. Nontrailing omissions need appropriate labels.

| Form | Mapping obligation |
|---|---|
| Ordinary value | Complete parameter type, existing value binding, and its copy/transfer contract |
| `implicit ref<T>` | Ordinary explicit-reference-equivalent formation; exact stored `T`, no snapshot or invented loan protocol |
| `init ref<T>` | Explicit `init` argument designates vacant storage of exact complete `T`; mode cannot be supplied by an ordinary value |
| Ordinary default | Selected published Copy constant when that position is unsupplied; no default for reference/initialization loans |
| Normal `params` | One argument binds the declared collection through an admitted direct binding |
| Expanded `params` | Corresponding arguments bind elements of a new eligible collection, with ordinary construction and copy/move rules |
| Omitted initialization destination | Only in the matching/value-producing `require` contexts below; required ordinary inputs remain required |

For one declaration, use normal `params` form when applicable; otherwise consider
its expanded form. Explicit generic arguments can make the normal collection
inapplicable while permitting an expansion. Expanded elements occupy new slots,
not aliases of caller locals. A `params` parameter is still by value with its
declared collection type, not a reference mode or type pack.
Collection construction requires its published builder/storage contract.
In expanded form, zero or more positional arguments at/after the final
collection position correspond to its ordered elements. This exception does
not shift fixed positions or fill omitted inputs. A label naming the `params`
parameter supplies the whole collection in normal form; it does not name
one virtual element. In-position labels on preceding fixed parameters still
permit following positional expanded elements.

[Generated producer calls](generated-producers.md) bind their complete named
cursor result and ordinary input arguments before body generation. Creation
captures staged inputs once but does not execute the body; init-reference
creation formals cannot be deferred to later advance. A marked cursor shell
has no ordinary default/type-call/new constructor: its associated producer,
not same-named member discovery, creates its initial frame.

[Core Capture](captured-exceptions.md#2-lexical-source-context) is a direct
registered zero-parameter invocation with a compiler-known lexical source
handler, not an ordinary supplied/default argument or contextual return
inference source. Type aliases preserve its exact identity; function-group,
fnptr or helper calls do not inherit that context. Its fresh affine result
uses ordinary staging/transfer and carries the actual event dependencies.
A helper accepting an explicit CapturedException can forward that owner
without being allowed to Capture the caller's handler event.

Each syntactically present generic argument position is explicit evidence or
a selector: a type/constant argument pins it, `_` requires inference, and
`default` selects its declared default. A present generic list excludes
nongeneric candidates. Functions do not acquire the type-name lookup rule's
exact-arity preference; each candidate must accept the supplied prefix and
resolve its remaining formals through the rules below.

## Finite candidate inference

Use only the selected declaration, published constructor/witness contracts,
supplied expressions, and available caller type facts. A formal's identity is
its owning declaration and ordinal. Maintain its unresolved or established
complete type/constant term; never introduce an unbounded type variable search.

Typed inference proceeds in rounds. At a round's start, use established terms
to identify every enabled typed, structural and callable evidence source.
Gather all such evidence before establishing any of that round's unresolved
formals; combine it independently of source order and establish the resulting
terms together. Sources waiting for an input become enabled in a later round.
Later evidence checks an established term rather than weakening/reinferring it.
Each successful round resolves a formal or a pinned forward dependency;
when no progress is possible, proceed to the bounded fallback below.
There is no oscillating provisional access type or body-retyping search.

### Explicit selection and typed evidence

Check explicit argument kinds and substitute their complete terms.
An explicit `default` pins the published earlier-formal expression, not a
permission to infer a different value. Resolve its forward dependencies as
earlier formals become established; do not invert constant arithmetic or
associated/representation projections to invent those dependencies.
Its known injective constructor structure can participate in forward matching
while dependent leaves remain unresolved; the selected default itself is not
replaced by inference.

Collect all available typed evidence before choosing an inferred access form.
Intrinsic typed values, typed places, ordinary typed expressions, supported
signature evidence, and already-established formals contribute. Numeric,
character, and registered contextual literal tokens supply no default type.
The mandatory unit value contributes `void` with its logical position.

Distinguish two kinds of evidence:

- **Exact evidence** comes from invariant stored types, initialization
  destinations, and ordered arguments inside injective nominal/reference/
  pointer/owning/tuple/callable shapes. Those complete arguments do not undergo
  recursively inferred covariance or access erasure.
- **Value-binding evidence** for a by-value formal may combine only the
  already-permitted immediate read/write access of the same normalized stem.
  The stem retains representation, definition, inner arguments, constants,
  pin and other qualifications, and optional presence.

An exact term pins the inferred formal and must satisfy every value-binding
obligation. Conflicting exact terms reject. Without exact evidence, infer the
strongest common permitted immediate access: all writable inputs retain write;
a readonly/writable mixture uses the readonly form. All other parts of the
normalized stem must agree. Do not search numeric promotions, common bases,
boxes, adapters, or different constructor arguments.
Independent copy/transfer checks can still reject the resulting value binding.

For example, by-value `Choose<T>(T left, T right)` infers `write C` from
two writable `C` handles, but `C` from a writable/readonly mixture.
`Use<T>(T value, ref<T> storage)` instead has exact evidence from `storage`.
Readonly `C` storage can accept a writable by-value handle through attenuation;
writable `C` storage cannot accept a readonly handle by upgrading it.
`ref<write C>` never becomes `ref<C>`.

Collect evidence independent of argument order. An explicit pinned type is
checked against the arguments, not retargeted by them.
Constraints filter the established argument; they do not choose an implementer
or weaken `T` merely to make `[Copy]`, `where T : write`, or a witness succeed.

### Forward shapes and non-injective projections

Decompose known injective constructor shapes by selected definition and ordinal.
A nominal `Box<T>` remains an anchor; it is not the normalizing `box<T>` operator.
Constants retain their declared domain and
[canonical value identity](constant-generics.md#canonical-value-identity).

Do not recover an unknown argument from `T?`, compatible `box<T>`, `val<T>`,
or an associated projection. Their normal forms need not uniquely identify an
original term. If other evidence already supplies the inner arguments,
substitute forward and check the resulting complete type and admitted binding.
A proven redundant `write T` under `where T : write` is the same formal term,
not a non-injective search for readonly and writable originals.

The selected pin-reference-to-pointer edge can expose its known, exact
referent term in forward matching. It does not expose new inferred access,
acquire a pin, or enable user-conversion/intermediate-type search.
Qualification attenuation remains separate from changing the referent type.
Other registered direct projections may participate only through their
published forward contract; they are not inverse inference rules.

### Contextual structural evidence

A candidate's already-known tuple shape or eligible collection family can
expose independently typed constituent evidence. This does not supply a
canonical representation for an otherwise untyped expression.
For a tuple target, map elements by its fixed logical positions, not labels
or an inferred parameter pack. Complete ordered element arguments remain
invariant when the tuple type itself is supplied as a typed value.
For a written tuple expression under that known shape, gather each
constituent's value-binding evidence instead of first fixing a competing
whole-tuple type. Constructing elements with admitted attenuation is not
covariance of an existing typed tuple.

For a collection expression, the selected family's published builder/element
contract must expose a supported forward relationship to its element formal.
Typed elements and the declared element types of eligible spreads can
contribute by-value evidence to that formal. Combine their admitted immediate
access as for ordinary element arguments; invariant positions inside an
element remain exact. Do not execute a spread, inspect a builder's private
body, or invert an associated/non-injective projection to find the element.

Thus `Inspect<T>(Span<T> items)` can infer `T = int32` from
`Inspect([int32(1), int32(2)])` under an eligible collection contract.
`Inspect([1, 2])` and an empty collection provide no element evidence;
another typed argument, expected result or allowed default must supply it.
`var items = [int32(1)]` still has no selected collection representation.
Typed children do not themselves register a builder or authorize allocation.

Gather available structural evidence with ordinary typed evidence.
Newly established element/input positions can enable delayed callable
evidence. Explicitly contextual children that remain unresolved wait for
expected-result/default completion; they are not guessed to help inference.
Element copies/transfers, spread enumeration, partial cleanup, builder
effects and complete contextual construction are checked after selection.

### Eager text-content evidence

An interpolation at an eligible [TextTarget parameter](construction-handlers.md#texttarget-parameter-schema)
has a checked typed-capture formatter construction, not an implicit default
string type. When its source-known capture types and fragment contracts are
established, its module-local complete content shape fixes a generated type
that supplies forward evidence for the content formal through ordinary
by-value or implicit-reference binding. Equivalent closed shapes in the same
module supply equal type evidence even at different source occurrences;
captured values and literal text are data, not type arguments. Distinct
shapes or originating modules retain distinct nominal identities.
Unresolved hole types receive no guessed numeric width or inverse inference
from the desired formatter. Already established type arguments remain fixed.
The declaration's complete constraints, capture/storage/ownership and
formatting obligations still require checking after unique selection.
TextTarget contributes no new binding-quality or specificity preference,
signature identity, callback-body inference or candidate execution.
Without that schema, a generic IFormattable constraint alone does not generate
this content; ordinary interpolation can still use an independently
established target with its own checked construction registration.

### Delayed callable evidence

A method group or lambda needs an already-selected callable family/modes and
enough known input signature to supply evidence. This does not infer a canonical
`func`, `fnptr`, environment, or nominal functor from a body.
Method groups contribute declared signatures, never implementation return
expressions. Resolve known signature positions under ordinary selection;
unresolved holes need one determinate signature result, not an arbitrary
winner among distinct signatures.
A determinate declared method signature can supply missing input positions
of a known local callable target. A lambda's body cannot supply its own
unknown input types.

When a contextual lambda's input types are established, type its result using
those types without an unresolved result context. An independently typed result
can contribute delayed typed evidence. Apply ordinary complete-type consistency
and permitted immediate-access combination to multiple typed normal results.
Do not guess input types, use a default numeric width, execute a literal parser,
or manufacture witnesses to discover a result.

For `Map<T,R>(T value, func<T,R> transform)`, a typed `int32` argument fixes `T`;
`x => x + 1` then has typed `int32` result and fixes `R`.
`x => 1` alone supplies no result type. A nested untyped lambda does not
automatically produce a callable type. Explicit annotations can constrain
signature positions; they do not select a representation.

Revisit only finite unresolved positions whose known dependencies changed.
Delayed result evidence fills unresolved result positions; it does not
retarget established inputs or require retyping a body under successively
guessed parameter types. If a result position is already established, its
body is checked against that target rather than reinferred to repair a
conflict. Method groups and delayed lambda typing are not a general logical
solver or a license to try private bodies of candidate callables.

### Expected result, defaults, and contextual completion

After typed evidence reaches its finite fixed point, an expected result may
fill only still-unresolved positions through the same forward-shape rules.
It cannot retarget a typed operand, replace a typed lambda result, change an
explicit pin, or strengthen the declaration's constraints.
Resume typed rounds when this establishes new inputs, before choosing defaults.

The expected type of a value-producing lifted `require` relates to its omitted
output value, or ordered complete tuple of outputs, not the status return.
Without omitted outputs, the requirement gives its operand a Boolean checking
context and itself yields `void`. The enclosing expected type does not become
the operand's result context: an unresolved ordinary result formal can receive
`bool` from the check, not `void` from the requirement's unit result.
Pattern-arm types provide no inference evidence and cannot select a channel.
For constructors, an explicit/expected constructed type supplies its declared
arguments; no inference comes from field capture or a private constructor body.

Then visit omitted unresolved formals in declaration order and resolve their
declared earlier-formal defaults. After each successful default, resume typed
rounds before considering the next default. Defaults do not repair conflict
or ambiguity.
An unresolved `_` cannot fall back to a default. A defaulted formal already
established by typed/expected evidence stays established.
Every term must satisfy its formal kind/domain, formation and declared
requirements before a candidate can be accepted.

For `Build<T = int32, R = int64>(func<T,R> transform)`,
`Build(x => x + 1)` first defaults the unresolved input `T`, then infers
`R = int32` before its default is considered. If a result formal was already
established earlier, enabling a body cannot change it. An unresolved `_`
cannot itself acquire an input from a default, but can use newly enabled
typed evidence. Remaining holes/default dependencies without progress diagnose.

Contextual literals, lambdas, and collection expressions now need a complete
eligible target. Typed structural and delayed callable evidence can have
established positions, but the syntax does not itself infer a canonical
collection/callable family. Any still-missing position diagnoses.
All local holes must resolve at binding. No exported signature, alias,
parameter or field retains an unresolved hole.

## Applicability and best candidate

Check parameter count/mapping/modes, completed inference, permitted bindings,
receiver access, declared constraints/witnesses, and independently required
ownership/lifetime/representation facts. Do not actually evaluate arguments,
invoke bodies, allocate adapters, or execute registered literal parsers for
each candidate. Call-site facts cannot lend a template stronger constraints
than its definition declares.

Contextual syntax contributes its known kind, arity, explicit signature
positions, and representation eligibility. Literal **value** acceptance,
range, parser success, and parser budgets do not choose a signature.
Thus `Select(int8)` and `Select(int64)` leave `Select(300)` ambiguous;
`Select(int64(300))` supplies typed evidence.

Compare quality for each corresponding supplied argument:

| Quality | Meaning |
|---|---|
| Identity | Exact normalized complete type/mode binding |
| Access attenuation | Already-permitted immediate access weakening, retaining representation and inner arguments |
| Other admitted binding | One selected direct builtin/projection/erasure/reference-adaptation contract; no conversion-chain search |

Identity is better than attenuation, and attenuation better than other admitted
bindings. Distinct bindings within one class have equal quality unless an
adopted contract explicitly provides another rule.
Eligible contextual literal targets have equal quality without considering
their range or parsed value. Untyped contextual syntax supplies no fictitious
identity match; known signature positions retain their ordinary comparisons.
A candidate dominates only if it is no worse at every supplied argument
and strictly better at at least one. Crossed advantages leave that pair incomparable,
not summed into a score. Output count, allocation estimates, trait-proof cost,
literal fit, result type, and declaration order do not rank candidates.

After quality ties, apply the established binding priorities: by-value `T`
over otherwise equal implicit formation of `ref<T>`, and non-expanded over
an otherwise equal expanded `params` form. An already supplied reference
descriptor is not an implicit place-borrow adaptation.
Parameter-binding preferences likewise need no worse corresponding binding
and a strict advantage; crossed preferences do not establish a better candidate.
Then compare declared parameter structure at corresponding supplied arguments:
a strict specialization through consistent substitution is more specific;
unrelated/crossed structures remain ambiguous.
An applicable `List<U>` pattern is more specific than bare `T` at a
`List<int32>` argument. A nongeneric exact declaration wins a remaining tie
with a generic declaration, not before argument quality.
Constraints do not create constraint-only overloads.

Ordinary and omitted-output forms share those comparisons. Omitted
destinations provide neither comparison arguments nor specificity advantage.
If ordinary rules leave them tied, diagnose ambiguity rather than prefer a
return or output channel. Applicable ordinary members retain priority over
extensions, and ambiguous applicable members do not fall back to extensions.

Apply these comparisons to the complete applicable set. A candidate beaten
by another is not a winner; unique selection needs one candidate better than
every other applicable candidate. A crossed pair does not prevent a third candidate
from dominating both. If no unique winner remains, diagnose ambiguity rather
than choosing an arbitrary maximal candidate.

After unique selection, perform complete target checking, registered literal
construction, lambda flow/effect/capture checking, defaults and collection
construction validation. A failed range/parser/target check is a diagnostic,
not permission to retry another declaration or channel.
Discharge a selected [NoAlias parameter requirement](published-predicates-and-effects.md#noalias-parameters)
against the actual staged receiver, arguments and prepared destinations.
It does not participate in type inference, signature identity or ranking,
and an unestablished separation proof does not retry another candidate.
Implicit materialization and expanded element slots do not privatize copied
handle/view backing. Preserve ordinary argument/default/cleanup order.
For text/collection construction, substitute the uniquely selected target's
[bound builder recipe](construction-handlers.md#definition-checking-and-substitution).
Builder arity, a private successful implementation or missing protocol
support cannot retarget established arguments or choose another candidate.
Record the selected declaration, closed arguments, witness origins and
admitted adaptations. Linking/JIT work validates that contract; it does not
rerun overload resolution or recover a missing source feature.

## Omitted outputs and retained observations

Only a call directly serving a matching or value-producing `require` subject
requests omission; ordinary parentheses preserve that subject.
The context does not omit destinations from unrelated nested argument calls.
Explicit `init ref<T>` modes with checked ordinary call-completion output,
ownership and dependency contracts establish eligibility without another
attribute or output registration. Required inputs remain required.
Later task/producer completion needs its separate phase contract.
The [selected async cursor](async-iteration.md) uses an explicitly supplied
init destination and an operation-bound completion Boolean. Awaiting it
establishes its reservation/publication relationship, not ordinary call-return
lifting; `require` or matching does not omit that destination or insert a join.

[Synchronous lending outputs](lending-iteration.md#reference-output-and-publication)
may use the existing eligible call-completion lifting rules. The lifted value
is the exact ref<T>/write ref<T> descriptor with its source/backing/invalidation
obligations, not a copied/owned T or an element borrowed from hidden output
storage. Async lending retains explicit descriptor destinations and the
selected completion reservation; lifting does not add a wait or longer loan.

Supplied destinations remain ordinary arguments. Omitted destinations form
an ordered contextual projection: one has its complete value type; several
have a structural shape in declaration order. This is not a tuple formed by
reading vacant slots. With all destinations supplied, matching observes the
ordinary return value. Pattern types do not choose that channel.

### Hidden destination instances and retirement

Each omitted destination site has one hidden initially vacant mutable ordinary
local per activation of the original supporting scope. Apply the
[supporting-scope table](initialization-and-flow.md#binding-pattern-declarations-and-scopes):
an ordinary `if`/`while`/`do` condition uses its enclosing block, `for` and
iteration expressions their header, and contract clauses their isolated phase.
Reentered body/arm/lambda scopes create their ordinary new activation.
The call site also has one reusable hidden observation slot for its ordinary
return, with that complete type and logical state. Private helper names do
not occupy user declaration spaces.

The complete type and all ordinary storage, initialization, ownership,
root/loan/guard and cleanup rules apply. No owner arena, private lifetime
extension, stack-allocation-per-iteration retention, or hidden clone is allowed.
An ignored owner behaves like an unused named output local; neither the end
of an `if` nor ending a named view implicitly retires that separate owner.
Supporting scope supplies a boundary, not permission to outlive a declared
backing or other stricter contract.

At a reached evaluation, stage the receiver and written arguments under
ordinary source-order/place/loan rules and perform required invocation-only
receiver checks at their ordinary point. At that boundary, retire
the previous hidden observation/destination values in an ordinary
dependency-valid order and establish vacant destinations before initialization.
Each live value retires once. A retained user view or other obligation can
prohibit that retirement; diagnose rather than keeping another hidden owner.
If staging or an invocation-only receiver check fails, previous values remain
until ordinary unwind. If the call fails after preparation, the old values
are already retired; no rollback or replay is implied.

A skipped site preserves its hidden values. Those values are not a result of
an invocation that did not occur: short-circuit/optional evaluation must retain
its not-evaluated path, not expose an earlier output as current availability.
Reached normal failure uses its declared per-output live/vacant state.
Final scope exit applies ordinary conditional cleanup and dependency checks,
even when it occurs after the construct containing the call.
Implicit-reference materialization still uses its separately specified
build-before-drop assignment rule; initialization destinations cannot be
reinitialized while live.
A retained initialized-reference loan can therefore prevent preparation
even when the reusable slot's address does not change.

For an owner output and a dependent Copy view, this source-equivalent example
marks the boundaries explicitly:

```terrum
{
    init mut Owner ownerTmp;
    init mut View viewTmp;
    init mut bool statusTmp;

    // At invocation, after staging the original written arguments.
    if (viewTmp is init)
    {
        viewTmp = _;
    }
    if (ownerTmp is init)
    {
        ownerTmp = _;
    }
    statusTmp = TryMake(init ownerTmp, init viewTmp);
    if (viewTmp is var view)
    {
        Use(view);
    }

    AfterIf();

    if (view is init)
    {
        view = _;
    }
    if (viewTmp is init)
    {
        viewTmp = _;
    }
    if (ownerTmp is init)
    {
        ownerTmp = _;
    }
}
```

The final lifetime ends illustrate ordinary block-exit retirement, not
cleanup inserted immediately after the `if`. `view = _` releases that value
and its dependency; it does not also destroy `ownerTmp`.
Initialization tests make retirement conditional; ending a vacant value
is not a valid source operation. Assume `TryMake` publishes checked
owner/view dependencies and conditional states, and the example's view is Copy.
On repeated evaluation, a still-live `view` may block retirement of the old
owner. Hidden dependencies must be accounted for too; source lowering does not
leave a private dependent alive while pretending its owner may be replaced.

### Matching, requirements, and ownership

Save the call's ordinary return as part of its checked observation, retaining
status correlations and any ordinary ownership/dependency obligations.
Selecting outputs does not abandon a task, owner, guard or status obligation.
Elision is permitted only when it preserves logical state and cleanup.

Matching tests declared initialization/presence, not a universal success bit.
An initialized failure output may match. `_` reads nothing and demands no
initialization; live ignored values still have their ordinary scoped obligations.
Binding patterns follow their fixed/mutable supporting-local rules and Copy
requirements. They do not consume a unique output to test an arm or its guard.

A bare lifted value-producing `require` first requires every omitted output
initialized, without stripping nullability or assuming a success group.
Then it yields the one complete value or complete ordered tuple through
ordinary copy/transfer. No owning output is transferred before those checks.
Failure raises the selected requirement failure under ordinary unwind.
No fabricated default, vacant read, implicit detach/join, or obligation
erasure is introduced.

### Boolean requirements and logical unit

When no outputs are omitted, a value-form `require` performs the ordinary
Boolean check: evaluate its primitive `bool` predicate once, yield `()`
on true, and raise the selected requirement failure on false. Its complete
result type is `void`, not an always-true Boolean or the predicate's type.
A non-Boolean ordinary status needs an explicit Boolean predicate; unit,
integers and status enums have no implicit truthiness.

```terrum
var result = require Ready(); // Ready returns bool; result has type void.
int32 value = require Read(); // Read has an omitted init ref<int32> output.
init int32 destination;
var checkedResult = require Read(init destination); // Check status; yield ().
```

The declarations in the example have otherwise valid ordinary call-completion
contracts. All explicitly supplied destinations stay in their caller locals
with the states the call actually publishes; checking its status does not
extract, move, or roll them back. Failure during predicate evaluation propagates
as that evaluation failure, not as the selected false-requirement exception.
These rules do not use a result context to break an ordinary/lifted call tie.

An omitted `bool` output remains an output value: a lifted requirement
checks its initialization, not whether the payload is true. It can therefore
yield `false`. A value-form Boolean check instead yields unit; ordinary result
binding/return rules admit that unit and reject an implicit unit-to-`bool`
conversion. Statement-form `require predicate;` remains a Boolean check
without requesting output omission.

### Selective extraction

Selective Copy extraction can use patterns while ignoring other outputs.
For a unique selected output, supply an explicit initialization destination
and move it after the desired availability/guard succeeds:

```terrum
init Owner owner;
if (Read(init owner) is var count
    && owner is init && Accept(ref owner, count))
{
    Consume(move owner);
}
```

Here `count` is the omitted Copy output; `owner` is not lifted.
Failed guards leave any live owner in its ordinary caller local.
Explicit guard effects are not rolled back; the body move occurs only after
selection. New move-binding patterns or implicit unique aliases are not
part of this profile.

[Conformance cases](../conformance/source-calls-and-inference.md) specify
the required inference, ranking, scope, and extraction outcomes.
