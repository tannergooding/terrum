# Asynchronous value iteration conformance

These cases specify [async-value-iteration/1](../language/async-iteration.md).
Fixtures supply exact nominal, completion/output and backing contracts;
this is not a compiler/runtime implementation or physical task/frame codec.

## Protocol identities and source selection

| ID | Scenario | Required outcome |
|---|---|---|
| AIPROTO-01 | Exact IAsyncCursor<T> witness exposes `TryMoveNextAsync(ref<init T>)` | Bind its completion-Boolean output phase, not invocation-return initialization |
| AIPROTO-02 | Exact IAsyncSequence<T, TCursor> witness is selected | Bind readonly synchronous GetAsyncEnumerator and exact IAsyncCursor<T> result |
| AIPROTO-03 | User types/members merely have the same names | No Core protocol or phase relationship from spelling |
| AIPROTO-04 | Ordinary Task<bool>-returning method has init output | Task shape alone does not replace its ordinary declared output contract |
| AIPROTO-05 | Implementation omits async while returning an eligible operation | Inherit the same phase/capture/output requirements |
| AIPROTO-06 | IAsyncCursor candidate retains a Current property instead of init publication | Require an explicit selected adapter; no standard Current call |
| AIPROTO-07 | Non-Copy T can be transferred from an owning cursor | No universal Copy bound or separate consuming cursor |
| AIPROTO-08 | T is an optional absent value or void | True still publishes one live item; false is distinct exhaustion |
| AIPROTO-09 | Repeated source paths reach one witness/member identity | Deduplicate exact relationship |
| AIPROTO-10 | Distinct async factory/direct relationships exist | Diagnose ambiguity even with equal item/cursor types |
| AIPROTO-11 | Explicit loop binding mismatches selected T/access | Diagnose; no inverse witness search |
| AIPROTO-12 | Only synchronous cursor/sequence witnesses exist | No async task wrapper, blocking or member discovery |
| AIPROTO-13 | Named affine direct cursor lacks move | Diagnose; no implicit consuming acquisition |
| AIPROTO-14 | Fresh or explicitly moved direct cursor is adopted | Preserve existing traversal and vacancy; no factory/reset/clone |
| AIPROTO-15 | Generic declaration lacks async relationship/storage guarantees | Specialization cannot repair definition-time validity |
| AIPROTO-16 | Required Task completion or reservation support is absent | Diagnose before affected invocation; no partial fallback |
| AIPROTO-17 | Factory returns Task<TCursor> or a different cursor form | Exact synchronous result/witness relationship required; no implicit task wrapping or conversion |
| AIPROTO-18 | ref/mut ref loop has only the by-value async protocol | No borrowing of its hidden output as a source element |

## Pending initialization reservations

| ID | Scenario | Required outcome |
|---|---|---|
| AIRES-01 | Invocation receives one exact vacant destination | Attach fresh operation identity, formal ordinal and destination version |
| AIRES-02 | Destination is already live | Diagnose before invocation; no hidden retirement |
| AIRES-03 | Task returns before initialization completes | Destination is reserved, not caller-live or ordinarily reusable vacant storage |
| AIRES-04 | Caller reads/tests value through reserved item or alias | Reject before reading partial state |
| AIRES-05 | Caller tests item is init while reserved | Reject; availability observation cannot race publication |
| AIRES-06 | Caller tests an init descriptor's own initialization | Distinguish descriptor state from reserved referent state |
| AIRES-07 | Caller forms competing init/write loan or overwrites destination | Reject overlapping reservation, not a second private output |
| AIRES-08 | Caller moves/retires/reuses reserved storage | Reject until completion observation and cleanup |
| AIRES-09 | Cursor advance/close/reset is attempted while advance pending | Reject through the actual traversal relationship |
| AIRES-10 | Two aliases/task bindings denote the same pending operation | One reservation/responsibility, not two independent completions |
| AIRES-11 | Distinct cursor/output regions have checked disjoint contracts | May coexist without global NoAlias/exclusive-reference requirement |
| AIRES-12 | Different descriptors share output/cursor backing | Slot/object inequality is not disjointness |
| AIRES-13 | Zero-data output has no payload bytes | Reservation still protects its logical availability identity |
| AIRES-14 | Caller ends destination backing/root/guard before completion | Reject lifetime plan; task does not extend backing by name |
| AIRES-15 | Operation is stored/moved with exact dependencies | Preserve reservation and completion responsibility |
| AIRES-16 | Operation is discarded or loses its owner | No cancellation/discharge; require a valid responsible owner |
| AIRES-17 | Cancellation request or timeout occurs | Reservation and mutable cursor access remain outstanding |
| AIRES-18 | Body privately initializes output before returning task | No caller publication until checked completion observation |
| AIRES-19 | Execution is serialized but destination cannot legally survive suspension | Diagnose; no single-thread lifetime exception |
| AIRES-20 | Pending operation delegates its init authority to one inner call | Checked reborrow, not competing caller initialization or duplicated owner |
| AIRES-21 | Only one control-flow branch starts advancement | Preserve its guarded pending reservation and owner; do not join it into caller-testable vacancy/maybe-live state |

