# Call and inference conformance cases

These cases apply [calls and inference](../language/calls-and-inference.md),
the [call/output contracts](../language-contracts.md#23-checking-and-consuming-the-contract)
and [source grammar](../grammar-spec.md#33-statements-and-local-declarations). They are specification
obligations, not executed compiler tests. Assume otherwise valid arguments
and an admitted ordinary call-completion contract unless a case tests selection.
Later-completion protocols need their separate supported profile.

## Omitted-output eligibility

| Case | Situation | Required result |
|---|---|---|
| LIFT-01 | Boolean callable declares `init ref<int32>` and no additional marker | Eligible in matching/value-producing `require`; Boolean default supplies true-live/false-vacant |
| LIFT-02 | Callable has no initialization destination, but its name or ordinary reference parameter suggests output | No output eligibility by name, readonly/writable reference access, or arbitrary attribute |
| LIFT-03 | A unit-result callable declares an eligible initialization destination | Ordinary normal completion must establish its declared output state; result need not be Boolean |
| LIFT-04 | Destination has a complete qualified generic type supported at the call | Preserve that exact type, ownership, dependencies and formation obligations |
| LIFT-05 | All initialization destinations are explicitly supplied | No output projection; matching observes the ordinary return value |
| LIFT-06 | One destination is supplied by name and other destinations are omitted | Lift only omitted destinations in declaration order; preserve supplied destination correlations |
| LIFT-07 | A required ordinary input is omitted | Reject; output lifting does not omit inputs |
| LIFT-08 | Ordinary `_ = TryRead();` omits a required destination | Reject despite explicit result discard; ordinary calls do not request output lifting |
| LIFT-09 | Statement-form Boolean `require TryRead();` omits a required destination | Reject; statement requirement does not silently request a value-producing projection |
| LIFT-10 | An eligible matching/value-producing call omits destinations | Evaluate one selected call, not a status call followed by a second output call |
| LIFT-11 | A declaration publishes initialized outputs on a normal failure status | Match declared availability; do not impose a universal successful-status gate |
| LIFT-12 | An output reference/view retains a declared input dependency | Preserve it; projection does not create a new reference, backing source or lifetime extension |
| LIFT-13 | Hidden destination cannot be represented as an eligible ordinary local | Reject unsupported storage; no guessed size, hidden box or weakened type |
| LIFT-14 | A pattern ignores a live owning output with `_` | Do not read it; retain required cleanup and other outstanding obligations |
| LIFT-15 | An eligible output has type `void` | Preserve logical destination position and live/vacant state despite zero payload bytes |
| LIFT-16 | Output publication occurs only after task completion or producer advancement | Do not lift at ordinary invocation completion or implicitly join/advance |
| LIFT-17 | Required output/projection contracts lack physical profile support | Reject required semantic consumption; row framing alone is not execution support |
| LIFT-18 | `Read()` and `Read(init ref<bool>)` are otherwise tied in an omission-permitting context | Diagnose ambiguity; identical payload types do not select a channel |
| LIFT-19 | Supplied-argument quality or declared specificity establishes one winner | Select it under ordinary rules; hidden-output presence does not override that result |
| LIFT-20 | A pattern's type would match only one tied ordinary/lifted candidate | Still diagnose call ambiguity before pattern checking |
| LIFT-21 | Existing nongeneric/parameter-binding priorities resolve an otherwise tied pair | Preserve those ordinary priorities; no new output-count preference |
| LIFT-22 | Caller explicitly supplies `init destination` to the output-taking overload | Select by ordinary argument/mode matching; all-supplied outputs leave return matching |
| LIFT-23 | Both declarations exist but the call is outside an omission-permitting context | Only ordinary applicable forms participate; `Read()` may select the zero-output declaration |

## Argument mapping and call forms

Fixtures have visible complete declarations and otherwise valid bodies.
`C` handles and the scalar types permit their stated copies; `Owner` is
unique. Collection/callable examples require an admitted published family
contract, not a guessed representation or implementation-derived builder.

| Case | Situation | Required result |
|---|---|---|
| CARG-01 | A selected local callable has the wrong arity while a hidden type has a matching conversion | Diagnose the invocation; no callable-to-type retry |
| CARG-02 | Two imports supply distinct same-named callable groups | Diagnose group ambiguity before comparing argument types |
| CARG-03 | Arguments use in-position labels followed by positional arguments | Preserve declaration positions and source evaluation order |
| CARG-04 | A positional argument follows an out-of-position label | Reject; no next-unfilled-slot search |
| CARG-05 | An argument repeats a label or names an unknown label | Reject the mapping |
| CARG-06 | A leading output is omitted before a supplied required input | Use the input's label; a positional input does not slide past the output |
| CARG-07 | An ordinary value is supplied for `init ref<T>` | Reject the mode; no initialization permission from value conversion |
| CARG-08 | An explicit `init` argument names a live owner | Reject; passing a loan does not drop the owner |
| CARG-09 | An explicit destination is vacant but has a different complete inner type | Reject invariant storage mismatch |
| CARG-10 | An omitted ordinary parameter has a valid published Copy default | Use that selected value without reevaluating a provider body |
| CARG-11 | A defaulted parameter precedes a required parameter | Permit labeled omission; keep positional mapping unchanged |
| CARG-12 | One argument directly matches the declared `params List<T>` | Use normal form and preserve that collection |
| CARG-13 | `T` is explicitly pinned to `List<int32>`, making a `List<int32>` argument inapplicable to normal `params List<T>` | Consider eligible expanded form with one `List<int32>` element |
| CARG-14 | Expanded `params` receives several typed elements | Construct new element slots under ordinary copies/transfers; do not alias caller locals |
| CARG-15 | A unique expanded element is a named owner without `move` | Reject implicit consumption; a fresh value or valid explicit move remains eligible |
| CARG-16 | `params` would require an unregistered/unsupported builder | Reject; argument mapping does not synthesize the builder |
| CARG-17 | `F()` and `F(())` are both declared | Preserve zero versus one logical argument despite zero unit payload |
| CARG-18 | A generic argument list is present | Exclude nongeneric candidates; require each remaining candidate to accept the formal prefix/kinds |
| CARG-19 | Final `params` has no supplied elements and its complete type is established | Use eligible empty expanded construction; no inferred default element type |
| CARG-20 | A label naming `params` supplies an eligible collection value | Use normal whole-collection binding |
| CARG-21 | The named `params` argument supplies one scalar rather than its collection | Reject unless that scalar has an independently admitted normal collection binding; no named expanded element |
| CARG-22 | Fixed parameters use in-position labels, followed by positional expanded elements | Permit the mixture with original positions and source evaluation order |
| CARG-23 | A member belongs to an already-closed generic declaring type | Substitute that owner's exact arguments before method inference; do not infer them from private fields |

## Finite typed and contextual inference

`Choose<T>(T left, T right)` and `Use<T>(T value, ref<T> storage)` below
are schematic signatures with suitable Copy/transfer contracts.
`Map<T,R>(T value, func<T,R> transform)` has known callable family/modes.
No case permits private callee-body inspection.

| Case | Situation | Required result |
|---|---|---|
| CINF-01 | `F<T>(T left, T right)` receives a typed `int32` and untyped `1` | Infer `T = int32`, then check the literal against that target |
| CINF-02 | The same call receives only `1` and `2` without result/default evidence | Reject unresolved `T`; no default integer width |
| CINF-03 | `Identity<T>(T value)` receives `int32(42)` in an `int64` result context | Keep `T = int32`; diagnose incompatible use unless an explicit result conversion is supplied |
| CINF-04 | `Identity(42)` has expected result `int64` and no typed evidence | Infer unresolved `T = int64`, then validate the literal |
| CINF-05 | `Choose` receives two `write C` values | Infer `T = write C`, not `C` to satisfy a witness |
| CINF-06 | `Choose` receives `write C` and `C`, in either order | Infer `T = C` through permitted immediate attenuation |
| CINF-07 | Both arguments have the same optional stem and mixed present-value access | Infer common access without proving presence or removing `?` |
| CINF-08 | Arguments differ in pin, ownership or representation rather than only admitted immediate access | Reject combination; no capability weakening/search disguised as inference |
| CINF-09 | Arguments have different numeric types | Reject combination; no numeric promotion |
| CINF-10 | Arguments have different nominal types with a common base/interface | No inferred common-base or implementer search |
| CINF-11 | Typed arguments are `ref<write C>` and `ref<C>` | Reject their inner mismatch; no recursively inferred covariance |
| CINF-12 | Typed arguments are `List<write C>` and `List<C>` | Reject inner mismatch; the outer nominal stem does not erase element access |
| CINF-13 | `Use` receives a writable value and exact readonly `C` storage | Infer exact `T = C`; attenuate the value only |
| CINF-14 | `Use` receives a readonly value and exact writable `C` storage | Reject; exact storage cannot be retargeted and the value cannot upgrade |
| CINF-15 | Two invariant storage arguments supply conflicting complete types | Reject independently of argument order |
| CINF-16 | An `init` destination supplies exact `T` while by-value evidence suggests another stem | Reject the conflict; no hidden alternate destination type |
| CINF-17 | Solely writable value evidence fails a declared constraint | Reject that established type; do not reinfer readonly to rescue the constraint |
| CINF-18 | Mixed access establishes readonly `T`, but `where T : write` is required | Reject; the constraint filters rather than upgrades inference |
| CINF-19 | A supported injective nominal `Pair<T,U>` argument has known ordered complete arguments | Infer those exact argument terms through the nominal definition |
| CINF-20 | Only an unknown `T?`, compatible `box<T>` or associated projection relates to an actual type | Do not invert it; require independent evidence |
| CINF-21 | Another argument pins `T` before a non-injective formal is checked | Substitute forward and check its normalized type/binding |
| CINF-22 | `write T` is proven redundant under the actual-write requirement | Treat it as the same formal, not a search for readonly originals |
| CINF-23 | A known pin-reference argument meets the selected direct pointer formal edge | Infer its exact known referent forward; acquire no pin and search no intermediate type |
| CINF-24 | Explicit `T = C` is supplied with writable `C` values | Preserve the pin and use admitted attenuation; do not change `T` to write |
| CINF-25 | Explicit `T = write C` is supplied with a readonly value | Reject the binding; inferred fallback cannot replace the pin |
| CINF-26 | `Convert<_, int32>(typedValue)` has appropriate formals | Infer only the hole; keep the explicit result type |
| CINF-27 | An unresolved `_` has a declared default | Reject; an inference hole does not select the default |
| CINF-28 | An omitted unresolved formal has a valid declared default | Use it only after typed and expected-result evidence |
| CINF-29 | Typed evidence establishes a defaulted formal | Keep the inferred term; the default does not override it |
| CINF-30 | Explicit `default` depends on an earlier formal established by typed input | Resolve the selected forward expression once that earlier term is known |
| CINF-31 | Explicit `default` has no declared default, or a default depends forward/cyclically | Reject; no fallback or inverse dependency solver |
| CINF-32 | A local callable hole has one determinate declared method signature | Fill it from that signature and check the complete callable contract |
| CINF-33 | Competing method signatures leave a hole or target representation ambiguous | Reject; result bodies and declaration order do not choose |
| CINF-34 | A method's private body appears to return a narrower type | Ignore it for inference; use the published result signature |
| CINF-35 | Typed `int32` input and `x => x + 1` are supplied to `Map` | Known input types permit delayed `R = int32` before expected/default fallback |
| CINF-36 | The callback is `x => 1` with unresolved `R` | No result width from the literal; use other evidence or reject |
| CINF-37 | A callback's input type or callable family is still unknown | Do not guess it to type the body |
| CINF-38 | Delayed evidence establishes `R = int32` but expected result asks for another type | Keep the typed result; context does not retarget it |
| CINF-39 | A result formal is established by an explicit pin | Check the callback against it; do not reinfer known inputs/results to repair the body |
| CINF-40 | Known inputs and `x => ()` target the declared callable family | Infer an unresolved result formal as logical `void` |
| CINF-41 | A nested untyped lambda is the callback's result | Do not invent an inner callable representation |
| CINF-42 | `Inspect<T>(Span<T>)` receives `[int32(1), int32(2)]` under an eligible element contract | Infer `T = int32` from typed elements, then validate construction |
| CINF-43 | The collection contains eligible typed spreads | Use their declared element types without enumerating them during inference |
| CINF-44 | The known collection contains only untyped literals or is empty | Supply no element evidence; need another argument, expected result or allowed default |
| CINF-45 | Typed collection elements have mixed access over the same normalized stem | Use strongest common permitted element access, preserving invariant inner arguments |
| CINF-46 | The builder element type is an unknown associated/non-injective projection | Do not invert it or inspect the builder; require other evidence |
| CINF-47 | `var items = [int32(1)]` has no selected collection family | Reject missing target representation despite typed children |
| CINF-48 | A known tuple target `(T, int32)` receives `(int32(1), 2)` | Infer the first position from typed evidence; then check the contextual second element |
| CINF-49 | An injective nominal argument carries a known constant argument | Infer the exact declared domain/canonical value; no layout-based wrapper identity |
| CINF-50 | An unresolved constant would require reversing arithmetic or guessing a witness | Reject; finite forward checking is not symbolic inversion |
| CINF-51 | Two currently enabled callbacks provide writable/readonly result evidence over the same stem | Gather both in the round and infer common access independently of callback order |
| CINF-52 | A callback becomes enabled only after its shared input/result formal was established | Check that established result; do not retype earlier inputs to repair a conflict |
| CINF-53 | Expected-result fallback establishes a callback input while its result remains unresolved | Resume typed rounds and infer the independently typed result before its default |
| CINF-54 | `Build<T=int32,R=int64>(func<T,R>)` receives `x => x + 1` without other evidence | Default T, resume body evidence, and infer R=int32 before R's default |
| CINF-55 | A result default occurs earlier than the input default that enables the callback | Keep the earlier established result; incompatible body checking diagnoses without reinference |
| CINF-56 | Callable input/result dependencies have no typed, expected or permitted default starting point | Reject when finite progress stops; no guessing or recursive type search |
| CINF-57 | A known local `func<_,int32>` target has one determinate method signature | Its declared input can fill the local hole; do not impose a lambda-style unknown-input prohibition |
| CINF-58 | Function candidates with different declared generic arities accept the same explicit prefix | No type-name exact-arity preference; use ordinary argument quality/specificity or diagnose a tie |
| CINF-59 | A selected type default exposes an injective constructor with an unresolved earlier formal | Match the forward shape to typed evidence; do not replace the default or invert arbitrary expressions |
| CINF-60 | A written tuple under known `(T,T)` shape contains writable/readonly values of the same stem | Infer common element access and construct that tuple; an existing typed tuple's inner arguments remain exact |
| CINF-61 | Expected `int32` consumes a lifted `require` from Boolean `Read<T>(init ref<T>)` | Infer T from the omitted output value, not the ordinary Boolean return, then require initialization |
| CINF-62 | Matching `Read<T>()` has no typed/default evidence but an `int32` binding pattern | Reject unresolved T; arm type is not inference evidence |
| CINF-63 | An admitted extension contract has an injective generic receiver parameter | Use its actual receiver as ordinary typed evidence without shifting written argument positions |
| CINF-64 | `void result = require Read();` calls a selected `T Read<T>()` without outputs or typed evidence | Infer T=bool from the predicate context, not void from the enclosing unit result |
| CINF-65 | Ordinary `require Identity(int32(1))` has established result T=int32 | Reject the non-Boolean predicate; do not retarget typed evidence to bool |

## Applicability and ranking

| Case | Situation | Required result |
|---|---|---|
| CRANK-01 | Identity and access-attenuation candidates otherwise apply | Prefer identity |
| CRANK-02 | `write pin ref<int32>` can bind `pin ref<int32>` or `write ptr<int32>` | Prefer access attenuation over the other direct binding |
| CRANK-03 | Two supplied arguments give crossed identity/attenuation advantages | Diagnose ambiguity; no summed score |
| CRANK-04 | Distinct other admitted single-step bindings have equal quality | No invented cost, allocation or representation tie-breaker |
| CRANK-05 | Ordinary by-value `T` and implicit formation of `ref<T>` both apply | Preserve the value-over-borrow result under ordinary quality/priorities |
| CRANK-06 | By-value consumption of a named unique owner is invalid, but an implicit ordinary borrow is valid | Only the borrow candidate applies; ranking cannot authorize a move |
| CRANK-07 | Caller supplies an existing reference descriptor | Compare its actual complete binding; do not treat it as implicit formation from the referent |
| CRANK-08 | A fixed/non-expanded form ties with an expanded `params` form | Prefer non-expanded after quality, not before it |
| CRANK-09 | Expanded form has strictly better per-argument bindings | Preserve its quality win; the non-expanded preference is not absolute |
| CRANK-10 | Bare `T` and declared `List<U>` both infer identity at `List<int32>` | Prefer the declared structural specialization |
| CRANK-11 | Declared parameter structures specialize each other inconsistently across arguments | Diagnose crossed/incomparable specificity |
| CRANK-12 | A nongeneric exact declaration ties a generic one after quality and binding priorities | Prefer nongeneric |
| CRANK-13 | A generic candidate has better supplied-argument quality | Preserve that win; nongeneric status is only a remaining tie-breaker |
| CRANK-14 | Call supplies explicit generic arguments | Compare eligible generic declarations only |
| CRANK-15 | Declarations would differ only by constraints or result type | Reject duplicate overload shape rather than rank the constraints/results |
| CRANK-16 | `Select(int8)` and `Select(int64)` receive untyped `300` | Ambiguous before value acceptance, although only int64 can represent it |
| CRANK-17 | Only `Select(int8)` is selected for `300` | Diagnose range failure; do not retry another name, group or conversion |
| CRANK-18 | Two registered integer-literal targets have different parser success/budgets | No parser execution or fit-based choice during selection |
| CRANK-19 | Unique binding fails full lambda flow/capture/effect or collection target checking | Diagnose the selected target; do not retry a losing declaration |
| CRANK-20 | Applicable ordinary members are ambiguous while an extension would be unambiguous | Diagnose member ambiguity; no extension fallback |
| CRANK-21 | Two candidates have crossed advantages but a third dominates both | Select the third if it is uniquely better than the full applicable set |
| CRANK-22 | A result context fits only one of otherwise tied fixed declarations | Still diagnose ambiguity; expected results fill generic holes, not rank fixed return types |
| CRANK-23 | Quality ties but parameter-binding preferences cross between supplied arguments | Diagnose ambiguity; do not count value-versus-borrow advantages |
| CRANK-24 | Selected normal `params` form fails its registered literal parser while expanded form could accept the value | Diagnose the selected normal form; no parser-driven expanded fallback |
| CRANK-25 | Within one selected eligible extension group, a writable receiver has identity versus attenuation bindings to the declared logical receiver | Include that receiver's ordinary quality; prefer identity without shifting written arguments |

## Hidden storage, scopes, and preparation

| Case | Situation | Required result |
|---|---|---|
| COUT-01 | An omitted-output call occurs in an ordinary `if` condition | Hidden values use the existing supporting block, not the successful branch |
| COUT-02 | The call occurs in an ordinary `while` or `do` condition | One hidden destination/observation set per site and enclosing-block activation |
| COUT-03 | The call occurs in a `for` initializer/condition/step | Use the existing header scope; retire at its valid exit |
| COUT-04 | The call occurs in an iteration sequence expression | Use the iteration header activation and required close obligations |
| COUT-05 | A call site lies in a reentered braced loop body | A fresh ordinary hidden-local activation per body entry |
| COUT-06 | Call sites are in distinct arms, candidate catches or lambda activations | Their original supporting scopes remain distinct |
| COUT-07 | A call occurs in a declaration contract phase | Keep it in that isolated phase; no body/caller lifetime extension |
| COUT-08 | Two source sites omit the same destination type | Separate logical locals; no shared mutable output slot |
| COUT-09 | An ignored owner backs a copied view used after the `if` | Preserve the owner through the supporting scope and all declared dependencies |
| COUT-10 | The copied named view executes `view = _` | End that view, not the independently live hidden owner |
| COUT-11 | A loop exits with live hidden values | Do not retire block-supported values at loop exit; header/body values retain their own scope boundaries |
| COUT-12 | A later evaluation skips the entire call | Preserve old hidden storage, but establish no current invocation/output fact |
| COUT-13 | Short-circuit evaluation skips a nested call | Do not omit its destinations merely because an outer call is a matching subject |
| COUT-14 | A directly matching call is parenthesized | Preserve its omission context without adapting unrelated argument calls |
| COUT-15 | Repeated reached invocation has old live hidden destinations | Stage written arguments/checks first, retire old values under ordinary rules, then supply vacant destinations |
| COUT-16 | A hidden destination or observation is vacant at preparation | Do not perform a live-value lifetime end or drop on it |
| COUT-17 | A written argument fails during staging | Preserve old hidden values until ordinary unwind; clean acquired argument state without invoking the callee |
| COUT-18 | An invocation-only receiver check fails | It occurs after argument evaluation and before hidden retirement/transfer |
| COUT-19 | The call fails after preparation | Old hidden values stay retired; apply the actual new acquisition/exceptional contract, with no rollback |
| COUT-20 | A retained user binding, staged argument or receiver depends on the old hidden owner | Reject invalid retirement; no implicit binding end, clone or retained-owner arena |
| COUT-21 | Only hidden dependent observations must retire before their owner | Account for their ordinary dependency/cleanup order; parameter order alone is not a drop proof |
| COUT-22 | A retained initialized-reference loan requires the old hidden destination to remain live | Reject vacancy preparation while that obligation persists; reusable slot identity does not authorize an intervening vacant value |
| COUT-23 | A borrowed output forwards independent external backing | Keep that backing obligation; do not add a dependency on an unrelated hidden owner |
| COUT-24 | An output/reference would escape its supporting scope or declared backing | Reject; hidden storage does not extend a borrow |
| COUT-25 | An owner, guard, pin or task cannot validly retire at preparation/scope exit | Reject the missing obligation; no implicit join, detach, release or affinity change |
| COUT-26 | The ordinary return carries ownership/dependencies even though outputs are matched | Save one checked observation and retain its obligations; do not erase the result |
| COUT-27 | Lowering eliminates physical hidden storage | Preserve logical state, once-only evaluation, alias/root identity, observable cleanup and skip behavior |
| COUT-28 | Ordinary return is `void` while eligible outputs are lifted | Preserve one logical live/vacant unit observation position without inventing payload storage or a Boolean status |

## Availability and ownership-safe extraction

| Case | Situation | Required result |
|---|---|---|
| COWN-01 | One omitted output is initialized null | `var?`/bare lifted `require` preserves it; `var` fails its non-null test |
| COWN-02 | A declared normal failure initializes the selected output | Matching follows availability, not the Boolean/status name |
| COWN-03 | A wildcard ignores a vacant output | No read or initialization demand |
| COWN-04 | A wildcard ignores a live unique owner | Retain its scoped obligations; no destruction merely at the end of the arm/condition |
| COWN-05 | An ordinary value-binding pattern selects a unique output | Reject missing Copy permission; no speculative move or hidden unique alias |
| COWN-06 | An atomic Copy binding test fails before assigning its local | No new binding publication; preserve prior user binding state under ordinary pattern rules |
| COWN-07 | A unique output uses an explicit destination while a Copy output remains omitted | Match the Copy projection; move the named owner only after valid selection/guard |
| COWN-08 | The guard fails after the explicit owner was initialized | Keep the live owner in its ordinary caller scope; body move did not occur |
| COWN-09 | A guard explicitly mutates/moves/ends a destination | Keep those effects and invalidate affected facts; no transactional rollback |
| COWN-10 | A guard failure raises an exception | Apply ordinary unwind/cleanup once; do not replay the call or guard |
| COWN-11 | Bare lifted `require` selects one initialized unique output | Transfer it after availability checks, vacating the hidden source |
| COWN-12 | Bare lifted `require` has several outputs and a later one is vacant | Fail before transferring any required owning output |
| COWN-13 | All required outputs are initialized, including `void` or nullable values | Yield the complete one-value/ordered-tuple shape with logical unit positions and nullability retained |
| COWN-14 | Transferring an owner/view output group lacks a supported dependency-preserving move/aggregate contract | Reject; availability alone does not authorize relocation or lifetime retargeting |
| COWN-15 | A pattern requirement selects only one Copy output and ignores another | Require only the pattern's stated availability/presence; keep ignored live obligations |
| COWN-16 | Output-binding type changes under repeated evaluation | Reject; a mutable binding does not repeat inference |
| COWN-17 | A fixed pattern binding remains live on a path to another successful match | Reject replacement unless ordinary vacancy is established |
| COWN-18 | A selected requirement exception is requested, but call evaluation itself throws | Propagate the call failure; the selected exception handles the false requirement, not evaluation exceptions |
| COWN-19 | An ordinary Boolean statement `require` is used | Consume its predicate as a check; no omitted-output projection or invented Boolean result |
| COWN-20 | A Copy pattern succeeds but a following ordinary Boolean guard is false | Keep its completed binding/effects in that supporting scope; an arm-local activation instead retires normally on arm rejection |
| COWN-21 | `var result = require Ready();` has no omitted outputs and Ready returns true | Evaluate once and bind logical void/unit, not the checked Boolean |
| COWN-22 | The same ordinary value-form predicate returns false | Raise the selected requirement failure; no normal unit or Boolean result |
| COWN-23 | `require Read(init destination)` supplies every destination and returns true | Check the ordinary Boolean return, yield unit, and retain caller destination values without extraction |
| COWN-24 | That explicit-destination call returns false with a declared live failure output | Raise the requirement failure with the actual published state and ordinary unwind; no universal vacancy rollback |
| COWN-25 | A value-form requirement has no omitted outputs and ordinary return is a status enum, integer or unit | Reject without an explicit Boolean predicate; no truthiness or invented success group |
| COWN-26 | An eligible lifted bool output is initialized with false | Yield false after its availability check; do not reinterpret payload as the predicate or yield unit |
| COWN-27 | A bool destination expects the result of an ordinary value-form Boolean requirement | Reject unit-to-bool binding; the predicate does not become its result |
| COWN-28 | A void callable or func<void> returns an ordinary value-form Boolean requirement | Permit its logical unit result, preserving the predicate's evaluation and failure |

## Exact hidden-owner traces

These finite fixtures use a synchronous unique owner with NoThrow cleanup,
an independent Copy count output, and no retained loans. Successful calls
initialize both outputs; normal false calls leave both vacant. The statement
subject is `TryRead(arguments) is (_, mut var count)`, so repeated scalar
binding is allowed. Argument staging and cleanup have no additional hidden
effects. A fixed preallocated trace sink does not allocate, block or throw.

| Event | Observation |
|---|---|
| `Sk` | All written arguments for evaluation `k` have staged |
| `Ik` | Callee invocation `k` starts |
| `Ck` | Complete output owner tagged `k` is constructed |
| `Qk` | That call normally returns its specified status |
| `Bk` | Original body executes with that count |
| `Gk` | A registered body guard retires |
| `Dk` | Output owner tagged `k` retires exactly once |
| `Fk` | Argument staging fails before invocation `k` |
| `Jk` | An invocation-only receiver check fails |
| `Ek` | Invocation `k` fails before complete publication |
| `Pk` | Acquired partial state is cleaned |
| `A` | Following statements in the existing supporting block execute |

Normal fixtures leave that block immediately after `A`; failed fixtures
unwind out of it. No event denotes storage reclamation or forced GC.
Unlike [materialization traces](source-materialization.md#observable-owner-traces),
output preparation retires the old owner **before** the next invocation's
construction, not between a successful RHS construction and assignment.

| Case | Fixture | Exact trace |
|---|---|---|
| TRACE-LIFT-01 | First test false | `S1 I1 Q1 A` |
| TRACE-LIFT-02 | Tests true, true, false | `S1 I1 C1 Q1 B1 S2 D1 I2 C2 Q2 B2 S3 D2 I3 Q3 A` |
| TRACE-LIFT-03 | First test true; guarded body breaks | `S1 I1 C1 Q1 B1 G1 A D1` |
| TRACE-LIFT-04 | Tests true, false; guarded body continues | `S1 I1 C1 Q1 B1 G1 S2 D1 I2 Q2 A` |
| TRACE-LIFT-05 | First test true; later short-circuit skips the call | `S1 I1 C1 Q1 B1 A D1` |
| TRACE-LIFT-06 | First test true; second argument staging fails with partial acquired state | `S1 I1 C1 Q1 B1 F2 P2 D1` |
| TRACE-LIFT-07 | First test true; second invocation-only receiver check fails | `S1 I1 C1 Q1 B1 S2 J2 D1` |
| TRACE-LIFT-08 | First test true; second invocation fails after partial acquisition | `S1 I1 C1 Q1 B1 S2 D1 I2 E2 P2` |
| TRACE-LIFT-09 | Two independent block-supported sites run normally then reach `A` | `S1 I1 C1 Q1 S2 I2 C2 Q2 A D2 D1` |

TRACE-LIFT-02 distinguishes vacant initialization from build-before-drop
assignment and per-iteration retained slots. TRACE-LIFT-03/05 distinguish
loop exit/skip from supporting-block lifetime end.
TRACE-LIFT-06/07 distinguish failed staging/checks from entered invocation;
TRACE-LIFT-08 shows that output retirement is not rolled back.
