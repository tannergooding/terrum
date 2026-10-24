# Generated cursor producers

This chapter owns logical `generated-producers/1`: named compiler-generated
affine cursors, source producer registration, capture and creation, lazy body
entry, yielded output, completion and close. [Value](iteration.md),
[async](async-iteration.md) and [lending](lending-iteration.md) iteration own
their exact Core roles and caller output/backing contracts.
[Async execution](async-execution.md) owns the established-operation failure
boundary. No ordinary member name or unregistered attribute creates a producer.

## Owned named cursor registration

A cursor's named unique shell does not infer a thread capability.
[Sharing/handoff contracts](../runtime/sharing-and-handoff.md) preserve each
capture, guard/return endpoint, code lifetime and use/cleanup affinity through
creation, advancement, suspension and close. Explicit nominal opt-in still
needs those full admitted obligations for every condition-satisfying instance.

Core supplies two resolved, no-argument, nonrepeatable semantic schemas:

| Schema | Effective target and meaning |
|---|---|
| IteratorCursor | Primary namespace-level `mut unique struct`; compiler-owned cursor frame and implementations |
| Iterator | Primary callable with a completed signature and producer block body; sole producer of that named cursor |

Inline and exact type-targeted IteratorCursor placement denote the same type
contract. Iterator applies to the callable, not its result or a parameter.
Secondary partial parts cannot introduce, replace or duplicate either primary
contract. Same-spelled user attributes, aliases, imports and extensions cannot
author or override a generation record.

The cursor shell and producer belong to the same defining module. The shell
is an ordinary explicitly declared nominal type, with its normal namespace,
arity, source-unit/access and type identity. Its definition associates with
exactly one producer callable definition, not one overload name, body shape
or concrete specialization. Missing or competing claims diagnose, including
equal signatures/bodies. The registration key is the exact cursor definition;
repeated imports of one record deduplicate, not reselect by import order.

The shell selects exactly one of the six already-admitted direct cursor roles:

| Role | Producer timing | Yielded output type |
|---|---|---|
| ICursor<T> | Synchronous | T |
| IAsyncCursor<T> | Async | T |
| ILendingCursor<T> | Synchronous | ref<T> |
| IWritableLendingCursor<T> | Synchronous | write ref<T> |
| IAsyncLendingCursor<T> | Async | ref<T> |
| IAsyncWritableLendingCursor<T> | Async | write ref<T> |

The producer's declared normalized result is that exact shell, with its
complete arguments. Async roles require the source async modifier; synchronous
roles do not admit it. Task<Cursor> and sequence-factory results are not this
contract. No role fallback, associated item inference, interface erasure,
boxing or obligatory heap allocation follows.

```terrum
[IteratorCursor]
public mut unique struct PairCursor : ICursor<int32>
{
}

[Iterator]
public static PairCursor Pair(int32 first, int32 second)
{
    yield first;
    yield second;
}
```

The compiler supplies the private state and exact public mut advance/close
implementations. A shell may declare those required members without bodies
to author their ordinary contracts/effect annotations. This is a restricted
generated-implementation declaration, not permission for bodyless ordinary
concrete methods. Async slot declarations use async to identify their
generated async implementation and completion-clause phase:

```terrum
[IteratorCursor]
public mut unique struct PositiveCursor : ICursor<int32>
{
    public mut bool TryMoveNext(ref<init int32> item)
        ensure !return || *item > int32(0);
}
```

That shell still needs its one checked producer. Omitted required slots use
their inherited Core contracts; written slots must identify and preserve
the exact requirement, signature/access and output relationship. They cannot
overload, replace or provide another role. Ordinary nominal witness mapping
governs labels and exact formal identities.
Additional requires cannot narrow the inherited caller domain, including
the states on which close must be valid. Additional ensures must hold for every
admitted generated instance and affected operation exit.

Initial shells have no authored fields, constructors, cleanup methods,
additional members/conformances, Copy promise or user layout controls.
Only the associated producer creates an initially live cursor; ordinary
default/type-call/new construction is unavailable. A vacant declaration is
not a fabricated cursor. A wrapper/adapter is the route for custom state,
additional APIs or another role. The actual generated contents still determine
supported storage/layout, tracing, transfer, sharing and affinity capabilities;
the marker grants none of them.