## Terminal completion and publication

| ID | Scenario | Required outcome |
|---|---|---|
| AICOMP-01 | Actual successful completion and cleanup establish true/live | Await observation publishes the complete item and Boolean correlation |
| AICOMP-02 | Completion establishes false/vacant | No item/body/default; traversal is exhausted |
| AICOMP-03 | true pairs with vacant/partial/invalid item | Reject contradictory completion contract |
| AICOMP-04 | false pairs with live item | Reject contradictory completion contract; track cleanup |
| AICOMP-05 | Body faults before forming an item | Vacant output after cleanup; preserve original failure |
| AICOMP-06 | Body faults after partial or complete private item formation | Clean unpublished item once; no caller read or success |
| AICOMP-07 | Completion postcondition faults after item formation | Prevent publication and clean unpublished item |
| AICOMP-08 | Authored completion cleanup faults | Same failure/vacancy relation; completed cleanup not replayed |
| AICOMP-09 | Cancellation request is followed by terminal cancellation | Remain pending until stopped/cleaned; then observe vacant output |
| AICOMP-10 | Cancellation request is followed by normal true completion | Publish success normally; request is not retroactive vacancy |
| AICOMP-11 | Timeout/abandoned wait does not terminate operation | No terminal output fact or permission to close/reuse |
| AICOMP-12 | Task completed synchronously before invocation returns | Same reservation/observation and true/live relation, no early special case |
| AICOMP-13 | Caller checks IsCompleted without observing outcome | No Boolean/output relation or reservation discharge |
| AICOMP-14 | Physical output writes occur before completion | Track private initialization/roots; do not expose partial T |
| AICOMP-15 | Awaiter supplies result before required cleanup finishes | Reject incompatible completion observation |
| AICOMP-16 | Completion failure leaves mutated cursor state | Keep cleanup-safe CloseAsync/residual obligations; no retry guarantee |
| AICOMP-17 | Normal false has effects | Preserve them; exhaustion is not inferred effect-free |
| AICOMP-18 | Later standard advance of exhausted cursor occurs | Cannot resume/new item without separate reset/new traversal |
| AICOMP-19 | Task reports terminal failure but reservation holder still touches output | Contradicts terminal cleanup; no caller release until actual contract satisfied |
| AICOMP-20 | Success output has independent or cursor-backed dependencies | Preserve actual published relationship through use/retirement |
| AICOMP-21 | Invocation fails before returning its advancement operation | Discharge started work/reservation and private output under valid invocation cleanup; vacant caller output, cleanup-safe cursor, no stranded operation |

## Saved results and forwarding

| ID | Scenario | Required outcome |
|---|---|---|
| AIFLOW-01 | Await yields saved bool hasItem from the bound operation | Its true edge establishes that exact output version live |
| AIFLOW-02 | Awaited Boolean is copied to another scalar binding | Preserve supported exact correlation without duplicating output |
| AIFLOW-03 | Original Boolean binding is overwritten | Invalidate that binding's proof, not an unchanged saved copy's |
| AIFLOW-04 | Published output is moved/retired/replaced | Invalidate every old completion/output correlation |
| AIFLOW-05 | Task/frame/destination storage is reused by a later operation | Fresh logical identity; old true cannot initialize later item |
| AIFLOW-06 | Same completion is observed again where Task permits it | No reinitialization, owner duplication or revived output version |
| AIFLOW-07 | Unrelated Boolean/task of equal type is tested | No output-state proof by spelling or shape |
| AIFLOW-08 | State is tested after checked completion observation | Ordinary init test can refine current maybe-live state |
| AIFLOW-09 | Only result proof is discarded after true publication | Live item still needs guarded cleanup |
| AIFLOW-10 | Direct nominal adapter forwards original operation | Map exact formals, destination and capture/cleanup extent |
| AIFLOW-11 | Wrapper substitutes unrelated already-completed true task | Cannot satisfy inherited output/completion attachment |
| AIFLOW-12 | Async adapter awaits inner operation then completes outer | Outer retains publication through its own checks/cleanup |
| AIFLOW-13 | Inner completes true but outer completion check fails | Clean outer-unpublished item; caller sees failure/vacancy |
| AIFLOW-14 | Independent forwarding signature omits phase relationship | No body-inferred completion proof or default rewrite |
| AIFLOW-15 | Operation crosses aggregate/storage/ownership boundary | Preserve attachment/dependencies or diagnose unsupported escape |
| AIFLOW-16 | Distinct operation relationships join control flow | Retain exact guarded possibilities; do not invent one unconditional output proof |
| AIFLOW-17 | Matching/value require omits async destination | No implicit output lifting or joining; explicit init destination remains required |
| AIFLOW-18 | Structured owner/supervisor accepts only scheduling responsibility | Insufficient unless destination/outcome/live-item cleanup obligations survive |
| AIFLOW-19 | Every reached reservation is observed before a join; another path did not start work | Apply ordinary live/vacant join and guarded cleanup without inventing an output or dropping a live owner |

