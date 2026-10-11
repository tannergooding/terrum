# Asynchronous value iteration

This chapter owns logical `async-value-iteration/1`: the standard
asynchronous by-value source/cursor roles and their completion-published
initialization output. [Value iteration](iteration.md) retains the common
nominal selection, exact item types, copy/transfer, backing and traversal
requirements. [Structural flow](initialization-and-flow.md) owns ordinary
place states and discriminator invalidation; [lifetimes](../lifetimes.md)
owns capture, completion responsibility and cleanup.

This contract is not inferred from Task<bool>, the Async suffix, an `async`
implementation modifier or similarly named members. It does not change
arbitrary task-returning output defaults, add general completion-clause
syntax, lift an omitted async destination or enable async collection spreads.

## Nominal protocol identities

Core's resolved Terrum.IAsyncCursor and Terrum.IAsyncSequence requirements
have these logical signatures:

```terrum
public interface IAsyncCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(init ref<T> item);
    public mut Task<void> CloseAsync();
}

public interface IAsyncSequence<T, TCursor>
    where TCursor : IAsyncCursor<T>
{
    public TCursor GetAsyncEnumerator();
}
```

The selected TryMoveNextAsync requirement includes a fixed
**completion-phase output relationship**: the item destination is reserved
by the returned advancement operation, and its terminal Boolean controls
publication. This is part of the inherited checked member/witness contract,
not an ordinary invocation-return postcondition over its task handle.
The resolved Core Task identity must provide the admitted completion,
observation, dependency and responsibility operations needed by these roles.
[Task responsibility](task-responsibility.md) supplies its affine ownership,
consuming await and every-exit settlement; the wait retains this exact output
reservation when the task is moved.
The signature alone supplies no physical task/frame representation or
complete general Task API.

There is no universal Copy item bound, separate IAsyncConsumingCursor,
Current property or Task<T?> exhaustion convention. True can publish a
valid absent optional item or zero-data unit item; false publishes no item.
The actual source/item contract determines whether a stored item can be
copied or an independently owned item transferred. A borrowed collection
is not consumed to satisfy this interface.

Async bodies and non-async implementations returning an operation both
satisfy the same inherited phase/output and capture obligations.
An ordinary `ensure` keeps its declaration's established phase: for an
ordinary async implementation, contextual `return` is the completed payload;
for a non-async declaration it remains that declaration's ordinary result.
The fixed inherited relationship does not reinterpret unrelated clauses
or provide a general source annotation for registering new phase contracts.
Producer creation, per-yield and final-completion clauses remain separate.

## Source selection and acquisition