### Generic definition and code ownership

For a generic producer, its enclosing and own type/constant formals map
bijectively to the shell's arguments as direct formal references, permitting
reordering but not omission, repetition, fixed instantiation or wrapping a
formal in another constructor. Their declared domains and constraints must
be equivalent under the checked mapping. A nongeneric producer uses a
nongeneric shell. Defaults at ordinary call/type uses retain their own rules.
This initial mapping preserves every generic choice that can change captured
types, private frame layout, operation availability or complete capabilities.
It is not inverse inference from a body or an erased frame recipe.

Bind and check the complete pair under that shared definition contract for
every admitted instantiation. A stronger private body requirement, convenient
specialization or future output context cannot repair it. Ordinary forward
substitution fixes the complete result before body generation.
Constant formals use only the already-admitted domains.

The transformed producer body retains its original semantic code owner,
source unit and access authorization. Physically placing code in cursor
methods does not grant their authored slot contracts or unrelated code access
to the producer's private/file-local state. Generated fields have no specified
source-visible names. Lowering is not a new friendship or source-declaration
scope, and debug names cannot carry this ownership contract.
[Runtime require defaults](requirement-failures.md) retain that same producer
owner for its creation clauses and transformed body. Physically executing a
body check in an advance/close member does not select that member's default.
Separately authored shell-member clauses retain their own exact member
default; neither it nor a caller/type/module is ambient producer policy.
Ordinary failure construction retains the creation/operation failure channel.

## Creation and captured state

Producer invocation evaluates arguments/defaults and performs ordinary
params construction once, in source order. It captures the staged by-value
values with their actual Copy/transfer rules and retains each borrowed input
and receiver under the published capture relationship. A named affine input
requires explicit move. The complete frame owns transferred arguments; it
does not copy them to make a sequence factory.

All supplied input/receiver capture bounds remain caller obligations through
their declared release and required completion/close. Private body usage
cannot publish a shorter bound. Descriptor/reference materialization retains
its original supporting scope and backing, not a new per-cursor arena.
Default confinement, Copy or rooting cannot repair an invalid capture.

[General dependency schemas](dependencies-and-views.md) use the creation
cursor as this callable's returned/retained destination, not an async payload.
They cannot remove the inherited complete input/receiver capture relationship
or close responsibility. Any backing-only claim still has to preserve the
actual captured frame state and every guard/owner required through release.
Later generated outputs use their own exact member/formal/phase contracts.

Standard producer declarations do not capture `ref<init T>` formals: their
ordinary invocation output contract cannot be deferred into later advancement.
Use an explicit eager wrapper for ordinary creation outputs. This does not
restrict the generated advance operation's separately selected init output.

Creation is synchronous even for an async producer body. After capture/frame
responsibility is established, execute the callable's require clauses, then
complete cursor construction and its ensure clauses. Contextual return in
those ensures denotes the complete cursor. Phase bindings and temporaries
have ordinary isolated scopes; they do not become producer body locals.
The body and its scopes have not run.

Creation failure uses ordinary synchronous failure and private/partial,
captured-owner and phase cleanup. Moved caller sources are not restored,
and unentered body finally/defer actions do not run. Failure of a creator
ensure prevents cursor publication and cleans its unpublished captured state.
No async work or close obligation can be stranded behind that failure.
Capture admission includes every possible creation failure. An owned input
requiring async release needs an admitted synchronous failure-release/transfer
contract, or proof that no checked creation failure remains after its transfer.
Marking the later body async does not make creation cleanup asynchronous.

Successful creation establishes one affine traversal and its close
responsibility. It is still unstarted. Factory/reusable sequences are explicit
wrappers with valid Copy/borrow/recreation and required source lifetimes;
readonly GetEnumerator cannot implicitly consume affine captures.
Obtaining or adopting a cursor does not run the producer body.

## Lazy body and yielded output

