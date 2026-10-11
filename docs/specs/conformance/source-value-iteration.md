# Value iteration conformance

These cases specify [value-iteration/1](../language/iteration.md), not an
implemented compiler, universal iterator ABI or physical metadata codec.
Each fixture supplies its exact selected witnesses, complete item/cursor
forms, publication and backing/ownership obligations.

## Identity and selection

| ID | Scenario | Required outcome |
|---|---|---|
| VITYPE-01 | An exact ISequence<T, TCursor> witness is available | Bind its exact readonly GetEnumerator and complete ICursor<T> result relationship |
| VITYPE-02 | An exact ICursor<T> value is the sole source role | Select direct traversal without factory acquisition |
| VITYPE-03 | A source only has GetEnumerator/TryMoveNext/Close names | No nominal protocol from member discovery |
| VITYPE-04 | Same-spelled user ICursor/ISequence identities are supplied | No Core iteration semantics from spelling |
| VITYPE-05 | Repeated inherited/imported paths reach one witness | Deduplicate exact identity |
| VITYPE-06 | Two distinct item/cursor relationships are available | Diagnose ambiguity, not body/literal-driven selection |
| VITYPE-07 | One source has both factory and direct-cursor roles | Diagnose distinct acquisition ambiguity even with equal item/cursor types |
| VITYPE-08 | A factory result merely converts to or wraps the required cursor | Reject absent an explicit checked adapter and exact result relationship |
| VITYPE-09 | A candidate factory requires source mutation/consumption instead of the selected readonly receiver | It is not this factory contract |
| VITYPE-10 | Cursor has MoveNext plus Current but no exact TryMoveNext | Require an explicit nominal adapter; synthesize neither getter nor advance |
| VITYPE-11 | Generic body declares one available sequence relationship | Bind it at definition; extra specialization facts do not retarget source |
| VITYPE-12 | Item/cursor cannot support required output/header storage or receiver authority | Diagnose before effects; mutable header storage grants no new backing access |
| VITYPE-13 | For item has an explicit incompatible type/access binding | Diagnose after source relationship selection; do not select another witness |
| VITYPE-14 | T is non-Copy and cursor publishes an independently owned item | No universal Copy bound; preserve ordinary transfer and cleanup |
| VITYPE-15 | ref/mut ref iteration sees only this by-value protocol | No source-element lending from the private output slot |
| VITYPE-16 | await for or async spread sees only this synchronous protocol | No automatic Task wrapper, blocking or async operations |
| VITYPE-17 | Optional items are bound with var instead of an eligible var? or proven non-null binding | No implicit unwrap; apply ordinary presence rules |

## Acquisition and ownership

| ID | Scenario | Required outcome |
|---|---|---|
| VIACQ-01 | A factory-source expression and GetEnumerator have effects | Evaluate source once, acquire once, then advance; retain actual effects |
| VIACQ-02 | A named non-Copy collection supports readonly factory acquisition | Borrow its eligible receiver rather than copy/move/dispose the collection |
| VIACQ-03 | A temporary factory source backs its cursor | Retain the source temporary through close and its ordinary header endpoint |
| VIACQ-04 | A factory forwards independently surviving backing | Preserve its published relationship; do not invent receiver-place dependence or erase actual backing |
| VIACQ-05 | Factory acquisition fails before complete cursor publication | Ordinary partial/result cleanup; no Close on uninitialized cursor |
| VIACQ-06 | A fresh affine direct cursor is supplied | Adopt its value without GetEnumerator, reset or cloning |
| VIACQ-07 | A named affine direct cursor is supplied without move | Diagnose missing explicit transfer |
| VIACQ-08 | for source is move cursor for an eligible named affine cursor | Source becomes vacant; loop receives traversal and close responsibility |
| VIACQ-09 | A moved direct traversal exits early or faults | No source revival/ownership rollback |
| VIACQ-10 | A Copy cursor carries shared backing/state | Preserve its actual copy/alias/close contract, not presumed independent traversal |
| VIACQ-11 | A cursor result borrows a source place/root/lease | Keep every required dependency through traversal and close |
| VIACQ-12 | Implementation body suggests a narrower backing relationship than published | Do not strengthen the caller's contract from the private body |
| VIACQ-13 | Source has completed state but no supported traversal relationship | A value name/state does not invent a witness |
| VIACQ-14 | A class cursor is returned readonly while mutation needs write access | Diagnose invalid carrier/receiver use; do not upgrade it |