## Async loop, acquisition and close

| ID | Scenario | Required outcome |
|---|---|---|
| AILOOP-01 | Factory source/call have effects | Evaluate once, acquire synchronously once, then advance/await; no producer body during acquisition |
| AILOOP-02 | Factory source is named non-Copy collection | Readonly acquisition, not source copy/consumption/disposal |
| AILOOP-03 | Factory temporary/lease backs acquired cursor | Retain through synchronous acquisition, iteration and awaited close |
| AILOOP-04 | Synchronous acquisition fails with private/partial cursor | Holder discharges actual obligations under valid acquisition cleanup; no stranded async work or CloseAsync on vacant caller result |
| AILOOP-05 | Source preparation is explicitly awaited before acquisition | Ordinary source completion followed by synchronous factory; no inserted acquisition task |
| AILOOP-06 | Direct cursor is adopted | No acquisition task; standard advance still uses completion/output contract |
| AILOOP-07 | Three items then exhaustion are produced | Await each advance; publish before body; retire before next advance; await one close |
| AILOOP-08 | Body continues | Retire item/body before next pending advance |
| AILOOP-09 | Body breaks/returns/faults | Retire remaining item then invoke/await CloseAsync before header exit |
| AILOOP-10 | Advance faults or terminates cancelled | No body for unpublished item; close cleanup-safe cursor after terminal cleanup |
| AILOOP-11 | Exit/cancellation happens during pending advance | First settle operation and handle any item; never overlap close or destroy backing |
| AILOOP-12 | Body moves an owning item elsewhere | No second retirement through vacant item binding |
| AILOOP-13 | T is itself a task | Yield task value; do not implicitly await it |
| AILOOP-14 | Item loan would be invalidated by advance/close/suspension | Require surviving published dependencies; no hidden lifetime repair |
| AILOOP-15 | CloseAsync suspends | Retain all header/cursor/source obligations until actual completion/cleanup |
| AILOOP-16 | CloseAsync faults after another fault | Preserve ordinary causal failure chaining, not successful end/suppression |
| AILOOP-17 | Cancellation request occurs during close | No release or implicit second close attempt |
| AILOOP-18 | Producer was never advanced or body already completed | Close unadvanced captures or residual obligations, without entering/repeating body scopes |
| AILOOP-19 | Nested/named loop transfer exits several async loops | Settle/close/clean intervening scopes in ordinary order |
| AILOOP-20 | Generated owner destruction would suspend to replace awaited close | Reject; destructor stays nonthrowing/nonsuspending |

## Retained admission and boundaries

| ID | Scenario | Required outcome |
|---|---|---|
| AIRET-01 | Retained call identifies member, init formal, operation and phase | Validate exact completion-output relationship before affected invocation |
| AIRET-02 | Source/type/witness remapping changes physical rows | Preserve exact nominal types, operation/formal and destination/phase mappings |
| AIRET-03 | Replacement publishes at task-return instead of completion | Incompatible caller contract |
| AIRET-04 | Replacement drops capture/reservation/cleanup obligations | Incompatible; no source/body guess |
| AIRET-05 | Private task/frame strategy changes while contracts remain | No incompatibility solely from physical implementation |
| AIRET-06 | Unknown required completion/phase semantics occur | Explicit rejection before affected effects |
| AIRET-07 | A producer publishes terminal cleanup/validity facts | Producer is responsible for their truth; consumers validate record structure, references and support, then may rely on those facts without semantic re-proof |
| AIRET-08 | Optional Attr/held table slot/opcode is used for mandatory phase semantics | No physical carrier assigned |
| AIRET-09 | Async-only cursor is used in synchronous eager spread | No implicit await, full materialization or incremental capture |
| AIRET-10 | Async by-value selection is used to infer lending or producer frame registration | Those catalogs retain independent requirements |
| AIRET-11 | Ordinary synchronous init output is invoked | Preserve existing call-return flow and omission rules |
| AIRET-12 | General Task/cancellation/supervisor APIs are inferred from this catalog | No such API or physical task ownership representation selected |
| AIRET-13 | Completion reservation is used to extend arbitrary Pure/NoAlias calls | Preserve existing invocation-phase schemas and separately checked later captures |