`await for` admits an exact IAsyncSequence<T, TCursor> factory or an
already-created IAsyncCursor<T> value. Select one unambiguous complete
relationship under the common [source-selection rules](iteration.md#source-selection).
Repeated paths to one identity deduplicate; distinct factories/cursor roles
or item/cursor relationships diagnose. Synchronous witnesses do not supply
async operations, and an explicit item binding is not inverse witness search.
There is no implicit task wrapper, synchronous blocking or adapter discovery.

Evaluate the source expression once in its iteration header. A factory uses
ordinary readonly receiver evaluation without copying/consuming a non-Copy
collection. Invoke synchronous GetAsyncEnumerator once and adopt the complete
TCursor before advancing. Acquiring traversal state does not start a generated
producer body; asynchronous work belongs to its advance/close operations.
The source can be an explicitly awaited preparation expression under its
ordinary completion contract, but factory selection inserts no task or await.
Preserve the factory's actual effects and possible failure, and retain the
receiver/source backing, roots, guards, leases and affinity for every
dependency published with the cursor. Failure uses ordinary private/partial
result cleanup without calling CloseAsync on a vacant caller result.
The factory must not strand started work or an owned traversal that requires
unperformed asynchronous cleanup on a failed synchronous acquisition.

A direct cursor instead stages/adopts its existing traversal state under
ordinary value copy/transfer rules. A named affine cursor requires explicit
move; no factory call, reset, cloning or ownership rollback occurs.
The loop has the close responsibility after successful adoption, not
ownership of the original source collection. A Copy cursor retains its real
alias/backing and close semantics, not a presumed independent traversal.
The cursor/item forms must support header/output storage and the required
mutable receiver access without new write, pinning, sharing or Copy authority.

## Invocation and pending initialization

Prepare one vacant destination of exact complete T before invocation.
The call evaluates and stages its arguments once, checks access/dependencies,
and associates the selected member, exact destination place/version and
cursor state with a fresh logical advancement-operation identity.
Returning its task transfers completion responsibility, not a live item.
If invocation fails before returning an operation, the actual holder must
discharge any started work, reservation and private/partial output under
valid invocation cleanup, leaving caller output vacant and cursor cleanup-safe.
A synchronous return/throw cannot strand an operation borrowing caller storage.
Work whose completion/cleanup can remain pending must instead have a
responsible operation whose checked completion carries its eventual outcome.
[Ordinary async and generated cursor execution](async-execution.md) establish
that operation before user preconditions/body and deliver later escaping
failure through failed completion, irrespective of suspension. A non-async
implementation returning a task retains its ordinary invocation boundary;
it must still meet this requirement against stranded work/output.

While completion publication remains outstanding, the caller's destination
is **reserved for pending initialization**. The reservation overlays its
availability: it is neither an ordinary vacant place available to another
initializer nor caller-readable maybe-live storage.

| Caller operation on the reserved destination | Permission |
|---|---|
| Read, value/null/type test, borrow or move T | Not permitted |
| Initialization-state test, including `item is init` or a projected destination test | Not permitted |
| Overwrite, initialize through another loan, retire or reuse its storage | Not permitted |
| Preserve its place/backing and transfer the operation with its complete obligations | Permitted under the checked dependency/responsibility contract |
| Let its supporting storage/owner/guard end before completion observation and cleanup | Not permitted |

An initialization-reference descriptor's own `is init` test is still a test
of that descriptor, not of the reserved referent. The producing operation
can inspect/build its own private output under its initialization authority;
caller aliases cannot bypass the reservation to inspect those writes.
Even zero-payload T has an availability identity and reservation.

The operation retains the cursor's required mutable access and every
destination/source/root/lease/guard/affinity dependency through actual
completion and cleanup. No competing advance, close, reset or invalidating
cursor mutation may overlap it through any alias.
Distinct task variables, descriptor slots or object addresses do not prove
distinct traversal/output regions. Disjoint operations remain possible only
with their actual checked access/backing contracts; this is not a new
global exclusive-reference or NoAlias rule.

Missing reservation, storage, dependency or responsibility support diagnoses
before affected use. A task does not legalize a dead-stack initialization
destination, an unsafe transfer of nontransferable state or an unsupported
capture merely because execution happens on one serialized flow.

[General source/capture schemas](dependencies-and-views.md) can select
the exact initialized output value and its retained sources. They inherit
this completion/publication phase rather than changing it to invocation.
Backing-only forwarding does not release the pending destination reservation
or erase a guard/owner obligation.

## Completion and observation

The operation completes only after its body, applicable exit checks and
required operation cleanup have established one terminal relation:

| Observed terminal completion | Destination state |
|---|---|
| Normal true | One fully initialized valid T with its complete result dependencies |
| Normal false | Vacant; this traversal is exhausted |
| Checked failure or terminal cancellation | Vacant after private/partial-item cleanup; cursor remains cleanup-safe |

False is exhaustion, not a caught failure, temporary unavailability, a
successful default T or allocation refusal. An exhausted traversal cannot
resume production through another standard advance without a separately
declared reset/new-traversal operation.
Cancellation is terminal here only when the operation has actually stopped
and performed its required cleanup. A cancellation request, timeout or
abandoned wait does not complete the operation or release its reservation.
If success was already published, a later request does not erase its live item.
The cancellation request API and scheduling policy remain separate contracts.

Writes can occur before task completion, including directly in the reserved
storage under a valid lowering. They do not establish caller availability.
The operation/implementation tracks actual partial initialization and roots
for cleanup/GC; a hidden representation cannot expose a partially formed T.
A body, completion check or authored cleanup fault after item formation
prevents successful publication and cleans that unpublished item once.
Actual cursor mutations/effects are not rolled back.
Every checked advancement exit preserves state on which the required
CloseAsync and residual cleanup are valid. Required cleanup is not unsafe
recovery or permission to retry a failed traversal.

Physical completion can precede the caller's observation. Until a checked
completion observation supplies the terminal relation, that caller cannot
discharge the reservation or obtain a true/live fact merely by inspecting
a task status. IsCompleted alone does not supply the Boolean/output relation.
Awaiting the returned operation uses the admitted completion contract,
establishes completion/cleanup and supplies its saved Boolean discriminator:

```terrum
init mut T item;
var operation = cursor.TryMoveNextAsync(init item);
bool hasItem = await move operation;

if (hasItem)
{
    Use(ref item);
}
```

The example assumes eligible item/cursor storage and ordinary Use contracts.
The task source is vacant after that consuming await, even on failure;
the saved Boolean and output relationship are not a second task owner.
The true branch has the live T; the false branch has vacancy. Before
discrimination the caller can retain this correlated maybe-live state and
its guarded cleanup obligations. After completion observation, an ordinary
`item is init` can test current availability without reading T.
No mandatory copy, allocation, second output slot or task-to-union conversion
is introduced.

## Correlation, forwarding and ownership

The completion observation attaches the exact operation/destination version
relationship to its Boolean result. The ordinary [saved-outcome rules](initialization-and-flow.md#outcome-correlations-and-invalidation)
apply to supported direct scalar copies, Boolean branches and joins.
Overwriting the result binding removes that binding's correlation, not an
unchanged copy's; moving/replacing/retiring the destination invalidates all
relations to its old version. Losing proof never makes a live owner vacant
or cancels its cleanup responsibility.

Control-flow joins preserve each reachable reservation with its path guard,
responsible operation and exact output version. A branch that did not start
advancement contributes ordinary vacancy, not permission to state-test the
other branch's pending destination. An available task selected from several
paths must preserve the guarded operation/output attachments needed by its
observation; equal Task<bool> types are insufficient.
After all reachable reservations have been lawfully observed/settled, ordinary
live/vacant joins and guarded item cleanup apply. A discarded proof does not
erase an operation or an already-published owner.

A new advancement gets a new logical identity even when physical task,
frame or destination storage is reused. An old true result cannot establish
initialization for a later item.
Repeated completion observation, if the selected task contract permits it,
does not initialize again, reacquire a moved owner or revive an old version.
An unrelated Boolean, an equality of task types or a reconstructed getter
does not become the completion discriminator.

Storing/moving/forwarding the operation preserves the attachment, exact
output place, completion owner and capture extent. It does not create a
second initialization or close responsibility.
An independently consumed forwarding declaration must publish the phase,
destination mapping and completion-result relationship its callers use.
Returning an arbitrary same-shaped Task<bool> or relying on a private body
does not supply them. A checked implementation of the selected nominal
requirement, including an explicit adapter, inherits that relationship;
general author-defined completion registrations require their own schema.

Direct forwarding may retain the original operation identity with mapped
formal identities. An async wrapper can instead retain a distinct outer
operation and delegate initialization to the inner one under checked
reborrowing: the inner publication is visible to the wrapper, not prematurely
to the outer caller. Outer completion waits for inner completion and its own
checks/cleanup; an outer exit fault cleans the still-unpublished item.
Neither returning the inner task nor awaiting it grants an added lifetime,
capture or output effect that the wrapper's public contract lacks.

Completion responsibility stays with an owner that can observe/join the
operation before its borrowed backing ends. Transferring it to a structured
owner/supervisor must preserve destination access, outcome correlation and
live-output cleanup obligations, not merely scheduling responsibility.
Discarding a task, losing its result proof or requesting cancellation cannot
drop the reservation. An explicit outcome-discard/join protocol must still
clean any live unclaimed item under its own admitted contract.
No generic TaskScope, repeated-await or failure-object API is selected here.

## Async loop and close order

After acquisition/adoption, prepare a vacant item destination, invoke
TryMoveNextAsync once and await that exact operation before entering the body.
True binds the published item under ordinary `var`/`var?`, type, copy/transfer
and backing rules. False enters no body and proceeds to required close.
The loop does not implicitly await a published T that itself is a task.

Retire remaining item/body obligations on normal completion, continue,
break, return or failure before another advance or CloseAsync.
Transferred owners leave no second cleanup through the vacant binding.
An item/source loan that would be invalidated by next advance, close or
suspension requires its actual surviving relationship, not Copy/boxing or
an extension of hidden header storage.
Reusable storage remains one eligible slot, not a retained per-item arena.

Invoke CloseAsync once on each adopted traversal's exit and observe its
Task<void> completion before leaving the header or completing the transfer.
Never overlap it with a pending advance. If an exit/request occurs while
advancement is outstanding, first establish that operation's actual terminal
completion/cleanup and handle any published item; only then close the cursor.
A cancelled wait is not permission to destroy either backing.
An explicitly awaited source-preparation expression retains its ordinary
completion responsibility before factory evaluation/adoption; it is not a
second implicit acquisition protocol.
Nested/named-loop transfers close and clean intervening scopes in order.

CloseAsync retains cursor/source/backing and its responsible owner through
its own completion/cleanup. It closes never-advanced and failed-advance state
without running unentered producer body scopes or repeating completed cleanup.
It does not dispose the original source collection.
Success discharges its actual traversal obligations; a fault preserves
ordinary causal cleanup/failure chaining and tracked residual obligations,
not successful exhaustion or an implicit second close attempt.
Generated owner destruction remains nonthrowing and nonsuspending: the
required async close is a structured authored/protocol operation, not
permission for a destructor to suspend.
Cancellation requests alone discharge neither advance nor close.

## Boundaries and retained contracts

Synchronous collection construction retains its eager capture/drain contract.
An async-only cursor is not silently enumerated, awaited, materialized or
incrementally captured by a spread. [Reference-output lending](lending-iteration.md)
selects separate readonly/writable roles with this completion reservation
and an independent element-loan/backing relationship. A reference-valued
by-value item does not supply those witnesses or an element-place alias.
[Generated producers](generated-producers.md) separately supply named affine
frames, lazy body entry and exact creation/operation/close phases.
Physical producer/task/frame programs remain independently required.
This reservation does not extend the invocation-phase Pure/NoAlias schemas
into arbitrary later work. Their [published effects](published-predicates-and-effects.md)
and independently declared capture relationships retain their existing scope.

| Required field | Meaning |
|---|---|
| Rules and authority | `async-value-iteration/1`, exact Core async interfaces/members, selected nominal witnesses and admitted task completion operations |
| Source | One factory/direct relationship; normalized T/TCursor/source terms, acquisition and access/copy/transfer/backing |
| Output phase | Exact init formal/destination, operation identity/version, completion Boolean and true/live false/vacant failure/vacant relation |
| Reservation | Pending availability, no competing initialization/state observation, cursor/destination access and actual completion/observation endpoints |
| Dependencies | Retained roots, leases, guards, storage/affinity, item result backing and completion/cleanup responsibility |
| Forwarding | Exact formal/operation mappings or checked outer/inner delegation, with no early publication or ownership duplication |
| Schedule | Once-only synchronous acquisition and async advance, observed publication before body, item retirement before next advance and awaited close before header exit |
| Admission | Complete supported relationships, checked semantic producer/audit trust and retained compatibility |

Independent consumers validate these requirements before affected invocation.
Linking/remapping preserves semantic operation, formal, output and phase
identities rather than task addresses, source names or row order.
Changing publication to invocation-return, dropping a reservation/capture,
returning unrelated completion or weakening cleanup/output/close promises
is incompatible with retained callers.
Physical operation/output/phase/capture/task/witness records remain separately
gated by the [metadata requirements](../representation/logical-records.md#asynchronous-value-iteration-encoding-pending).
No reserved table slot, optional Attr field, feature bit or opcode receives
this role. [Conformance cases](../conformance/source-async-value-iteration.md)
distinguish the logical contract from compiler/runtime implementation.