## Advance and publication

| ID | Scenario | Required outcome |
|---|---|---|
| VIADV-01 | TryMoveNext enters with exact vacant output and completes true | Publish one complete valid T after normal exit checks/cleanup |
| VIADV-02 | It completes false | Leave output vacant and establish exhaustion, not a default item |
| VIADV-03 | A live output is supplied directly | Require ordinary vacancy preparation; no hidden retirement |
| VIADV-04 | True is paired with vacant/partial/invalid output | Reject contradictory implementation contract |
| VIADV-05 | False is paired with a live output | Reject contradictory implementation contract and preserve cleanup obligations |
| VIADV-06 | TryMoveNext faults after preparing a private item | No caller-readable item; clean partial/unpublished output and preserve actual cursor effects |
| VIADV-07 | An authored exit check/cleanup faults after item formation | Prevent publication and clean the unpublished item |
| VIADV-08 | T is void | True has one live unit item; false has no initialized item |
| VIADV-09 | T is optional and its published value is absent | Valid initialized item, not exhaustion |
| VIADV-10 | An exhausted cursor is advanced again | No resumed production/new item; selected call effects remain |
| VIADV-11 | Producer/callback fault occurs | Propagate the actual failure, not false |
| VIADV-12 | Failed advancement is caught under recovery authorization | Vacancy/catch does not establish cursor validity or retry permission |
| VIADV-13 | An item view borrows cursor storage | Retain its loan and constrain advance/close/escape |
| VIADV-14 | Item view forwards independent backing under a published relationship | Use that actual backing extent, not presumed cursor storage |
| VIADV-15 | Cursor copies items out of a borrowed non-Copy source without permission | Reject unauthorized ownership duplication/consumption |
| VIADV-16 | Named affine item is emitted by an owning cursor | Ordinary explicit move/transfer; one owner after publication |
| VIADV-17 | Boolean/init shape is used to claim Pure, ConstEval or NoAlias | No such annotation/capability follows |
| VIADV-18 | Advancement or its exit checks fail after changing cursor state | Preserve cleanup-safe Close/residual state; required closing does not authorize traversal recovery |

## Loop and close order

| ID | Scenario | Required outcome |
|---|---|---|
| VILOOP-01 | Three items then exhaustion are produced | Acquire/adopt once; each advance publishes before its body; item cleanup precedes next advance; close once |
| VILOOP-02 | Empty source exhausts immediately | No body/default item; preserve advancement and one required close |
| VILOOP-03 | Body uses continue | Retire body/item obligations before following advance |
| VILOOP-04 | Body uses break | Retire body/item, close cursor, then retire dependent source/header state |
| VILOOP-05 | Body returns a separate result | Close and ordinary cleanup before invocation completion |
| VILOOP-06 | Body or item retirement faults | Follow ordinary unwind/close/failure chaining, with no next advance |
| VILOOP-07 | Advancement faults | No body for unpublished output; close actual acquired state once |
| VILOOP-08 | Acquisition faults | No acquired traversal to close; preserve partial source/result cleanup |
| VILOOP-09 | A successful item is moved out of its loop binding | Do not retire it again through that now-vacant binding |
| VILOOP-10 | Item is zero-data but owns no payload | Initialization/body/retirement ordering still occurs |
| VILOOP-11 | Physical item storage is reused | Vacate legally; no per-iteration retained arena or old owner |
| VILOOP-12 | A retained item/source loan would be invalidated by next advance or close | Diagnose invalid plan; no early lifetime end or silent copy/boxing repair |
| VILOOP-13 | Inner loop performs named transfer to an outer loop | Close/clean intervening traversal and item scopes in ordinary order |
| VILOOP-14 | Close faults after another failure | Preserve causal cleanup chaining, not suppression or successful exhaustion |
| VILOOP-15 | Close has side effects on normal exhaustion | Execute it once; do not erase it as a destructor or inferred NoThrow call |
| VILOOP-16 | Producer cleanup already completed during terminal advancement | Required Close cannot rerun completed body/resource cleanup |
| VILOOP-17 | Cursor value retires after its required Close | Clean only residual obligations; no double close/release |
| VILOOP-18 | Loop borrows an original collection as source | Closing cursor does not dispose the collection |

