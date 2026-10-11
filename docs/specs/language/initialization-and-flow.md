# Initialization, patterns, and structural flow

This chapter owns the required structural initialization/availability system
and binding-pattern storage semantics. It consumes [name lookup](names-and-binding.md),
[local declaration spaces](local-declarations.md), and
[call/output contracts](../language-contracts.md). It does not require a
general theorem prover, loop induction, or inference from private imported
bodies. Numerical proof rules and constant-evaluation budgets are separate.

## States, reads, and initialization

Each tracked place has a fixed complete type and a current value state.
Ordinary locals, parameters, initialization destinations, and structurally
identified construction storage are tracked by declaration/place identity,
not by comparing their bytes or display names.

| State | Meaning | Permitted ordinary operations |
|---|---|---|
| Vacant | No live `T` or retained acquired partial state | State test; eligible initialization; no `T` read, borrow, move, or `T` cleanup |
| Live | A complete `T` is initialized | Reads and other operations under its access, ownership, and dependency contracts |
| Maybe-live | Incoming paths include both vacant and live states | State test or a declared discriminator refinement; no unguarded value read, fixed-binding initialization, or unconditional `T` cleanup |
| Partial structural aggregate | Some required fields or complete-value validity are missing, while acquired fields remain | Checked field projection/state/repair and partial retirement; no ordinary whole-value use or whole-place initialization over live children |
| Reserved for pending initialization | A selected completion-output operation owns initialization/publication of this exact place/version | Preserve backing and operation responsibility; no caller state/value test, competing initialization, move, retirement or reuse |

