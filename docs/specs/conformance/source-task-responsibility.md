# Affine task and consuming-await conformance

These cases specify [task responsibility](../language/task-responsibility.md).
Fixtures supply complete type/registration, dependency, owner and output
contracts. Finite schedules distinguish actual terminal state, observation,
payload ownership and exact operation/value versions; they are not a compiler,
awaiter runtime, scheduler, memory model or physical task codec.

## Core identity and eligibility

| ID | Scenario | Required outcome |
|---|---|---|
| TREG-01 | Resolved Core Task<T> is admitted | Use exact nominal registration and consuming completion-owner contract |
| TREG-02 | User defines same-named Task or GetAwaiter methods | No Core/task eligibility from spelling |
| TREG-03 | Bare Task supplies its declared default | Exact Task<void>, not result erasure |
| TREG-04 | Task<Derived> is treated as Task<Base> or Task<void> | Reject unregistered covariance/erasure |
| TREG-05 | T carries write access or borrowed dependencies | Preserve the complete qualified T and actual bounds |
| TREG-06 | T is non-Copy but has an admitted complete profile | Formation can succeed; no universal Copy/ManagedStorable requirement |
| TREG-07 | Task<T> owner is copied | Reject duplicate affine responsibility |
| TREG-08 | Default bytes/optional absence are used as a task | No fabricated operation or cold-task Start semantics |
| TREG-09 | Task object allocation is elided | Preserve logical identity, ownership and failure channels |
| TREG-10 | IsCompleted is queried on a live task | Synchronous bool status only; terminal true is monotonic for that operation |
| TREG-11 | Custom awaiter/callback contract is incomplete | Reject before use; no name-based continuation registration |
| TREG-12 | Target lacks the required task/wait/root representation | Reject; no allocation or blocking fallback |

## Ownership and transfer

| ID | Scenario | Required outcome |
|---|---|---|
| TOWN-01 | Operation is established before its task returns | One responsible callee/runtime owner exists |
| TOWN-02 | Task return transfers to caller | Preserve operation and all obligations; no ownerless interval |
| TOWN-03 | Named task is moved to another owner | Source becomes vacant; same operation/payload/observations |
| TOWN-04 | Vacant moved task is read, awaited or transferred again | Reject |
| TOWN-05 | Task is borrowed for status/inspection | Borrow owns no completion responsibility |
| TOWN-06 | Borrowed observer is copied where its type permits | No second task owner; preserve observer backing |
| TOWN-07 | Aggregate contains a task owner | Carry its task obligation transitively through storage/transfer |
| TOWN-08 | Later argument fails after task ownership was staged | Exceptional path must settle or transfer the staged owner |
| TOWN-09 | Receiver accepts task before failing | Responsibility stays in the actual destination; no rollback |
| TOWN-10 | Synchronous function returns an existing moved task | Transfer without restarting work or changing invocation failure phase |
| TOWN-11 | Conditional paths retain different task owners | Keep guarded operation/owner identities; equal types do not merge responsibilities |
| TOWN-12 | Borrow of task storage survives an owner move/take | Reject conflicting storage invalidation |
| TOWN-13 | Task is explicitly discarded while pending/unobserved | Reject abandonment; no implicit detach/join/cancel |
| TOWN-14 | Consuming helper accepts a task parameter | Check its own every-exit settlement/transfer; by-value is not unchecked supervision |

## Consuming await

| ID | Scenario | Required outcome |
|---|---|---|
| TAWAIT-01 | Fresh Task<T> result is awaited | Transfer once into wait; produce exact T or terminal failure |
| TAWAIT-02 | Named Task<T> is awaited without move | Reject implicit transfer/Copy adaptation |
| TAWAIT-03 | Named task uses await move task | Source becomes vacant on success and failure |
| TAWAIT-04 | Operation is already terminal | Continue inline absent explicit dispatch policy; same outcome/ownership |
| TAWAIT-05 | Operation remains pending at first observation | Wait retains owner/dependencies through suspension |
| TAWAIT-06 | Completion succeeds with owning T | Transfer T once; settled task does not clean the transferred payload |
| TAWAIT-07 | Completion succeeds with void | Publish live () despite zero payload bytes |
| TAWAIT-08 | Completion fails before or after suspension | Ordinary failure at observation; no T/default/exhaustion |
| TAWAIT-09 | Admitted terminal cancellation is observed | Failure path after cessation/cleanup; no successful payload |
| TAWAIT-10 | Operand evaluation fails before yielding a task | Caller failure; no wait owns a nonexistent task |
| TAWAIT-11 | Async establishment fails before an operation exists | Synchronous failure with acquired-state cleanup, no stranded work |
| TAWAIT-12 | Wait setup/registration can fail after transfer | Retain or settle a responsible owner; never discard the task |
| TAWAIT-13 | Consumed task is awaited/reobserved through its old binding | Reject vacant owner; no repeated execution or transfer |
| TAWAIT-14 | Continuation adapter changes dispatch | Preserve consumption, owner, failure and affinity; do not reschedule producer |
| TAWAIT-15 | Awaited Boolean has selected init-output attachment | Observe that exact operation/formal/destination/version relation |
| TAWAIT-16 | Payload borrows from retiring task/wait storage | Reject; external surviving backing and actual owned transfer are distinct |

## Observation and payload state

