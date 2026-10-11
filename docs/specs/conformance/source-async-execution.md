# Asynchronous execution conformance

These cases specify [async-execution/1](../language/async-execution.md).
Fixtures supply admitted task-like results, execution/capture facts and
checked failure/cleanup phases. They are not task/awaiter representations,
arbitrary source proofs or scheduler/concurrency tests.

## Entry and establishment

| ID | Scenario | Required outcome |
|---|---|---|
| AEENTRY-01 | Ordinary async implementation has an admitted completion result | Source body supplies its exact completion payload |
| AEENTRY-02 | Ordinary async result is void or unregistered task-like shape | Reject; no implicit result wrapper or name-discovered protocol |
| AEENTRY-03 | Caller arguments have ordered effects | Evaluate/stage once in source order before callee establishment |
| AEENTRY-04 | A later caller argument fails | Synchronous caller failure; clean earlier staged owners; no callee body |
| AEENTRY-05 | Caller explicitly moved an earlier argument before failure | Its original binding remains vacant; no transactional restoration |
| AEENTRY-06 | Callee frame/operation establishment fails | Synchronous failure; acquired parameter/private state cleanup; no body finally/defer |
| AEENTRY-07 | Established operation then fails its first precondition | Failed completion, not synchronous invocation propagation |
| AEENTRY-08 | Later precondition fails after an earlier phase local was formed | Ordered phase cleanup and captured parameter cleanup before failed completion |
| AEENTRY-09 | Establishment failure would strand started borrowed work | Reject contract; establish responsible operation before deferred cleanup is needed |
| AEENTRY-10 | Lowering delays/elides physical task allocation | Preserve logical establishment and every failure channel |
| AEENTRY-11 | Iterator has async body but returns its named async cursor | Creation remains synchronous and body remains unstarted |
| AEENTRY-12 | Non-async function returns a task and throws while creating it | Ordinary synchronous invocation failure; not automatically async execution |

## Inline execution and suspension

| ID | Scenario | Required outcome |
|---|---|---|
| AERUN-01 | Ordinary async call can execute without suspension | Start inline and execute through completion/failure |
| AERUN-02 | First await observes an already-completed operation | Continue inline absent an explicit dispatch requirement |
| AERUN-03 | First await actually suspends | Retain checked state and resume under admitted operation/runtime policy |
| AERUN-04 | Body fails before any await | Failed completion, possibly terminal before handle return |
| AERUN-05 | An executed await completes inline, then body fails | Failed completion; executed await is not a channel switch |
| AERUN-06 | Body fails after one or more suspensions | Same failed-completion channel after ordinary unwind |
| AERUN-07 | Body catches failure and then returns normally | Success only after remaining cleanup and postconditions |
| AERUN-08 | Call is async but no scheduler/executor adapter is selected | No implicit pool hop, caller executor capture or parallelism |
| AERUN-09 | Affine operation/helper suspends | Preserve its own declared affinity and real backing/guard obligations |
| AERUN-10 | Unbound helper is called by affine code | It does not acquire caller affinity merely through invocation |
| AERUN-11 | Already-failed await has an explicit awaiting-continuation policy | Observe failure/unwind on that policy; do not reschedule producer |
| AERUN-12 | Captured reference loses valid backing during suspension | Reject unsupported lifetime/transfer; default confinement is insufficient |

## Completion and ownership

| ID | Scenario | Required outcome |
|---|---|---|
| AECOMP-01 | Body returns a successful payload | Evaluate once, finish applicable body/parameter cleanup, then completion ensures |
| AECOMP-02 | Async declaration ensure uses contextual return | Observe exact completed payload, not returned task handle |
| AECOMP-03 | Body cleanup faults after a pending return | Failed completion; no success publication |
| AECOMP-04 | Completion ensure fails after body cleanup succeeded | Failed completion; completed body cleanup is not replayed |
| AECOMP-05 | An owned parameter was moved into the payload | Observe destination; do not clean/read consumed source again |
| AECOMP-06 | Completion clause dereferences destroyed value or released guard | Reject invalid availability/backing, not a retained-byte read |
| AECOMP-07 | Body and cleanup both fault | Ordinary causal chaining, no failure suppression/default success |
| AECOMP-08 | Body failure escapes | Skip normal completion ensures |
| AECOMP-09 | Operation becomes physically terminal before observation | Keep independently required observation/output facts outstanding |
| AECOMP-10 | Completion is observed repeatedly | Do not rerun body/checks/cleanup or transfer an affine result twice without authority |
| AECOMP-11 | Async cursor output was moved/retired/reused after observation | Old completion retains its original output version; no republishing |
| AECOMP-12 | Fail-fast or process termination occurs | No normal-completion or cleanup promise |

## Effects and responsibility

| ID | Scenario | Required outcome |
|---|---|---|
| AEFAIL-01 | Async NoThrow declaration can escape a valid-domain body failure through task completion | Reject NoThrow promise; task transport does not erase exception effect |
| AEFAIL-02 | Async NoThrow declaration can fail callee establishment/check/cleanup on its valid domain | Its promise covers those callee phases too |
| AEFAIL-03 | NoThrow non-async factory returns an admitted faulted task without throwing | Invocation promise alone says nothing about later success |
| AEFAIL-04 | Caller argument fails before an otherwise NoThrow call | Failure retains caller attribution |
| AEFAIL-05 | NoAlias is placed on an async/producer source input | Do not infer a post-invocation separation extent |
| AEFAIL-06 | A pending operation handle or wait is discarded | Responsible completion/error/cleanup owner remains |
| AEFAIL-07 | Cancellation is requested while work is pending | Request is not terminal completion or backing release |
| AEFAIL-08 | An admitted terminal cancellation completes cleanup | Exceptional completion; no normal guarantees or fabricated payload |
| AEFAIL-09 | Later cancellation request follows published success | Do not erase published successful result/output |
| AEFAIL-10 | Unregistered Task-returning init output is treated as completion-reserved | No relationship from Task shape; preserve ordinary defaults |

## Retained contracts

| ID | Scenario | Required outcome |
|---|---|---|
| AERET-01 | Retained call distinguishes ordinary async and synchronous task factory | Preserve behavior/result/payload and failure-boundary identity |
| AERET-02 | Replacement moves a pre-await established failure to synchronous throw | Incompatible retained contract |
| AERET-03 | Replacement publishes success before completion cleanup/ensure | Incompatible contract; no late success retraction |
| AERET-04 | Consumer lacks required async-execution rule/capture support | Reject before affected use |
| AERET-05 | Consumer remaps operation/formal/phase/failure references | Preserve semantic relationships independently of rows/debug names |
| AERET-06 | A task/frame/awaiter/failure flag is treated as a proof of implementation truth | Explicit semantic producer/audit trust, not structural flag proof |
| AERET-07 | Logical phase model passes | Not task representation, arbitrary body proof or scheduler/concurrent execution conformance |
