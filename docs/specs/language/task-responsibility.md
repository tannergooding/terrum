# Task ownership, observation and consuming await

This chapter owns logical `task-responsibility/1` and
`consuming-task-await/1`. Core Task<T> is an affine completion owner.
Ordinary await consumes that owner, supplies T or propagates terminal failure,
and settles the operation's checked completion obligations.
[Async execution](async-execution.md) owns establishment, eager execution and
failure channels; [dependencies](dependencies-and-views.md) and
[flow](initialization-and-flow.md) own backing and place/version relationships.
No task destruction implicitly blocks, suspends, cancels or supervises work.
The shared [definitions](../language.md#definitions-and-notation) distinguish
ownership from non-owning reference access and the lifetimes supporting it.

## Core identity and value contract

The resolved Core family is Terrum.Threading.Tasks.Task<T = void>.
Its registered identity, complete payload type and checked operation contract,
not an Async suffix or similarly named methods, authorize these rules.
Bare Task is Task<void>, not a base type or erasure of another payload.
Task<write C> retains that exact access-qualified T. There is no automatic
payload covariance, Task<T>-to-Task<void> conversion or owner-parameter variant.

Task<T> is not Copy. One live value owns the operation's completion, residual
cleanup, failure-observation and untransferred payload obligations. A non-owning
reference to a task, or an observer, does not own them. The runtime's execution state is not a second
independently transferable completion owner. Wrapping/storing an owner
preserves its obligation; owning the containing value is not permission to
abandon its task fields.

Formation requires an admitted complete payload/storage/cleanup and task
profile, including all actual borrowed dependencies. T need not be Copy or
universally ManagedStorable. A zero-data payload still needs its ordinary
initialization/publication, and default bits do not create a task or operation.
Optional absence is not a cold task. This initial contract selects no cold-task
constructor/Start API, task object layout, public awaiter type or allocation.
Inline or allocation-elided lowering must preserve the same identities and
responsibility transitions.

Core's synchronous readonly IsCompleted getter has logical bool result.
For a live task it reports whether that exact operation is terminal, after
its required execution cleanup and completion checks. Once true it stays
true for that operation; owner transfer or storage reuse cannot attach the
observation to a new one. The getter neither transfers responsibility nor
supplies payload, failure-handling or completion-output discharge facts.
It is not a general Pure annotation, synchronization edge or sharing grant.

## Existential Task view

In an admitted type position, `Task<_>` is a per-value existential view of
`Task<T>` with a hidden complete payload type. It is not `Task<void>`, a
payload conversion, or a direct-call inference hole. A generic call argument
`_` continues to request checked inference; `Task<var T>` in a pattern
continues to capture a type argument.

The view retains the exact operation, its hidden T, and the corresponding
required witness/evidence for that value. Two values with different hidden
payload types do not share one artifact-wide inferred T. Each value preserves
the ordinary task owner, dependencies, failure and cleanup obligations. Its
physical representation may use a box, tagged representation, specialization
or another admitted form; no particular allocation or representation is
required if the same correlation and responsibility contract is preserved.
A persisted consumer must receive typed hidden-type/witness evidence or
reconstruct it through a checked path; it must not recover it from names or
private implementation bodies.

An existential view exposes only operations whose complete signatures and
contracts are valid for every hidden payload type. The selected common
observation includes `IsCompleted`. A completion-only consuming operation
may consume the owner, observe terminal completion, and settle and clean up
the hidden payload under the Task contract; it returns no hidden payload and
propagates terminal failure through the ordinary completion path. It is not
an implicit `Task<void>` conversion or permission to discard an arbitrary
affine result. Ordinary typed `await` still returns exact T and therefore
cannot produce an unknown payload from `Task<_>`. The exact Core member name
and signature for completion-only consumption remain outside this contract.
No other operation is admitted merely because its spelling matches a method
on another Task specialization.

## Establishment and responsibility transfer

Establish a responsible operation before executing an ordinary async body's
preconditions or body. Before returning its task, the callee/runtime retains
the responsibility; successful return transfers it to the caller's actual
destination. Pre-establishment failure must clean acquired state without
stranding work. Once established, failure is reported through completion
irrespective of suspension, under async-execution/1.

Task moves use ordinary explicit value transfer. A named owner must be moved;
a fresh result can transfer directly. A successful move vacates its source and
preserves the same operation, terminal state, complete dependencies, any
payload and outstanding observation/output obligations. It does not restart
work, create another operation or restore the old source after later failure.
Borrowing the source's storage can block its move independently of operation
completion.

By-value call parameters, initialized owning outputs, returned owners and
owning fields inherit this obligation. Check every consuming callable/holder
against it on every permitted exit. An ordinary consuming parameter is not
an unchecked detach API: its callee must settle or preserve/transfer the owner.
A receiver mutation before failure can already have transferred responsibility
into retained state; no rollback is implied. Captures grants no new ownership.

For example, this ordinary synchronous forwarder transfers an existing
operation, without a new async frame or a payload conversion:

```terrum
public static Task<T> Forward<T>(Task<T> operation)
{
    return move operation;
}
```

A non-async Task-returning factory retains its synchronous invocation/failure
phase. The registered Task family supplies the operation's responsibility
and possible borrowed-input capture relationship, not a completion-phase
init-output relationship or new clause phase. A returned owner must preserve
the factory's checked source bounds whether it creates or forwards work.
An explicit narrower capture contract is checked against the implementation.

## Consuming await

Ordinary await of Core Task<T> consumes its operand under the ordinary
copy/transfer rules and has result type exactly T:

```terrum
int32 first = await ReadAsync();
Task<int32> operation = ReadAsync();
int32 second = await move operation;
```

ReadAsync in this fragment returns the admitted Task<int32>.
Bare `await operation` rejects for that named affine owner; await is not an
implicit move or a Copy adaptation. The consumed source is vacant on both
successful and failed await exits. `await move task` does not mean moving
the eventual payload before completion.

The initial Core wait is compiler-owned, with a private affine wait extent.
It is not discovered by public GetAwaiter/OnCompleted/GetResult name matching.
Evaluate the operand once; transfer the owner into that extent before
registering suspension or exposing pending wait state. Establishment or
registration failure must leave responsibility with a valid enclosing owner,
or settle it under a checked failure contract. It cannot discard the task.
The registration/trust/profile contract must establish this property before
affected use; this is not a new catch-and-ignore runtime fallback.

An already-terminal ordinary operation continues inline, including failure,
unless an explicit admitted continuation adapter requires dispatch.
Otherwise the consuming wait retains the owner, captured state and required
roots/guards/affinity through suspension until checked terminal observation.
An ordinary wait cannot be abandoned by injecting an implicit timeout or
cancellation. An explicit early-return adapter must preserve or transfer every
remaining obligation under its own admitted contract.

On success, transfer the complete payload and its dependencies to the await
result exactly once; retire settled task/wait bookkeeping without replaying
producer cleanup. Task<void> supplies () and an await of it is unit-valued.
Copy T is not automatically copied instead of transferred.
On failure, deliver the admitted failure to the awaiting flow under ordinary
unwinding and causal chaining, with no successful T or fabricated default.
Terminal cancellation, where separately admitted, follows that failure path.
An expected domain error returned as a successful payload is still data.

If the established operation executes
[consuming captured throw](captured-exceptions.md), failure delivery preserves
that same event, origin, causes and responsible payload closure, not a fresh
copy. A successful Task<CapturedException> instead transfers an ordinary
affine captured owner on success. Neither shape duplicates event ownership
or settles pending tasks/other obligations hidden in a captured payload/cause.

Payload formation/check failure cleans unpublished owning state before
terminal failure publication. Successful payload transfer cannot retain a
borrow into task/wait storage being retired. External borrowed payloads keep
their independently valid backing; owning payloads transfer the actual
resource/root obligations. No representation or hidden retain repairs a
short lifetime.

The consumed owner cannot be awaited again or used for a status query.
This does not prohibit independently admitted nonconsuming observations of
an owner still retained elsewhere.

## Observation, payload and output obligations

These are distinct facts for one exact operation/value version:

| Fact | What it establishes |
|---|---|
| Live task owner | Responsibility has an actual holder; not a completed operation |
| Terminal status | Execution cleanup/checks have completed; not payload access or error handling |
| Checked terminal observation | The actual success/failure and required correlated completion-output facts |
| Payload available | A complete untransferred T remains in the owner |
| Payload transferred or retired | That payload's owning obligations moved or completed; no second transfer |
| Responsibility settled | Completion, observation, output discharge and residual cleanup are accounted for |

A checked nonconsuming observer may repeat terminal classification/failure
inspection while its owning task remains valid. It cannot restart execution
or duplicate responsibility. A payload-copy operation requires complete T
Copy eligibility and duplicates its full dependencies, not its operation
owner, resource or output reservation. A non-Copy payload can be borrowed only
under the actual result-source contract or transferred once. Borrowing task
storage blocks conflicting take, move or destruction.

Specific nonconsuming payload/failure accessors and failure representations
require their own admitted contracts; this chapter does not select the
proposal's TaskOutcome/TaskAwaiter APIs. Repeated observation is not an implicit
Copy promise for an owning failure transport, nor repeated propagation with
invented error/stack identity. A delivered failure follows ordinary explicit
handling; task destruction never supplies that handling automatically.

Consuming await observes the exact operation and discharges its wait-owned
completion obligations on either terminal outcome. A selected
[async cursor output](async-iteration.md#completion-and-observation) publishes
true/live or false/failure/vacant under its exact formal/destination/version
relationship. Transfer through another holder or wait preserves that
attachment. A Boolean copy can preserve its saved output correlation; a
reused destination or moved task does not create a new publication.

After observation, published outputs and transferred payloads retain their
own cleanup/dependencies. Settling the task does not end a published element
loan, dispose its backing or discharge cursor close. Hiding a payload,
forgetting the Boolean or converting to another completion carrier must still
preserve or explicitly discharge every output/payload obligation under the
adapter's contract. Task shape alone supplies no omitted-output lifting.

## Every-exit settlement and ordinary destruction

Every normal and checked exceptional path that ends an owning task's extent
must settle it or transfer responsibility to a valid retained owner.
An exit that could abandon a pending or unobserved failed operation rejects.
The same rule applies transitively to aggregates and staged argument values.
Passing a later argument that can fail cannot leave an earlier staged task
without valid exceptional settlement/transfer.

Ordinary `_ = task-producing-expression`, `_ = move task`, `task = _`,
replacement and generated scope cleanup obey that rule. None inserts a wait,
block, cancellation, failure suppression, owner arena or supervisor.
Even a true IsCompleted observation alone is insufficient.
A still-live task may retire synchronously only when a checked contract
establishes terminal observation/output discharge and permits exactly-once
payload/residual cleanup without throwing or suspension. Merely remaining
reachable to a collector is not completion responsibility.

Authored async finally/defer can perform explicit consuming await before
dependent scope cleanup. For example, StartAsync below returns Task<void>:

```terrum
Task<void> operation = StartAsync();
try
{
    DoOtherWork();
    await move operation;
}
finally
{
    if (operation is init)
    {
        await move operation;
    }
}
```

The fragment is inside an async body and assumes admitted dependencies.
The state guard tests the binding's liveness, not task completion.
If DoOtherWork fails, cleanup consumes and settles the still-live task.
If the first await completes or fails, its source is vacant and cleanup
does not await it again. Await failure during cleanup follows ordinary causal
failure chaining; no earlier cleanup is replayed. Required waits precede
supporting root, backing and guard retirement.

An explicit structured owner/supervisor must accept the actual owner and
preserve its capture/output dependencies, failure policy, cleanup affinity and
shutdown obligations. Its name alone does not establish that protocol.
A join accepting several owners must settle all adopted operations even when
one fails; its admitted policy defines failure reporting/combination.
It cannot report completion while unresolved siblings are abandoned.
TaskScope, supervisor, multi-task join and shutdown APIs remain separate
catalogs, not implicit machinery for ordinary scope exit.

[Aggregate field cleanup](ownership-and-cleanup.md) preserves this boundary:
a partial structural owner retains every remaining task field's responsibility,
and complete authored Cleanup is nonthrowing/nonsuspending before automatic
field retirement. Neither order inserts a wait or settles a pending child.

This is not a termination, fairness or deadlock-freedom theorem. A wait can
remain pending indefinitely while its responsible owner and dependencies
remain valid. Responsibility cycles, a cancellation request or loss of
observable handles do not prove settlement. Fail-fast/process termination
has no ordinary cleanup/completion promise.

## Cancellation, placement and independent admission

Cancellation requests do not move ownership, complete work, release captures,
publish vacancy or erase a completed success/failure. Only the checked
terminal operation relationship establishes cessation and required cleanup.
No automatic parent/child cancellation or inherited cancellation token follows
from await. Request/token APIs, cancellation failure representation and
timeout/race/supervisor policies remain separately admitted.

[Sharing and handoff](../runtime/sharing-and-handoff.md) binds those duties
to actual capture, guard, return, code and placement contracts. Conditional
payload eligibility is not a task-family capability. Terminal/payload
observation does not independently release another protocol's reservation.

Task ownership is not ThreadTransferable, ThreadShareable, a thread-pool hop,
executor capture or a new synchronization edge. Every transfer, observer,
continuation and cleanup retains the existing complete sharing/handoff,
backing and affinity contract. An external supervisor cannot make caller-local
borrowed backing outlive its permitted scope. Custom awaiters/continuations
must have complete registration, effects, reentrancy, failure, ownership and
root/lifetime contracts before they become eligible; Task-like spelling alone
does not provide them.

Retained contracts preserve the exact Core identity and T, affine ownership,
consuming wait, establishment/failure phases, capture/output mappings,
payload/failure observation and every-exit settlement. Linking maps declaration
identities independently of rows; invocation binds actual operation, owner and
value/output versions. Replacement cannot turn consuming await into copying,
drop responsibility or rely on hidden blocking/cancellation.
An implementation cannot claim support for unsupported required rules or
task/awaiter representations. The producer is responsible for published
implementation truth; consumers validate selected metadata structure,
references and support before affected use, then may rely on those facts
without semantic re-proof.
[Metadata](../representation/logical-records.md#task-responsibility-encoding-pending) assigns no
physical task/awaiter/root record, feature bit, table or opcode.
[Conformance cases](../conformance/source-task-responsibility.md) are logical
requirements, not compiler/runtime or physical codec conformance.