The first valid advance starts the body. Later advances resume after the last
yield and run until another yield, normal completion or escaping failure.
Yield is suspension of that producer activation, not an exit from its entered
lexical scopes or a fresh invocation of the source callable.
Locals, parameter states, active handlers and registered defer actions retain
their actual state and dependencies. A reentered loop block has ordinary
fresh activation; resuming the same block does not reinitialize it.

`yield expression;` evaluates its operand once in the exact output type
selected above. Value output uses ordinary contextual typing and Copy/transfer;
it cannot extract an owner from borrowed collection storage without authority.
Lending output constructs a valid descriptor to an existing live T place.
`ref place` retains that place's actual immediate access, attenuating only
where the expected descriptor permits it. No invalid ref<void>, absent
optional-as-exhaustion, hidden-output-slot borrow or authority upgrade occurs.

The yielded value/descriptor stays private until the selected advance's
required checks and operation cleanup permit publication. Suspended body
scopes remain live; do not clean them merely to implement an advance return.
A failure after operand formation cleans the unpublished owning value or
loan under its real contract, then unwinds affected producer state.
Loan retirement never destroys a borrowed referent. A cursor may later retire
its own scratch element after all dependent loans end.

Yield is invalid in finally/defer cleanup, declaration predicates or a
nested non-producer callable. A nested callable needs its own eligible
producer declaration; it does not inherit the enclosing marker.
Yield may retain entered try/catch scopes when their failure state, backing
and cleanup remain valid across suspension and close. Async body cleanup may
await only under its declared async contract; it cannot yield an item while
closing. Yield is not the scheduler operation Yield() and promises no hop.

[Lexical Capture](captured-exceptions.md) retains that source handler
activation's actual owning/transferred state across a valid yield/resumption.
Moving a captured owner to the consumer does not restore source responsibility
or permit the suspended frame to keep a payload borrow that consumer
retirement could invalidate. Its later resume/close must remain dependency-
and cleanup-valid; capture does not manufacture a new frame, pin or thread
capability. Synchronous versus established-async advancement still selects
the escaping failure channel.

Every suspension point must leave the cursor in a state its declared close
can settle and clean. A synchronous cursor cannot retain obligations requiring
unperformed asynchronous cleanup; use an async producer or explicitly
transfer that responsibility under an admitted contract.
Yield does not implicitly cancel/join work or acquire a pin, guard or root.

## Advancement, completion and clauses

Each advance checks its own requirements and output preparation, not the
source creator's clauses. True publishes exactly one item/descriptor with
the selected output/backing relationship. For lending, the conservative
cursor/frame bound and every surviving loan constrain later advance/close
unless an available checked published refinement applies.
Frame stability is not immutable contents, exclusivity, NoAlias or a longer
source/guard lifetime.