The reserved state is supplied by an explicit
[completion-phase contract](async-iteration.md#invocation-and-pending-initialization),
not Task shape or private writes. Its producing operation can form/check its
private output under the granted init authority. The caller and its aliases
cannot treat this state as ordinary vacancy or observable maybe-live storage.
Only checked completion observation and required cleanup establish its
terminal live/vacant relation; IsCompleted or a cancellation request does not.

Moving or explicitly ending a value leaves its source vacant. This does not
erase its name, established type, or declaration permissions. Mutable and
initialization-capable declarations retain their distinct rules: `init T`
can initialize proven-vacant storage but cannot replace a live value;
`init mut T` also permits ordinary replacement.

A permitted assignment to maybe-live `init mut T` constructs the replacement
once, then retires the previous value only on paths where it is live.
It publishes a complete live value on every normally completing path.
Ordinary dependency checks must permit both the vacant and live possibilities;
conditional retirement is not a read of vacant `T`.

An ordinary read, reference formation, move, or value predicate requires a
live input at that operation's point. A state test does not read `T`:
`place is init` yields true on live paths and false on vacant paths. When
the place is maybe-live, its two outgoing edges refine those states.
Known initialization state alone does not prove non-nullness.
A [partial owning aggregate](ownership-and-cleanup.md) is not empty storage.
An inline whole-aggregate state test is false until it is complete, but its
false edge does not prove vacancy of remaining fields. A descriptor or owning
handle's binding test still tests that carrier, not the designated payload.
A null/value/type test must establish initialization before reading its
subject; matching a vacant eligible place is a nonmatch, not a fabricated null.

For a reference descriptor `r`, `r is init` tests the descriptor's own place.
`*r is init` tests the projected destination under the
[initialization-loan rules](references.md#initialization-destination-projection).
Place formation and descriptor validity checks still precede that state test;
state inspection does not make an invalid pointer/reference safe.
Neither state test can inspect a reserved referent. Testing an already-live
descriptor's own state does not inspect that referent.

[Lending loop aliases](lending-iteration.md#mode-selection-and-element-bindings)
have binding availability separate from their referred-to element's ownership.
Publication binds an alias to an existing live T place with its checked loan.
`alias is init` tests that binding; ending it makes the binding vacant without
vacating T. Later place/value uses require the live binding and surviving
backing. Neither ending it nor invalidating an old output-version fact erases
dependencies held by a copied descriptor or reborrow.

[Whole-source flow](dependencies-and-views.md#stored-values-and-flow) carries
dependency obligations separately from availability/read facts.
Joins keep every possible permitted source instance/version even when only
facts valid on all paths survive. Copies and stored wrappers retain those
obligations; ending one descriptor does not end another or make a vacant
sibling output a source. A stored capture can survive a call's exceptional
exit, so ordinary write/capture effects are not an init-output rollback.
[Failed runtime requirements](requirement-failures.md) and failures constructing
their selected E preserve those actual output/dependency states. Ordinary
zero-input construction does not clear initialized destinations or retire
reusable hidden owners at expression completion. All required lifted
availability checks precede owning extraction; partial and live-state cleanup
follows actual scopes, without vacant reads, source revival or cleanup replay.

[Sharing and handoff authority](../runtime/sharing-and-handoff.md) is separate
from value liveness. A retained live carrier can lack access to a reserved
referent. Joins preserve every reachable reservation, token and responsible
holder with its actual generation/path guard; a local state test or task
status does not establish remote return, guard release or payload publication.

## Transfers, joins, and cleanup

The structural analysis follows the specified evaluation/control-flow graph,
including source-order argument staging, short-circuit edges, normal returns,
failures, and loop transfers.

| Operation | Normal outgoing state |
|---|---|
| Vacant declaration | Vacant |
| Complete permitted initialization | Live |
| Successful mutable replacement | Live with the new value and its dependencies |
| Failed assignment right-hand side | Assignment has not retired its destination; permitted source-evaluation mutations remain visible, subject to subsequent unwind |
| Allowed structural field move | That field vacant; ancestors retain their partial state, other live fields and obligations |
| Checked field repair | That field live; whole completeness also requires all affected validity obligations |
| Move or `name = _` | Source vacant; completed obligations cannot be replayed |
| Conditional output call | States and discriminator relationships from its published normal-outcome contract |
| Selected completion-output invocation | Pending reservation with the exact operation/destination relationship; task return is not output publication |
| Checked completion observation | Live/vacant outcomes, saved completion discriminator and cleanup obligations from that exact operation |
| Lending descriptor publication | Live reference output with its exact element/source/invalidation relationship; no new T owner or loan of the output slot |
| Reached successful value-binding pattern | Ordinary initialization or permitted replacement of its binding |
| Failed or skipped binding pattern | Its prior binding state remains unchanged |

At a join, retain only facts valid on every reachable incoming edge.
Live/live joins remain live; vacant/vacant remain vacant; a mixture is
maybe-live. The complete type and remaining ownership/dependency obligations
must still agree. A no-normal-completion edge does not contribute to the
normal join. A structural move or cleanup on one path cannot be undone at
the join.
Structural aggregate joins retain each field's actual guarded state and
complete-value validity, rather than collapsing a partial path to whole
vacancy. Whole use requires complete state on every reachable path.

[Affine task moves and consuming await](task-responsibility.md) leave the
source vacant on successful and failed completion. Preserve each actual
operation's owner/capture/output obligations with its path guard.
`task is init` refines binding liveness, not terminal status or settlement.
Normal/exceptional scope edges require explicit completion settlement or a
valid responsibility transfer; ordinary maybe-live cleanup cannot implicitly
await or suppress an unresolved failure.

Retain every reachable pending initialization reservation and responsible
operation at a join, with its ordinary path guard. A pending/vacant or
pending/live join does not become ordinary maybe-live storage whose state can
be inspected. A caller use requiring availability must establish completion
observation/cleanup for each reachable reserving operation, or refine to a
path without that reservation. Equal task types do not collapse different
operation/output versions. Unsupported preservation of these guarded
relationships diagnoses; it does not discharge pending work.

Maybe-live storage retains path-sensitive cleanup responsibility. Scope exit
or defined unwinding performs `T` cleanup exactly on paths where that value
is live, preserving ordinary cleanup order. This can be expressed as a
state-guarded cleanup, not an invented `default(T)` or unconditional destructor.
If a value's lifetime already ended, its cleanup does not run again.
Partial structural owners instead retire only their remaining live fields.
Complete retirement runs authored Cleanup before reverse-declaration field
retirement; failed construction uses reverse acquisition without parent
Cleanup, under [ownership and cleanup](ownership-and-cleanup.md).
An implementation may use private state bits, but cannot expose a fake value
or bypass the ordinary dependency checks.

Fixed initialization therefore supports both of these forms. In the first,
`TryGet` declares true/live and false/vacant output states:

```terrum
init T item;
if (!TryGet(key, init item))
{
    item = Initialize();
}
Use(item);
```

```terrum
if (value is T item)
{
    Inspect(item);
}
if (!(item is init))
{
    item = Initialize();
}
Use(item);
```

Both examples require their successful/fallback paths to publish complete
values. Without the fallback or state guard, `Use(item)` may read vacant
storage. Unconditional `item = Initialize()` after the second example's
first `if` is also invalid: its fixed binding may already be live.

Loop headers join the initial entry and every reachable backedge. Analyze
their finite structural state to a fixed point; do not assume that a loop
runs once, terminates, or preserves a fact invalidated by another iteration.
`break`, `continue`, return, and failure have their actual destinations and
cleanup edges. This required loop join is not numeric induction or invariant
synthesis. A valid initialized-then-moved body local has a fresh state on
the next activation of its block.

## Producer suspension

[Generated producers](generated-producers.md#lazy-body-and-yielded-output)
keep one body activation across yields. Suspension retains reached local
live/vacant states, active scope/handler/defer registrations and actual
backing/responsibility. Resuming an existing block is not its reactivation.
Ordinary producer loop reentry still creates that block's fresh activation;
consumer loop item bindings are separate activations.

[Captured-event flow](captured-exceptions.md) separately tracks source handler
responsibility and each ordinary captured-owner place. Successful Capture
vacates the former and publishes the latter once; a move vacates only its
actual owner source. A maybe-transferred handler at a join cannot Capture or
bare-rethrow without ownership on every reached path. Normal handler exit
does not retire a transferred event. Suspension preserves those states rather
than reactivating the handler or recreating an owner from retained bytes.

Yield is not normal body completion or scope cleanup. Normal return/fallthrough,
escaping failure and early close take their specified distinct cleanup edges.
Private yielded output cannot become caller-live before its selected
advance publication/observation; pending async output stays reserved.
Saved output facts retain exact versions and cannot revive reused storage.

## Binding-pattern declarations and scopes

An ordinary typed or inferred value-binding pattern introduces a local
equivalent to `init T name`, with the complete matched/bound type.
Its `mut` form introduces `init mut T name`. `mut` controls the binding,
not the subject's referent authority:

```terrum
value is T item
value is mut T item
value is var item
value is mut var item
```

The forms are tests within expressions, not standalone declaration
statements. Typed/inferred/reference matching keeps its
[existing copy and explicit-indirection rules](../grammar-spec.md#33-statements-and-local-declarations).
There is no implicit owner consumption, arbitrary conversion, boxing, or
reference formation. A value-binding pattern without Copy permission cannot
silently move a unique subject merely to test it.

Its name participates in the relevant declaration space independently of
whether a match occurs. It is not confined to the successful body.
Whole-scope lookup, no-before-declaration, collision, and protected generic
formal rules apply. The binding starts vacant when that scope activates;
the declaration's source point is its binding declarator.

| Pattern occurrence | Binding's supporting scope |
|---|---|
| Expression, `if` condition, or ordinary `while`/`do` condition in a block | That existing block; later statements may test or initialize the binding |
| `for` initializer/condition/step | The existing `for` header scope |
| Iteration sequence expression | Its existing iteration header scope |
| Switch arm pattern or guard | That arm's scope, including its guard and body |
| Catch filter | Its candidate catch scope |
| Lambda/expression-bodied implementation | Its implementation activation, as for the corresponding source-equivalent block |

The same original scopes support the [hidden locals of omitted-output sites](calls-and-inference.md#hidden-destination-instances-and-retirement).
Their per-invocation destination states are separate from retained user
pattern bindings. Repeating a call cannot silently retire a named binding
to make an old hidden owner replaceable; its dependencies and fixed/mutable
rematching rules remain checked. A skipped call preserves storage, not a
new output observation or newly established pattern fact.

These scopes also apply to named type captures. A captured type name requires
available checked type evidence before any use as a type; name visibility
cannot supply a missing witness, representation, or runtime instantiation.
Complete captured identities/qualifications must remain consistent with the
binding's established complete type. `mut` qualifies value bindings only,
not a named type capture or a mutable type identity.

All declaration preconditions share one isolated precondition scope.
All declaration postconditions share a separate isolated postcondition scope.
Earlier clauses can establish bindings for later clauses in the same phase,
as if that ordered group of clauses had its own block. Neither scope extends
into the implementation body, the other phase, or caller code. No explicit
block syntax is required.

Their locals, materializations, and captured type evidence follow ordinary
scope cleanup and dependency rules before that phase completes. A failed
check uses ordinary failure/unwinding; no completed cleanup is replayed.
These names are not new parameters or implementation locals, nor named facts
exported to callers. Public facts refer to the declaration's eligible
formal/receiver/result identities.

## Successful, failed, and repeated matching

Evaluate the subject once at its specified point. When a binding's own
positive match succeeds, construct its eligible bound value and perform an
ordinary assignment to the introduced local. A vacant binding initializes;
a live mutable binding replaces build-before-drop. A live fixed binding
cannot be implicitly overwritten, including on a loop backedge.
Conflicting dependencies diagnose rather than retaining an old owner.

A failed match performs no assignment to that binding. A short-circuited
pattern is not reached and likewise leaves it unchanged. These outcomes
do not erase a prior successful value or imply that the binding is vacant.
Failure while evaluating the subject or constructing a replacement retains
the old value until ordinary lifetime end/unwind; effects are not rolled back.

Consequently:

```terrum
while (Next() is mut T item)
{
    Inspect(item);
}
if (item is init)
{
    UseLastMatch(item);
}
```

keeps the last successful match in the existing block. With no successful
match, `item` remains vacant. A final failed match leaves the earlier value
live; permitted replacement, explicit lifetime end, or scope exit retires it.
The fixed `is T item` version is invalid when the loop can successfully
reinitialize a still-live binding. Explicitly ending it in every continuing
body path can instead establish vacancy, under the ordinary dependency rules.
That explicit end intentionally discards the retained last match.

Each source binding site uses one local per supporting-scope activation.
A pattern site inside a reentered braced body has a new body activation,
not a retained collection of prior locals. Distinct sites cannot silently
merge declarations with the same name, including across `or` alternatives.

## Boolean and pattern composition

Ordinary Boolean `!` swaps true/false edges and their facts.
`&&` evaluates its right operand only on the left true edge;
`||` does so only on the left false edge. A conditional expression evaluates
only its selected branch. Join facts from the actually possible edges,
not from an imagined eager evaluation.
Saved Boolean results retain structurally established initialization
correlations while their relevant value/place versions remain unchanged.

Pattern `not`, `and`, and `or` use the corresponding negation and
short-circuit matching order on the same subject observation.
A positive binding still assigns when its own match succeeds under `not`;
negation changes the result edge, not whether that assignment occurred.
Thus `if (value is not T item) { item = Initialize(); }` can establish a live
fixed binding after the statement when the negative arm initializes its
vacant path and the other arm's positive match initializes it.

Every binding declarator introduces an ordinary declaration in its supporting
space. Repeating a name across `or` alternatives diagnoses a duplicate
declaration, even when the types and contracts agree or both bindings use
`mut`. Mutability permits assignment to an existing declaration; it does not
permit redeclaration or an implicit merged local.

Alternatives may introduce distinct names or different binding sets.
Their outgoing availability is the ordinary path join: a successful `or`
does not alone make a binding live if another successful path leaves it
vacant. A later state test can establish its availability.
To inspect several values through one binding, declare it once and apply
further tests to that value:

```terrum
if (value is int32 item && item is (0 or 1))
{
    Use(item);
}
```

Separate switch arms have sibling declaration spaces and may reuse a name.
There is no special common-binding declaration supplied by `or`.

Completed binding assignments and authored guard effects are not rolled back
when a later `and`, Boolean operand, guard, or alternative fails. Structural
flow retains those completed assignments. A compiler cannot secretly clear
or stage a binding to undo an earlier successful part of a compound pattern.
This does not permit a second declaration of its name in another alternative.

## Outcome correlations and invalidation

A saved call discriminator can retain its declared relationships with output
places. Testing that saved value refines those outputs according to the
particular call, not the method's name or today's body.
A direct saved copy of a supported scalar discriminator retains its
relationship while the relevant value/place versions remain unchanged.
Arbitrary arithmetic, getters, user operators, or reconstructed comparisons
do not acquire a correlation by spelling similarity.

For [async completion outputs](async-iteration.md#correlation-forwarding-and-ownership),
the discriminator comes from observing the exact operation after its terminal
checks/cleanup, not from invocation returning a task. Retain operation identity,
init formal/destination and place version through permitted forwarding and
saved Boolean copies. Physical task/slot reuse creates no relationship to an
old outcome; repeated observation cannot reinitialize or revive a moved item.
The pending reservation is released only by its admitted completion contract,
then ordinary correlated maybe-live flow and guarded cleanup apply.

Reassignment of a discriminator, replacement/move/lifetime end of an output,
or relevant possibly aliasing effects invalidate affected correlations and
value predicates. Reassigning one saved discriminator binding invalidates
relations depending on that binding's old version, not an independent
unchanged scalar copy of that result. Changing an output's relevant version
invalidates its relation with every saved copy.
A call's published write/retention footprint bounds that
invalidation; missing stable-observation guarantees cannot be supplied by a
readonly path or devirtualized private body.
Invalidating a relation does not physically make a live value vacant or
cancel its cleanup obligation. A later `is init` test can inspect current
state again without reading the value.

Contract-phase exit includes that phase's cleanup. Facts passed to the body
after preconditions, or published to the caller after postconditions, must
remain valid after later clauses and phase cleanup. A required published
guarantee invalidated by those effects must be reestablished or proved to
survive; otherwise the declaration diagnoses. Completed predicates cannot be
silently replayed to restore facts, and phase-local names do not become public
fact identities.

The mandatory structural system covers initialization, null/presence tests,
matching edges, finite joins, moves/early ends, declared output-state
relationships, and conservative invalidation. Additional sound numerical,
alias, or loop proofs may establish more, but cannot invent a value,
authority, shortened lifetime, or success-shaped fallback.
Unsupported required proofs still diagnose under the
[portable proof policy](../language-contracts.md#42-versioned-portable-proof-floor).

[Flow conformance cases](../conformance/source-initialization-flow.md) cover
these required outcomes independently of compiler implementation status.
