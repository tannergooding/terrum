# Published predicates and pure evaluation

This chapter owns checked purity, evaluation modes, logical effect/read
records, published predicate admission and dependency-based reuse. Its logical
rule sets are `pure-evaluation/1`, `effect-footprints/1` and
`published-predicates/1`, with the parameter schema `noalias-parameters/1`.
They assign no metadata row, feature bit or opcode.
[Contract phases](../language-contracts.md#3-contracts-bracket-semantic-body-completion)
own execution and cleanup order; [structural flow](initialization-and-flow.md)
owns availability and saved outcomes; [constant execution](proof-and-constant-evaluation.md)
owns the admitted interpreter, value domain and resource accounting.

## Pure means complete input state

A selected pure evaluation, for a call satisfying its declared preconditions,
has no observable effects or escaping language-level exception effects.
Its normal results depend only on its complete input values, state observed
through its receiver or explicit inputs, immutable published data and its
selected rule/code/target semantics. Its result obeys the declared
value/representation contract; arithmetic NaN latitude is not an additional
payload promise.

The logical input is not just a reference's machine address. It includes the
state actually observed through that input. Reading `this._field`, `ref<T>`,
`implicit ref<T>`, a span or an object input does not inherently violate
purity. `implicit` changes argument adaptation, not this rule.
No `Stable`, receiver-only purity or `ReadsInputState` modifier is required.

An input may include a mutable referent without permitting the evaluation to
mutate it. Readonly access remains shallow. Writes through an independently
writable stored handle are still external writes, even if no receiver slot
is changed. The complete checked contract covers callees, selected operators,
getters, constructors, contracts and cleanup, not just direct statements.

| Operation in a selected pure form | Requirement |
|---|---|
| Read an available input or input-reachable region | Record its value/state and backing dependencies |
| Mutate evaluator/callee-private temporary state | Permit when it does not affect pre-existing external state or escape illegally |
| Construct a fresh logical result | Permit under its validity, publication, ownership and backing contract |
| Read immutable published data | Permit with exact identity, publication and lifetime dependencies |
| Read varying ambient state | Reject, including mutable statics, clocks, culture, environment and input streams |
| Write pre-existing caller or ambient state | Reject; disjointness from another input does not make the write pure |
| I/O, process termination or identity-producing allocation | Reject, including such effects hidden in helpers or cleanup |
| Volatile/atomic observation, synchronization, blocking or suspension | Reject; a readonly or shared path does not waive the effect |
| Escape a language failure on an admitted call | Reject the purity claim; a valid-input no-failure obligation must be established |

Private slot creation and result storage are not identity-producing object
allocation. This distinction does not allow a fresh resource, managed object
identity or callback registration merely because its allocation is local.
Ordinary input transfer, result construction and cleanup must retain their
actual contracts; a purity marker cannot make an observable destructor pure.

Purity does not prove termination. An invalid precondition can still fail at
its ordinary check. A valid-input failure bound does not imply an unconditional
`NoThrow` call when that precondition is not established. A false enclosing
`require` is also the requirement operation's failure, not a side effect of
computing its Boolean operand. Preserve these failures and their evaluation
points unless the applicable proof establishes their absence.
[Its failed-path `new E()`](requirement-failures.md) retains ordinary
constructor/default/static-init/cleanup effects and actual construction
failures. RequireException chooses a source-owned type, not a Pure/NoThrow
capability. Proof of Boolean computation alone does not discharge those
false-path obligations or permit predicate replay/side-effect removal.

[Lexical exception capture](captured-exceptions.md) likewise has an actual
implicit handler source and ownership publication, despite zero supplied
arguments. Its registration adds no Pure/ConstEval or unconditional NoThrow
creation promise. Include creation resources/effects, handler vacancy,
payload/cause dependencies, transfer and nonthrowing/nonsuspending retirement.
An internally handled preparation failure need not escape an enclosing
NoThrow body, but complete path/effect proof remains required.
Opaque storage or unchanged payload bits cannot justify eliminating that
responsibility transition or treating captured throw as a fresh payload Copy.

## Checked annotations and forms

`Pure` and `ConstEval` are resolved compiler-recognized Core schema identities,
not privileges attached to a matching attribute name. Both take no arguments,
are nonrepeatable, and author the primary callable/accessor contract.
They apply to ordinary named callables, individual accessors and eligible
initialization-only constructors. Type, field, parameter and result placement
cannot substitute for callable placement. A whole property may target its
getter through the ordinary `[get: Pure]` attribute form.
No new lambda annotation, static-initializer policy or callback-effect type
is supplied by these schemas.
An initialization-only receiver is a vacant result destination; this does
not annotate an ordinary live receiver's mutation as result construction.

`[Pure]` is an explicit, versioned authorization to select the checked pure
form. Ordinary execution may contain effects explicitly guarded out of that
form by the mode queries below. The selected form must satisfy purity on its
admitted input domain; an unguarded I/O path cannot be ignored merely because
one attempted evaluation did not take it.
All source and body structure validates. Unsupported required purity or
unproved safety diagnoses rather than accepting a name as evidence.

`[ConstEval]` is the explicit opt-in for Terrum source-required evaluation.
It implies pure-form support and additionally requires its actual eligible
program/intrinsic, input/result contracts and dependencies to be available.
A concrete request checks the executed path, value admission and resource
limits. It is not a promise that every input succeeds, every branch belongs
to the minimum constant domain, or every request terminates within its budget.
Constant execution can diagnose a known unsupported value/program path
without violating pure-form support. It cannot excuse an external effect on
a path admitted by the pure-form preconditions: guard that effect out of the
pure form or exclude the input through a declared requirement.
Evaluator exhaustion/tool failure is not an escaping exception of a
successfully executed language call, and still prevents required publication.

`[Pure]` alone may authorize JIT/AOT folding when the backend can establish the
result and materialize it safely. It does not opt that callable into Terrum's
required constant interpreter or promise a persistent constant-value codec.
`[Pure] [ConstEval]` is permitted but redundant in its pure-form capability;
the two distinct attributes each obey their own placement/multiplicity rules.

The ordinary, pure and constant forms preserve the declared value, outcome,
ownership, lifetime and compatibility obligations. Author authorization can
omit effects explicitly guarded out of a selected evaluation form; it is not
permission to omit unrelated effects, argument evaluation or cleanup.
Mode-dependent behavior must be part of the declared behavior rather than an
undeclared algorithm or result change.
The [floating-result contract](floating-environment.md) publishes a canonical
constant NaN recipe but no ambient status observation. It supplies neither
Pure/ConstEval author opt-in nor a complete helper/effect/resource program.
Sign-only Abs/negation and copies keep their representation guarantees in
every admitted form. [Numeric capability groups](numeric-capabilities.md)
likewise provide operation evidence, not automatic evaluation authorization.

## Evaluation modes

The resolved Core `RuntimeHelpers.IsPureEval` and
`RuntimeHelpers.IsConstEval` queries are static readonly Boolean intrinsic
properties, not mutable runtime fields. A same-named property has no special
meaning. Querying the mode creates no runtime static-initialization group or
ambient-state observation. Its intrinsic recipe reads the immutable selected
mode, with ordinary getter activation/read/return accounting and no I/O,
allocation, synchronization or new backing.
Its logical program is accessor entry, one read of the selected Boolean,
ordinary save/publication of that result and activation retirement.
The [constant action rules](proof-and-constant-evaluation.md#semantic-actions-and-normalization)
charge those events and the Boolean result/frame state normally; repeated
queries do not receive a caching discount.

| Selected evaluation | IsPureEval | IsConstEval |
|---|---|---|
| Ordinary execution | false | false |
| Authorized pure form, including nonconstant inputs | true | false |
| Selected constant evaluation, including Terrum or authorized backend evaluation | true | true |

The queries report the semantic form, not annotation presence, optimizer
level or whether a machine instruction happened to be eliminated.
An ordinary call to an annotated member still uses false/false unless the
authorized evaluation form is selected. Ordinary semantics-preserving
optimization can also retain the ordinary form.
Constant mode always implies pure mode; false/true is impossible.
The mode is fixed for a selected evaluation and inherited by its evaluated
callees. Eligible nested calls do not silently revert to the ordinary form
or create an independent permission to execute an unsupported operation.
A published-clause evaluation is its own selected pure-expression activation.
On its completion, an enclosing ordinary invocation resumes its ordinary
mode; this does not change mode midway through either activation.

| Reached named call | Selected pure evaluation | Terrum required constant evaluation |
|---|---|---|
| Checked `Pure` contract | Use its supported pure form | Additionally require constant opt-in/body or an admitted intrinsic |
| Checked `ConstEval` contract | Pure-form support is implied | Use its admitted constant program/intrinsic and concrete path |
| No published purity contract | Need a sound closed proof of the selected execution's purity/equivalence | Ordinary unmarked call is unsupported; private body availability alone is not opt-in |
| Missing/incompatible required contract or body | Diagnose a required unsupported selection | Diagnose, never execute a runtime fallback |

A closed pure-region proof may admit a helper whose verified writes are
confined to that region's private temporary state. That does not publish the
helper as pure on arbitrary caller-owned inputs. Ordinary setters writing a
live external receiver cannot claim pure-form support merely because one
caller supplies a temporary. The constant interpreter likewise needs the
actual admitted operation/call contract, not permission inferred from that
temporary's address.

```terrum
[Pure]
public int32 Observe(implicit ref<int32> value, bool trace)
{
    if (!RuntimeHelpers.IsPureEval && trace)
    {
        Log(*value);
    }
    return *value;
}
```

This illustrative member authorizes omitting the guarded logging in its pure
form. Adding `[ConstEval]` also permits a required request with admitted
compiler-owned backing. Using only `!IsConstEval` to guard I/O would leave
that I/O in the true/false pure form and would not establish unconditional
pure-form support for that path. `IsConstEval` can instead select a
constant-compatible implementation over another permitted pure algorithm.

A published purity contract or a sound whole-program proof can authorize
selection. Without author authorization, a proof must establish equivalence
to the ordinary observable behavior and purity of the selected form.
An alternate branch that skips ordinary logging is not itself such proof.
Devirtualization, known current bodies or a constant argument do not grant
an unrecorded permission to discard effects.

## Read roots and effect records

[ThreadShareable and protocol admission](../runtime/sharing-and-handoff.md)
do not promise stable input contents or pure observation. Synchronization,
guard acquisition and atomic/ordered access retain their actual effects.
A helper reading an already established guarded/immutable input extent can
qualify only under its own checked pure read/effect contract; it cannot
silently acquire or restore authority during pure evaluation.

Each callable form has a checked logical record. Ordinary and selected pure
forms have distinct bounds; the pure marker does not erase the ordinary
form's writes or failures from consumers.

| Record field | Required meaning |
|---|---|
| Identity and rules | Exact callable/declaring-member identity, form and supported rule versions |
| Signature | Substituted receiver, ordered parameter modes, result and initialization destinations |
| Domain | Preconditions, actual access/validity and any checked form-eligibility requirements |
| Reads | Complete conservative set of logical value/state roots and retained region dependencies |
| Writes | Complete conservative set of externally affected roots/regions, separate from fresh outputs |
| Outputs | Normal result and per-destination live/vacant/value relationships |
| Transfers and retention | Input/result ownership, captures, backing, root, lifetime and retirement obligations |
| Activity bounds | I/O, process termination, identity allocation, ambient state, volatile/atomic access, synchronization, blocking, suspension and language-failure bounds |
| Entry obligations | Static initialization, publication, authority and load-lifetime demands not discharged by body purity |
| Availability | Checked program/intrinsic and value-ABI dependencies when execution requires them |

The minimum read-root vocabulary is finite:

| Root | Identity and binding |
|---|---|
| Receiver value/state | Captured receiver and its observed referent/backing, not a later reread of a rebound descriptor |
| Input value at ordinal `i` | Staged by-value input snapshot; `i` is the zero-based declared parameter ordinal |
| Input state at ordinal `i` | Storage actually observed through that parameter, including nested handles and view backing |
| Logical output at ordinal `i` | Initialization destination identity, availability and the constructed result/backing |
| Ordinary result | Saved result destination after semantic body completion |
| Immutable data | Resolved constant or published immutable region, its actual value/publication and load-lifetime identity |
| Evaluation mode | The predefined immutable mode input used only through its admitted query recipe |

Roots are semantic bindings, not physical addresses, source spellings or
private field names. Aliases map to actual backing regions. Reading a copied
scalar input depends on the staged value, not automatically on the caller's
current variable; reading a copied handle additionally depends on the
observed object state. A receiver-only record is insufficient when a getter
traverses independently mutable element handles.

A complete root can conservatively cover all relevant reachable state.
More precise checked field/range regions can avoid needless invalidation,
but precision must not omit a dependency or require private imported layout.
Shared immutable regions need not be treated as mutable regions merely
because several roots reach them.
The initial source annotations add no field-path/priority/conditional
read-selector language.

An absent ordinary effect record is conservative unknown, not empty reads or
writes. It invalidates all facts it may affect and cannot justify a required
pure call. A claimed pure form must instead have complete finite allowed
roots and checked activity/transfer bounds. Unknown effects cannot be repaired
by assuming a method name, readonly receiver or current implementation is pure.

## Aliasing, memory state and sharing

Ordinary `ref<T>`, `implicit ref<T>` and `write ref<T>` are aliasable by
default. `write` permits writes, but does not claim the selected invocation
actually writes or acquire an exclusive loan.
A pure form can read through multiple aliased descriptors. A write to
pre-existing caller storage is impure even when the target is disjoint from
every other input.

[Whole-source lifetime contracts](dependencies-and-views.md) are independent
of these read-state revisions. Backing-only can remove a borrow of source
storage while preserving mutable backing and guards; it does not stabilize
reads or authorize reuse across an invalidating effect. Captures into a
pre-existing holder is still an external effect, even with proved NoAlias.
Fresh private output state retains its separately checked result/capture bounds.

For reuse, compare the selected code/form, input values, read-state revisions,
immutable data, target/value semantics and surviving dependencies.
These revisions are an abstract analysis model, not runtime counters:

```text
Observe(reference, memory0)
Observe(reference, memory0)  // Same logical inputs can permit reuse.
WritePossiblyAliasingState()
Observe(reference, memory1)  // A relevant write changes the input.
```

Invalidate only facts potentially affected by writes, replacement, moves,
retirement, captures or changes to backing/publication authority. A checked
noninterfering effect preserves unrelated facts. Unknown aliasing invalidates
conservatively; pointer inequality alone does not separate reachable regions.
Same-thread calls and callbacks participate in this rule.

A declared disjointness obligation identifies exact roots, the relevant
storage/view extents and its active scope. The caller must establish it by
sound analysis or an actual checked capability/contract. It grants no new
write, ownership, lifetime or concurrency authority. It is needed only when
the required proof depends on separation not otherwise established.
The parameter `NoAlias` schema below supplies one such published requirement.
Ordinary reference modes still imply no disjointness.

The [memory model](../memory-model.md#1-nonconcurrent-access-is-the-default)
excludes competing access without opt-in. That permits ordinary unshared
input-state analysis, not a global assumption that every alias is unchanged.
For explicitly shared mutable state, the actual protocol must establish the
needed access and stability extent. Atomicity, readonly access or one observed
value does not make two shared reads reusable. A guard's release cannot be
ignored or implicitly delayed through postconditions.

Immutable RVA-backed data is permitted only with a checked immutable extent,
valid publication and retained artifact/load lifetime. RVA placement alone
is not immutability; a readonly static handle can still reach mutable data.
Runtime initialization demands remain observable obligations. Prove them
discharged or retain their required execution at the ordinary point; the
constant interpreter cannot run them or mark a runtime group complete.

## NoAlias parameters

Core's resolved `NoAlias` schema is an optional, no-argument, nonrepeatable
parameter attribute. It authors the primary parameter contract of a
synchronously completed named callable, accessor or constructor, including
its corresponding bodyless requirement. Matching user attribute names supply
no capability. Callable-wide, receiver, type, field and result placement do
not substitute for a parameter target.

```terrum
// Signature-only instance-member illustration.
[Pure]
public int32 Inspect([NoAlias] write ref<T> other);
```

At this invocation, mutable regions accessed through the staged `other` input
must be disjoint from mutable regions accessed through the receiver, every
other input and every initialization destination. The receiver need not have
its own annotation. Multiple annotated parameters impose the union of those
requirements: a pair needs separation when either endpoint is annotated.
NoAlias does not require regions reached through one endpoint to be mutually
disjoint from each other.

The requirement covers the whole invocation, including prologue demands,
declaration clauses, body/finally, parameter and phase cleanup, and normal or
exceptional completion. The caller establishes separation at the invocation
boundary after ordinary staging/preparation. The callee must preserve and
check the promised access extent; annotation is not a runtime discovery of
disjointness, a graph snapshot or permission to suppress an unresolved proof.
Unsupported or unestablished required separation diagnoses before invocation,
with no automatically inserted alias check or alternate-overload fallback.

### Accessed regions, not root addresses

Use the complete checked read/write/result-state footprint of the selected
form, with its actual referent/view/backing extents. A data region is mutable
when its state can change under permitted access, even when this particular
path is readonly. Include initialization/availability state identities
accessed through destination loans; zero payload bytes do not erase those
logical states. Pure-form fresh output construction is included even though
it is not an external-effect write.

Proven immutable published regions are exempt, with their actual publication
and load-lifetime contracts. Readonly or RVA placement alone is not that proof.
The same backing can therefore supply a shared immutable lookup table, while
distinct view/handle slots into mutable overlapping storage do not establish
separation. Compatible checked field/range extents can be disjoint within one
allocation; a root-wide conservative footprint may instead leave that proof
unestablished. Do not inspect private imported layout to repair it.

A by-value scalar/unit input has no external mutable state region merely
because its source was a caller local. NoAlias is legal but vacuous on that
input, including such a generic substitution. A checked empty mutable
footprint establishes its separation even when another endpoint has unknown
effects; it does not establish purity or discharge those other obligations.
A by-value handle or aggregate
with handles can have observed external state and retains those dependencies.
For an empty view, absence of payload access does not remove its backing,
validity, root or lifetime obligations. NoAlias is not a no-borrow contract.

### Binding, forwarding and changed aliases

Bind the annotation to its exact declared parameter identity and ordinal,
then the supplied argument after ordinary named mapping, substitution,
reference adaptation and output preparation. `[param(other): NoAlias]` and
inline placement target the same parameter; duplicate effective placement
rejects. A signature-declared indexer parameter contributes to each accessor
using it. An accessor-local selector, including a setter's actual `value`
formal, must resolve an exact applicable parameter target rather than guess
between property-wide targets. Primary authorship and multiplicity apply
to those effective contracts.

Normal or expanded `params` still binds one final formal. Its internal elements
need not be pairwise disjoint, but copied handles in fresh element slots retain
their actual backing and can overlap another call root. Default constants and
implicit materializations use their actual staged values/regions, not a
freshness claim from source spelling or compiler-owned descriptor storage.
NoAlias cannot authorize copying to evade an otherwise invalid borrow.

A descriptor copy preserves provenance, not an independently usable NoAlias
capability. A locally rebound formal refers to its actual new source; it
cannot inherit separation from the originally staged argument's identity.
Rebinding alone is not a violation: subsequent accesses belong to their
actual new provenance, not whichever parameter name now holds the descriptor.
Mutations that introduce overlapping accessed backing must not falsify the
published promise. Prove the resulting access plan preserves the requirement
or diagnose; never retain an old root-separation fact across such a change.
A forwarding call maps and discharges its own exact endpoint requirements.

The requirement ends with the invocation. Escaping views/captures keep their
ordinary declared dependencies, not a new post-return separation guarantee.
NoAlias does not supply a lambda/callable-value effect schema or a later
async/producer access extent. Those uses require a separately admitted complete
protocol; returning a task or callback does not silently extend this schema.

[Generated producer annotations](generated-producers.md#effects-retained-identity-and-representation)
on the source callable govern synchronous creation. Delayed advance/close
requires its own checked operation effects and actual captured-region
requirements; private frame ownership does not make external writes pure
or extend creator NoAlias. A Pure/ConstEval creator does not automatically
authorize pure/constant enumeration or emitted affine cursor values.

### Authority and compatibility

Unrelated caller aliases are not forbidden. NoAlias neither acquires exclusive
access nor grants write, ownership, pinning, lifetime, transfer, sharing or
concurrency authority. Same-thread callbacks and explicitly shared protocols
retain their actual effect/access obligations. A writable input write is
still externally observable and impure even when its NoAlias requirement
is established; read-only aliased inputs can be pure without NoAlias.

The attribute is a published call requirement, not parameter type identity,
an overload discriminator or an inference/ranking preference. After unique
call selection, discharge it along with other access/dependency obligations.
Opaque imports can publish its complete checked region/extent contract without
exposing their bodies. Required constant execution still needs separate
ConstEval/program/value admission.

Adding a NoAlias entry requirement restricts the valid caller domain and is
incompatible with a contract that admitted overlapping inputs. Removing or
relaxing that requirement can widen the domain only while preserving the
old domain's published separation, outputs, effects and dependencies.
Interface/default/variant implementations and qualified-self adaptations
must meet the retained requirement over their actual adapted regions.
Neither devirtualization nor changed qualification proves new backing disjoint.
The logical record preserves the annotated parameter ordinal, rule version,
invocation extent, region/state requirements and immutable exemptions.
It assigns no TypeSpec qualifier, Param flag, Effects bit or clause kind.

## Initialization destinations are results

`init ref<T>` is a logical result position. It lends a vacant destination,
not a live input that the callee may overwrite. Initialization/publication is
the result-return mechanism, including efficient multiple-result ABI lowering.
The pure outcome includes the ordinary result, each destination's declared
availability and each constructed value; vacancy is not a fabricated default.

The callee may build its result using private state and reads of admitted
inputs, then initialize the destination under its checked contract.
The outer initialization loan is not general writable access after the value
is live. Copying an input's writable handle into a fresh result does not make
writes through that handle private.
All output validity, backing, ownership and exceptional cleanup rules remain.

The destinations need not use newly allocated physical storage. They must be
vacant at the invocation boundary; preparing reusable hidden storage or
retiring an old owner retains its ordinary caller-side effects and order.
Supplying a live value cannot implicitly destroy it to make a pure output.

Vacancy does not prove pairwise disjointness. Overlapping destinations must
satisfy every checked output relation and access obligation: no double
initialization of a live place or incompatible live/vacant promises for the
same place. Overlap with existing input/backing also requires its actual
availability and alias proofs. Unsupported overlap diagnoses rather than
inventing independent slots or an unstated no-alias guarantee.

Computation reuse still initializes every requested output under its ordinary
copy/transfer rules. It must not reuse a reference into another call's result
without a valid dependency-preserving adaptation. Unique ownership, result
identity, cleanup and destination locations cannot disappear because the
computation is pure. Later task/producer publication is not ordinary output
completion and remains outside this synchronous pure-result rule.

## Published predicate subset

Declaration `require`/`ensure` clauses use the ordinary Boolean syntax with
this checked expression subset. They are pure-expression contexts: reached
calls select their supported pure form. Runtime clause evaluation uses
true/false mode; evaluation inside a required constant request uses true/true.
The clause's own success/failure control retains ordinary contract semantics.

The initial subset admits:

| Form | Required contract |
|---|---|
| Available scalar/unit/constant reads and ordinary projections | Exact type, initialization and source-visible subject |
| Primitive arithmetic, comparisons, Boolean operators and short-circuit composition | Actual scalar contracts, evaluation order and partial-operation checks |
| Initialization, presence and admitted type/pattern tests | Structural availability and ordinary supported pattern semantics |
| Ordinary reference formation/dereference and view/field access | Valid access, retained backing and complete input-state read roots |
| Property/indexer/helper/operator/conversion calls | Accessible selected identity, checked pure form, complete dependencies and valid call domain |
| Fresh inline results and initialization outputs | Checked pure construction and ordinary result/publication obligations |

Unsupported indirect-call/callback effect shapes do not acquire purity from
a currently known target. No new snapshot, arbitrary `old(expr)`, mutation,
move-pattern, identity allocation, suspension, I/O or private-state escape is
introduced. The subset supplies no new destructuring or type-query syntax.
Ordinary body `require` remains general executable Boolean code, with its
actual inherited evaluation mode; it is not automatically a published clause.

All named declarations used by a clause must be visible at the callable's
effective accessibility. A public pure `Count` getter can hide its private
representation while publishing a receiver read dependency. A private field
cannot become a hidden public precondition. Pattern phase locals remain
phase-local and do not become exported caller names.

Serialize the bound predicate as a typed logical expression graph: constants,
receiver/parameter/result/output subjects, selected member/operation and
witness identities, supported pattern/composition nodes and complete
dependencies. Ordinary names or a true Boolean from one execution are not
substitutes for that graph. Translate it to staged arguments and saved outputs,
not a callee's retired frame or an implicitly saved entry value.
An altered by-value formal cannot be exported as the caller's unchanged input;
postcondition use needs the established surviving input identity.

The consumer applies supported structural/scalar guarantees under the
[portable floor](proof-and-constant-evaluation.md#joins-loops-and-effects).
An opaque predicate can be a checked boundary assertion without becoming a
numeric intrinsic or a general theorem solver. Reuse of a backing-dependent
observation still needs unchanged read roots and its actual value contract.
Floating equality, for example, does not become reflexive merely because
the same pure value is observed twice.

Preserve declaration order, short-circuit paths, once-only evaluation and
phase cleanup. Facts required by the body/caller must survive later clauses
and phase cleanup or be reestablished without replay; otherwise diagnose.
Checks on invalid call domains and partial operations retain their failure
behavior unless absence is proved. A valid-input pure guarantee is not
permission to move such a check outside the guard establishing its domain.
Postcondition failure still does not rerun body cleanup or roll back effects.

## Dispatch, constants and independent consumers

Interface/witness requirements, defaults, inherited members and retained
qualified-self adaptations preserve the same pure forms, read bounds,
domain, outputs, mode-query semantics and transfer obligations. A variant or
replacement may not weaken them. Dynamic dispatch uses the published
requirement/retained contract; consumers cannot substitute stronger private
facts from today's devirtualized body.

Embeddable constants additionally require the target-profile
[Blittable commitment](../data-layout-and-abi.md#fixed-directly-interoperable-representations),
Copy/frozen validity and an admitted constant-value recipe. Private evaluator
state and input references need not themselves be emitted constants.
Copy, layout equality or pure evaluation alone does not provide a value codec.

Constant metadata preserves typed scalar/field/tag values and the promised
representation observations, not host object bytes or uninitialized padding.
Target codegen applies the selected endian, alignment, packing and layout
contracts. Native-sized values are checked against that target.
Pointer/address constants require a separately admitted symbolic/relocation
recipe; no interpreter address or process-specific bit pattern is persisted.

Documented constant-result behavior is a compatibility promise. Changing it
incompatibly is a breaking change even if the new algorithm is still pure;
semantic equivalence is not generally detected automatically. Changes within
a contract's remaining declared latitude are not newly frozen. Runtime
arithmetic NaN latitude does not extend to the selected deterministic constant
NaN recipe. Record actual evaluated code/value/target dependencies; preserving
a purity flag alone does not make an old computed answer the new answer.

An independent consumer validates identities, record shape, substitution,
required rule versions, dependencies and
[complete binding compatibility](../representation/binding-and-replacement.md)
before use. Extra supported forms or narrower effects do not erase exact
state/phase promises, specified validation failure or stronger facts retained
by an optimized caller. Producer checking does not waive bounded decoding or
admission. Missing mandatory
pure/constant/predicate semantics reject rather than falling back to optional
attributes, host execution or a weaker effect interpretation.
Physical records, constant programs and value codecs remain separate admission
work. No pair-selector or broader NoAlias annotation schema is supplied.

[Conformance cases](../conformance/source-published-predicates-and-effects.md)
cover these logical obligations independently of compiler support.