`return;` or unit fallthrough finishes the producer. There is no yield return,
yield break or producer return expression. Finish applicable finally/defer and
ordinary local/untransferred capture cleanup, then discharge the outer-body
normal-completion ensures at the
[semantic body-completion boundary](../language-contracts.md#3-contracts-bracket-semantic-body-completion).
Only after those succeed, run the generated advance's normal-completion clauses
over surviving cursor state and false/vacant output, and publish exhaustion.
An escaping failure skips the remaining normal-completion obligations;
completed cleanup is not replayed.

Outer-body normal-completion ensure obligations run on that successful body
completion, not at every yield or early close. Body require remains a point
check. Neither is a public per-item declaration guarantee.
Source callable ensure is a creation check; a generated advance ensure is
that operation's item/exhaustion guarantee. Its contextual return is bool,
or the completed bool for an async slot, not the original creator's cursor
or a synthesized item variable. Short-circuit guards still protect vacant
outputs. No entry snapshot or new clause-target syntax is introduced.

After normal exhaustion, production cannot restart. Later permitted advances
remain false/vacant without reentering the body or replaying its completion
checks/cleanup; their ordinary operation clauses may still run.
After an escaping advance failure, the traversal is failed and cleanup-safe,
not resumable. Failure is never a false item, silent retry or caught-success
fallback. Required close remains.

## Close, ownership and asynchronous work

Close is required once before retiring an owned traversal, including
never-advanced, exhausted and failed cursors. Loop/adopted-spread lowering
performs its selected close; manual consumers retain explicit close
responsibility. Moving transfers it, and discarding a cursor does not
discharge it. This obligation is independent of item/result ownership.

Close of an unstarted cursor releases captures without starting the body.
Early close of a suspended cursor unwinds only its entered scopes and releases
remaining captured owners. It does not execute remaining production code,
enter a later scope, yield an item or claim successful final completion.
Exhausted/failed body cleanup is not replayed. Cursor operation postconditions
run only on their ordinary successful outcome and see only surviving state.

Close failure preserves ordinary causal chaining and cleanup-safe residual
state; no implicit second close occurs. User-observable body cleanup must be
settled by the explicit close/advance contract. Generated owner destruction
is nonthrowing and nonsuspending bookkeeping/residual retirement, not a
substitute for a missing Close or CloseAsync.

Async advancement establishes an operation before executing its advance
preconditions/resumed body. Even an inline failure is failed completion.
Its exact init destination/cursor reservation survives until checked terminal
observation and cleanup. Published loans/results then keep their independently
declared backing and ownership. Repeated observation does not republish them.

Settle pending advance and item/loan obligations before invoking CloseAsync;
await that close before dependent cursor/header/source cleanup. Close may
suspend while settling its entered async cleanup, retaining required backing,
roots, guards, affinity and responsible owners. Cancellation requests or
abandoned waits do not end any of those extents.
[Core task ownership](task-responsibility.md) transfers the advance/close
completion owner into a consuming wait. Named tasks require explicit move;
every normal/exceptional exit must settle or transfer remaining work.
This does not consume the cursor or settle a surviving published loan.
No scheduler capture, implicit task-wrapped factory or synchronous blocking
bridge is supplied.

## Effects, retained identity and representation

The creator's Pure, ConstEval and NoAlias annotations govern its
synchronous creation extent, including its applicable checks/failure cleanup.
They do not promise pure/no-throw/constant enumeration or post-return input
separation. A generated operation's declared effects cover its actual resumed
code, captured accesses, output formation, applicable checks and cleanup.
Moving borrowed work into a private frame does not make its effects private.
Inherited effects, valid-input domains and evaluator support remain checked;
Iterator is not a ConstEval opt-in or a new evaluation-mode query.

The retained logical registration includes:

| Field | Required contract |
|---|---|
| Identity and authority | generated-producers/1, exact module/primary cursor and producer definitions, source code owner/unit, selected Core role/witness |
| Definition mapping | Complete result/item/output types, type/constant formal permutation, domains/constraints and definition checking |
| Creation | Synchronous argument/capture order, frame responsibility, require/ensure phases, failure and cursor publication |
| Captures | Exact value/receiver/formal identities and modes, move/vacancy, all storage/backing/root/guard/affinity/responsibility dependencies |
| Execution | Unstarted/suspended/exhausted/failed/closed states; lazy entry, typed yield, body completion and active-scope/cleanup ownership |
| Operations | Exact advance/close requirements, authored/generated clauses/effects, output version/publication and async establishment/observation |
| Loans and retirement | Broad or available checked refined origins/invalidation, surviving item/copy/reborrow, pending operation settlement and once-only close |
| Representation and admission | Complete generated frame capability/layout dependencies and supported rules; producer is responsible for published body facts |

Distinct producer definitions remain distinct named cursor identities even
with equal code, layout or emitted values. Private state-machine code/layout
may change only under the retained type/ABI and replacement contract; being
compiler-generated does not make it layout-independent. A physical copy,
rebind or remapping preserves the exact capture/phase/member/source identities
and outstanding obligations, not just a program-counter integer.

Physical frame/task/loan/root/cleanup programs, generated member records and
codecs remain separately gated. No held opcode/table slot, optional Attr data,
generic constructor value, erased carrier or allocator is activated by these
logical rules. [Metadata requirements](../representation/logical-records.md#async-execution-and-generated-producers-encoding-pending)
and [conformance cases](../conformance/source-generated-producers.md) retain
that boundary.