## Eager collection spreads

| ID | Scenario | Required outcome |
|---|---|---|
| VISPREAD-01 | [A(), ..Sequence(), B()] uses factory source | Capture A/source/B before builder; acquire cursor only at reached spread drain |
| VISPREAD-02 | Earlier acceptance refuses before factory spread | Source capture effects remain; no factory acquisition/advancement |
| VISPREAD-03 | A direct cursor is created/moved during source capture | Adopt its close obligation there without advancing |
| VISPREAD-04 | A later capture fails after direct cursor capture | Close that unadvanced cursor during abandonment; do not start its body |
| VISPREAD-05 | Builder acquisition refuses/faults after direct cursor capture | Close captured cursor even though drain never began |
| VISPREAD-06 | Earlier acceptance refuses before direct cursor spread | Close the already-captured cursor; no advancement/GetEnumerator |
| VISPREAD-07 | Reached spread completes normally | Each true item reaches TryAdd once; false then close precedes later fragments |
| VISPREAD-08 | TryAdd refuses a consumed item | Rejected-input holder cleans it once; close cursor and stop later drain |
| VISPREAD-09 | Reached cursor faults during advance | No item/accept/default; close acquired state and abandon builder/captures |
| VISPREAD-10 | Item was published but transfer/accept staging fails | Clean its actual remaining owner before cursor close |
| VISPREAD-11 | Reached spread stops while later direct cursor is already captured | Close both appropriate obligations once without advancing the later one |
| VISPREAD-12 | Spread adapter uses an effectful ordinary Current internally | Standard caller only invokes TryMoveNext; adapter preserves its selected item observation once |
| VISPREAD-13 | Builder would retain an expired cursor-backed item | Reject incompatible storage/publication lifetime |
| VISPREAD-14 | Spread sees borrowed collection elements | Copy only with actual permission; no implicit move from borrowed slots |
| VISPREAD-15 | A spread seeks length/count or repeats enumeration | No speculative pass, replay or implied materialization |
| VISPREAD-16 | Empty spread has actual acquisition/close effects | Preserve them and backing dependencies |
| VISPREAD-17 | By-value spread item differs from selected construction TItem | Exact binding fails; no inverse witness search or implicit conversion |

## Independent contracts and boundaries

| ID | Scenario | Required outcome |
|---|---|---|
| VIKEEP-01 | A retained loop records exact interface/member/witness and source mode | Bind those identities rather than source names |
| VIKEEP-02 | Generic substitution closes T/TCursor/source terms | Preserve qualification, nominal identity and transfer/backing relationships |
| VIKEEP-03 | Required value-iteration rules/operations are absent | Reject before affected capture/loop effects |
| VIKEEP-04 | Linker/remapper changes rows or strips debug names | Preserve semantic relationships/schedule/close obligations |
| VIKEEP-05 | Same-spelled imported member is offered as replacement | No implicit rebinding or body-derived adapter |
| VIKEEP-06 | Replacement changes false/vacancy, close timing or source consumption | Incompatible retained contract |
| VIKEEP-07 | A result dependency/refinement disappears | Incompatible; do not infer old behavior from implementation |
| VIKEEP-08 | Untrusted producer claims safe output/cleanup | Use explicit semantic audit/decline policy; no trust flag supplies truth |
| VIKEEP-09 | Authored exception/close effects are omitted from retained contract | Reject mismatched effect/failure obligations |
| VIKEEP-10 | Standard roles are attached through optional Attr/reserved opcode data | No mandatory physical carrier is assigned |
| VIKEEP-11 | Iterator producer has affine captures and direct cursor result | Source mode can preserve one-shot ownership; exact generated frame/phase contracts are still required |
| VIKEEP-12 | Factory-style producer cannot legally copy/retain its captures | Sequence shape does not invent duplication/re-enumeration |
| VIKEEP-13 | Lending/async or generated producer forms lack their own selected contracts | Explicit unsupported requirement; no by-value/synchronous fallback |
| VIKEEP-14 | A consumer treats value iteration as incremental text/collection handler policy | No deferred construction semantics; eager source capture is unchanged |
