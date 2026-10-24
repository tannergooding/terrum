# Asynchronous execution and failure

This chapter owns logical `async-execution/1`: eager ordinary async execution,
operation establishment, failure channels and checked completion. It does not
select a task representation, general awaiter registration, scheduler or
cancellation API. [Task responsibility](task-responsibility.md) selects the
Core affine completion owner, consuming await and explicit every-exit settlement.
[Contract phases](../language-contracts.md#3-contracts-bracket-semantic-body-completion)
and [lifetimes](../lifetimes.md) retain their ownership and cleanup rules.
[Async iteration](async-iteration.md) separately supplies its exact output
reservation; a task-like result alone does not create that relationship.

## Invocation and operation establishment

[Sharing and handoff](../runtime/sharing-and-handoff.md) separately checks
actual thread migration, captured authority and continuation/cleanup affinity.
A guard or remote return reservation cannot disappear at suspension, terminal
status or cancellation. Its admitted extent and endpoint must survive; no
implicit release/reacquire, executor inheritance or blocking join is added.

An ordinary `async` implementation has an admitted task-like completion
result. Its source body produces that completion's payload, not another task
handle. `async void` is invalid. A non-async callable returning Task<T> remains
an ordinary synchronous callable unless an explicit registered operation
contract supplies additional phases.

Evaluate and stage caller arguments once in source order. Source lookup,
typing, access, initialization, transfer and dependency obligations precede
affected use. Failure evaluating an argument is a caller-side failure; no
callee operation exists. Earlier staged owners retain their ordinary
argument-staging cleanup, without restoring moved caller bindings.

The callee establishes parameter storage, frame/root responsibility and a
logical completion operation before executing its user preconditions or body.
Establishment means that the operation can retain all required state and
publish a terminal result or failure to a responsible owner. It is not a
requirement to allocate a task object or use a worker thread.

Failure before establishment propagates synchronously with valid cleanup of
callee-owned and private/partial state. No body scope has been entered.
A synchronous failure cannot strand started work, borrowed storage or cleanup
that remains pending without a responsible operation. An implementation that
needs deferred cleanup must establish its responsible operation first and
report the eventual outcome through that operation.

Physical task materialization can be elided or delayed when sound lowering
preserves this logical boundary and all failures. Running inline, allocating
late or recognizing a constant completion does not change a body failure into
a synchronous invocation exception.

## Eager execution and suspension

After establishment, ordinary async execution starts inline on the invoking
flow: preconditions, then the body, until it completes, fails or actually
suspends. Returning the handle does not by itself run completion postconditions.
An await of an already-completed operation continues inline unless an explicit
admitted continuation policy requires dispatch. An incomplete await suspends
and resumes under its checked operation/runtime policy.

Async does not imply a thread pool, a scheduler capture, a worker hop,
concurrency or parallelism. Own declared executor affinity and every captured
backing, root, guard, loan and transfer obligation survive suspension.
Calling an unbound helper from affine code does not implicitly bind that
helper to the caller's executor.

Execution of a failed already-completed await follows the same selected
continuation and ordinary failure/unwinding rules as a later failed
completion. An explicit awaiting-continuation adapter does not reschedule
the producer or bypass access/lifetime requirements.

## Failure-channel boundary

Once established, an operation reports escaping precondition, body, authored
cleanup and completion-check failure through **failed completion**, regardless
of whether execution ever suspends. A caller observing that completion takes
ordinary language failure at the observation point; it does not retrospectively
receive a synchronous invocation exception.
[Runtime requirement defaults](requirement-failures.md) retain the check's
source callable before/after suspension. Both a created requirement failure
and an actual failure during its common exception factory
use this same establishment boundary. Neither execution on another flow nor
failed-completion transport selects a caller default or fabricates E.

[Captured-event operations](captured-exceptions.md) use the same boundary.
Direct Capture retains its lexical callable/handler activation and actual
owning/transferred state across supported resumption, not a worker-thread
current exception. Captured throw transfers the same event into failed
completion when it escapes an established operation; observation retains
that provenance and responsibility. A successful completed CapturedException
payload is instead an ordinary owning result, not failed completion.
All input/owner/backing and affinity obligations still constrain suspension.

| Failure or outcome | Observation and remaining obligations |
|---|---|
| Caller argument evaluation fails | Synchronous caller failure; no callee operation or body; ordinary staged-value cleanup |
| Callee establishment fails | Synchronous invocation failure; private/partial and acquired parameter cleanup; no stranded work |
| Established precondition check fails | Failed completion after valid parameter/phase cleanup; no unentered body finally/defer |
| Body fails before its first await | Failed completion, possibly already completed when returned |
| Await is reached but completes inline, then body fails | Failed completion; an executed await is not a failure-channel switch |
| Body fails after suspension | Failed completion after ordinary body unwind |
| Body catches a failure and later completes normally | Successful completion only if remaining body cleanup and postconditions succeed |
| Body cleanup replaces a pending success with failure | Failed completion; no successful payload/output publication |
| Normal-completion postcondition fails | Failed completion; completed body cleanup is not replayed |
| Operation finishes without any await | The same successful/failed completion channels |
| Fail-fast or process termination | No normal completion or cleanup promise |

A generated async cursor's advance and close follow this boundary. Its
[producer creation](generated-producers.md#creation-and-captured-state) is
instead synchronous, even when the producer body is marked async.
An arbitrary non-async function returning a task is not silently transformed:
its own ordinary call can throw synchronously, and the task follows its
separately admitted completion contract.

[Dependency annotations](dependencies-and-views.md#destination-and-phase)
preserve that distinction. Ordinary async return dependencies/captures describe
the completed payload; callable-operation Captures describes responsible
work through its admitted completion/cleanup extent. A non-async factory's
explicit operation relationship does not change its synchronous failure phase.
Annotations add no output reservation, scheduler, retain or lifetime extension.

## Completion, cleanup and observation

Successful completion occurs after the semantic body and its applicable
finally/defer, local and untransferred owned-parameter cleanup, followed by
normal-completion postconditions. The contextual result in an ordinary async
declaration ensure denotes the completed payload. Consumed sources and
released guards are not available merely because their old bytes remain.

An escaping failure skips normal-return postconditions. A postcondition or
late cleanup failure cannot publish success and then retract it. Preserve
ordinary causal failure chaining; never rerun completed cleanup, suppress a
failure into a default payload, or use false/exhaustion as a failure carrier.
Actual effects are not rolled back.

Physical completion may precede observation. A completion discriminator can
establish caller facts only under the admitted observation relationship.
For [async cursor outputs](async-iteration.md#completion-and-observation), this
includes the exact operation, formal, destination and output version.
IsCompleted or a retained task alone does not release reserved storage.
An admitted nonconsuming reobservation does not rerun its body/checks or initialize a reused
destination. Result copying/transfer still requires its actual value contract;
reobservation does not promise repeated transfer of an affine payload.
Ordinary Core await consumes the task and transfers its payload once;
`await move task` is required for a named owner. Its old binding is vacant
on both success and failure.

Every started operation retains a responsible owner through actual completion,
required cleanup and observation/transfer of its obligations. Discarding a
handle or abandoning a wait does not cancel or discharge that responsibility.
For Core Task<T>, the affine value is that owner. A scope exit that could
abandon pending or unobserved failed work rejects unless authored cleanup
settles it or an explicit transfer preserves responsibility.
Generated destruction supplies no blocking, suspension, cancellation or
implicit supervisor. Task/payload/output observation retains the exact
relationships in [task responsibility](task-responsibility.md).
Cancellation is a request, not completion. A separately admitted terminal
cancellation follows exceptional cleanup and supplies no success guarantees;
its request API and failure representation remain separate.

## Effects and independent contracts

Failure transport does not erase the exception effect of the code that
produces it. An ordinary async operation may fail during callee establishment
or during operation execution through completion, including applicable
cleanup/checks. Replacing propagation with a failed task does not erase those
failures. A non-async task factory can return normally and the later task
operation can still fail; its completion has a distinct operation extent.
Generated async slots have the former async
extent. Caller argument evaluation retains its own effects outside the callee.

This chapter does not broaden the synchronous Pure result schema or
invocation-only NoAlias into arbitrary future task work. Actual async
access/effect/capture relationships remain independently checked. Ordinary
Task-returning output defaults, implicit output lifting and task/awaiter
eligibility are not changed by the failure-channel rule.

Retained declarations preserve task-like result and payload identity, ordinary
async versus synchronous-factory behavior, establishment boundary, failure
channel, contract phases, capture/ownership/affinity and observation obligations.
Replacement cannot move an established body failure into synchronous
invocation or publish success before required checks/cleanup.
An implementation cannot claim support without the required rule and
records. Consumers validate selected record structure, references and support
before affected use, then may rely on producer-published body facts without
semantic re-proof; they do not rebind from source.
Physical task/frame/failure/awaiter records remain gated by
[metadata](../representation/logical-records.md#async-execution-and-generated-producers-encoding-pending).
[Conformance cases](../conformance/source-async-execution.md) are logical
requirements, not evidence of compiler or runtime implementation.