| ID | Scenario | Required outcome |
|---|---|---|
| TOBS-01 | IsCompleted is true before checked terminal observation | No payload/error/output discharge or destruction permission |
| TOBS-02 | Terminal status is observed repeatedly | Same exact operation; no body/check/cleanup replay |
| TOBS-03 | Retained task has nonconsuming failure inspection | Repeat only under its declared representation/effect contract; no owning-error Copy |
| TOBS-04 | Payload is copied through an admitted Copy-only observer | Preserve dependencies; original payload and completion owner remain |
| TOBS-05 | Non-Copy payload is requested by repeated copy | Reject; borrow or one-shot transfer under actual contract |
| TOBS-06 | Payload borrow overlaps task take/move/destruction | Reject conflicting source-storage invalidation |
| TOBS-07 | Payload was transferred once | No second owning result; terminal classification does not recreate it |
| TOBS-08 | Failure delivery reaches the responsible flow | Error observation is accounted for before ordinary handler propagation |
| TOBS-09 | Output storage is retired/reused after observation | Old completion cannot initialize the new output version |
| TOBS-10 | Completed untransferred payload is explicitly retired | Exact-once payload cleanup under the settled owner contract |
| TOBS-11 | Producer postcondition faults after private payload formation | Clean unpublished payload before terminal failure; publish no success |
| TOBS-12 | Success payload is an expected domain-error value | Still successful completion data, not invented terminal fault |

## Every-exit settlement

| ID | Scenario | Required outcome |
|---|---|---|
| TEXIT-01 | Normal exit would end pending task extent | Reject without explicit settlement/valid transfer |
| TEXIT-02 | Exceptional exit would end pending task extent | Same responsibility rule; failure does not permit abandonment |
| TEXIT-03 | Owning task is explicitly returned | Caller receives responsibility with complete dependencies |
| TEXIT-04 | Finally state-guards and consumes a still-live task | Settle only live paths, including failure before the first await |
| TEXIT-05 | First await already consumed task before failing | Finally sees vacancy; no repeated wait/cleanup |
| TEXIT-06 | Join has pending/live and vacant paths | Preserve guards; task is init tests binding liveness, not completion |
| TEXIT-07 | IsCompleted alone is used to justify task destruction | Reject missing observation/output/payload settlement facts |
| TEXIT-08 | Settled owner has a live untransferred payload | Retire it and bookkeeping exactly once, without hidden wait/throw |
| TEXIT-09 | Task belongs to an active output reservation | Preserve output storage and full dependency extent until exact observation |
| TEXIT-10 | Supervisor consumes task before its accepting call fails | No source restoration; destination retains its checked obligation |
| TEXIT-11 | Supervisor would retain caller-local backing too long | Reject; supervision cannot extend that lifetime |
| TEXIT-12 | Structured owner/supervisor shuts down | Satisfy admitted wait/transfer/error/cleanup/affinity policy; a name alone is insufficient |
| TEXIT-13 | Multi-task join observes failure while siblings remain pending | Settle all adopted siblings before terminal publication; preserve declared failure policy |
| TEXIT-14 | Wait never completes or responsibility cycle exists | No termination/fairness proof or fabricated settlement; live obligation remains |

## Cancellation and placement

| ID | Scenario | Required outcome |
|---|---|---|
| TCANC-01 | Cancellation is requested while operation is pending | No ownership, completion or dependency discharge |
| TCANC-02 | Request follows successful completion | Preserve successful payload/output |
| TCANC-03 | Request follows failed completion | Preserve the admitted failure; no replacement success/cancellation |
| TCANC-04 | Request and completion compete | Request alone selects no terminal result; actual admitted outcome governs |
| TCANC-05 | Terminal cancellation is published | Require cessation and cleanup before failure observation |
| TCANC-06 | Timeout/early-return adapter stops waiting | Preserve/transfer remaining owner, captures and output obligations |
| TCANC-07 | Parent operation receives a request while awaiting child | No implicit child request or abandonment |
| TCANC-08 | Request token/source lifetime is invalid | Reject under its separate capture/protocol contract |
| TCANC-09 | Cleanup or continuation has executor affinity | Preserve affinity through transfer, wait and supervision |
| TCANC-10 | Affine task is assumed concurrently shareable or synchronizing | No inferred capability, worker hop, caller-executor capture or memory edge |

## Independent contracts and admission

| ID | Scenario | Required outcome |
|---|---|---|
| TRET-01 | Consumer lacks either task responsibility or consuming-await rule | Reject required semantics before use |
| TRET-02 | Link remaps declaration/operation owner records | Preserve logical type, owner, phase and output identities, not row/name coincidence |
| TRET-03 | Instance/output storage is reused with the same numeric identity | Distinguish generations; no revived owner or result |
| TRET-04 | Adapter erases T or completion Boolean | Preserve/discharge payload and exact output obligations; no Task<void> cast |
| TRET-05 | Non-async Task factory declares borrowed-input capture | Returned operation retains checked bounds; not automatic init-output deferral |
| TRET-06 | Task-returning normal output has no completion-phase contract | Keep ordinary invocation publication and failure semantics |
| TRET-07 | Consuming await settles task but leaves published element view live | Preserve its backing/guard and independent cursor-close requirement |
| TRET-08 | Replacement changes consuming await to reusable/copying ownership | Reject incompatible retained contract |
| TRET-09 | Replacement drops error/output/cleanup responsibility or adds hidden blocking | Reject incompatible retained contract |
| TRET-10 | Structural registration or semantic attribute is treated as body proof | Require checked evidence or explicit semantic producer/audit trust |
| TRET-11 | Held opcode/table/optional attribute encodes mandatory task semantics | No physical allocation activated by logical approval |
| TRET-12 | Finite task schedule/owner fixtures pass | Not compiler/runtime, arbitrary body, collector, concurrency or codec conformance |
