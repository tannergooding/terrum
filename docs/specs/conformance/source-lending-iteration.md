# Reference-output lending iteration conformance

These cases specify [lending-iteration/1](../language/lending-iteration.md),
not an implemented compiler, general lifetime prover or reference/task ABI.
Fixtures supply exact published witnesses, output/source/refinement records
and admitted element, cursor, storage, access and backing facts.

## Roles and mode selection

| ID | Scenario | Required outcome |
|---|---|---|
| LIROLE-01 | ref loop has one exact ILendingSequence<T, TCursor> | Select its readonly synchronous factory and readonly output cursor |
| LIROLE-02 | mut ref loop has one exact IWritableLendingSequence<T, TCursor> | Select its mut synchronous factory and writable output cursor |
| LIROLE-03 | await ref loop has one exact IAsyncLendingSequence<T, TCursor> | Synchronous readonly acquisition, completion-published readonly descriptor and awaited close |
| LIROLE-04 | await mut ref loop has one exact IAsyncWritableLendingSequence<T, TCursor> | Synchronous mut acquisition, completion-published writable descriptor and awaited close |
| LIROLE-05 | Each mode instead has its exact direct cursor as sole role | Adopt existing traversal without factory/reset |
| LIROLE-06 | Source only has matching advance/Current/close names or user same-spelled interfaces | No Core role from spelling or shape |
| LIROLE-07 | Source has only ICursor<ref<T>> or IAsyncCursor<write ref<T>> | These are ordinary value items, not element lending |
| LIROLE-08 | Source has both a value witness and one eligible lending witness | Filter by written mode before ambiguity; do not conflate witnesses |
| LIROLE-09 | Repeated paths reach one exact lending witness | Deduplicate identity |
| LIROLE-10 | Two eligible relationships in the selected lending mode remain | Diagnose ambiguity even with equal T |
| LIROLE-11 | Both factory and direct cursor are eligible in the selected mode | Diagnose distinct acquisition ambiguity |
| LIROLE-12 | Readonly mode sees only a writable lending role | No automatic role attenuation; require a declared witness or explicit adapter |
| LIROLE-13 | Writable mode sees only a readonly role | No authority upgrade |
| LIROLE-14 | Async mode sees only synchronous roles or vice versa | No Task wrapper, blocking or fallback |
| LIROLE-15 | Writable implementation substitutes `ref<init write ref<T>>` for a readonly output | Reject invariant destination mismatch, not covariance |
| LIROLE-16 | Typed alias changes an inner qualification of selected T | Reject after selection; do not reinterpret storage |
| LIROLE-17 | T is an addressable non-Copy element | Lending does not require copying T |
| LIROLE-18 | T is void with no exposable payload address | Reject lending formation; by-value unit iteration remains valid |
| LIROLE-19 | T is an initialized optional element | True can publish its descriptor even when T is absent |
| LIROLE-20 | Generic body has only a broad lending witness | Keep its selected role/bound; concrete specialization cannot expose private refinement |
| LIROLE-21 | Qualified-self adaptation changes nested init destination type | Reject projection; a body returning writable storage cannot repair it |
| LIROLE-22 | Actual cursor/output storage or receiver access is unsupported | Diagnose before affected invocation |

## Acquisition and ownership

| ID | Scenario | Required outcome |
|---|---|---|
| LIACQ-01 | Source evaluation and selected factory have effects | Evaluate once, acquire once, then advance |
| LIACQ-02 | Readonly factory borrows a non-Copy inline collection | No implicit copy/move of the collection |
| LIACQ-03 | Writable factory receives an eligible writable inline place | Use its actual mut receiver access and retain source storage |
| LIACQ-04 | Writable factory receives a readonly source place or readonly class handle | Reject; mutable header storage does not upgrade it |
| LIACQ-05 | A mutable view descriptor has readonly backing | Mutability alone cannot supply writable elements |
| LIACQ-06 | A readonly element slot contains write C | Retain object-write authority but do not grant slot replacement |
| LIACQ-07 | Writable-source expression produces an otherwise eligible temporary | Ordinary header materialization retains actual authority/backing through close |
| LIACQ-08 | Factory fails before complete cursor publication | Clean private/partial result; no close on vacancy |
| LIACQ-09 | Async lending factory would return Task<TCursor> | Not the selected synchronous acquisition contract |
| LIACQ-10 | Named affine direct cursor is supplied without move | Diagnose missing transfer |
| LIACQ-11 | Named affine direct cursor is explicitly moved | Adopt once; original binding remains vacant on every exit |
| LIACQ-12 | A Copy direct cursor shares actual traversal/backing | Preserve alias/close semantics, not independent traversal |
| LIACQ-13 | Cursor acquisition borrows receiver storage or forwards backing | Preserve its distinct checked result relationship through required close |
| LIACQ-14 | Failed synchronous factory strands started borrowed async work | Reject contract; no unowned pending capture/cleanup |
| LIACQ-15 | Traversal is closed after successful adoption | End cursor obligations, not source collection/element ownership |

## Descriptor publication

| ID | Scenario | Required outcome |
|---|---|---|
| LIOUT-01 | Synchronous readonly advance completes true into vacant ref<T> output | Publish one complete descriptor to a live permitted T place after exit checks |
| LIOUT-02 | Writable advance completes true into vacant write ref<T> output | Preserve real immediate element-write authority and complete stored T |
| LIOUT-03 | Advance completes false | Vacant descriptor output, exhausted traversal and no body |
| LIOUT-04 | Direct caller supplies a live descriptor output | Require ordinary vacancy preparation; no hidden retirement |
| LIOUT-05 | True has vacant/partial/null/invalid descriptor | Reject contradictory output contract |
| LIOUT-06 | True points to dead local, unavailable element or unauthorized source | Reject element origin/availability/authority |
| LIOUT-07 | False retains a live privately formed descriptor | Reject; release private loan state without destroying T |
| LIOUT-08 | Body/check/authored cleanup faults after forming a descriptor | Prevent publication; release unpublished loan once and preserve cursor effects |
| LIOUT-09 | Failure cleans output whose referent is source-owned | Do not destroy or move the element |
| LIOUT-10 | Cursor has a real live scratch T place | May lend it with the broad cursor dependency |
| LIOUT-11 | Implementation borrows hidden descriptor output as a fabricated T element | Reject source/place substitution |
| LIOUT-12 | T is optional and absent at the selected live slot | Valid true item, not false/exhaustion |
| LIOUT-13 | Exhausted traversal is advanced again | No resumed production; declared call effects and close remain |
| LIOUT-14 | Published descriptor is copied, moved or retired | Preserve surviving loan obligations; retirement never destroys T |
| LIOUT-15 | Saved true outcome refers to a retired/reused descriptor destination | No live-output proof for a new version |
| LIOUT-16 | Eligible synchronous call uses ordinary omitted-output require/matching | Lift the descriptor with its dependencies, not an owning T or hidden-slot element alias |
| LIOUT-17 | Async call omits its descriptor output | No implicit later-completion lifting |
| LIOUT-18 | Failure changes traversal state | Required close remains safe; vacancy/catch does not authorize retry |
| LIOUT-19 | Cursor owns scratch T whose dependent loans end before close | Loan retirement leaves T live; its actual cursor holder may then retire it once, unlike borrowed collection elements |

## Backing and invalidation

| ID | Scenario | Required outcome |
|---|---|---|
| LIBACK-01 | Output has only the standard broad contract | Retain possible cursor storage and all checked source/backing bounds |
| LIBACK-02 | Broad loan survives until a next advance | Reject invalidating advance while any dependent loan survives |
| LIBACK-03 | Broad loan survives until close, cursor move or retirement | Reject invalidating operation |
| LIBACK-04 | Original descriptor ends but a copied descriptor or reborrow survives | Keep that loan's invalidation obligations |
| LIBACK-05 | Cursor copy has a different slot/address but shares traversal | It cannot bypass the surviving loan bound |
| LIBACK-06 | Refinement excludes cursor storage and preserves backing through advance/close | Loan can survive those operations when all remaining backing obligations survive |
| LIBACK-07 | Refinement permits surviving advance but not close | Later advance can be valid; close still requires loan ending |
| LIBACK-08 | Refinement excludes cursor storage but close releases required guard/backing | No survival past close |
| LIBACK-09 | Current body forwards stable backing but public contract is broad | Keep broad bound; no private-body narrowing |
| LIBACK-10 | Refinement is conditional and available condition is not proven | Keep broad bound |
| LIBACK-11 | Generic declaration exposes exact refinement and proves its condition | Use that checked narrower relationship |
| LIBACK-12 | Multiple possible sources are listed | Retain every listed source absent a checked excluding refinement |
| LIBACK-13 | Rooted managed object can replace/reallocate/remove source storage | Root alone does not preserve the element |
| LIBACK-14 | Native wrapper remains rooted after disposal/pool return | Native element loan becomes invalid; root/address equality is insufficient |
| LIBACK-15 | Required guard/lease ends while a refined loan survives | Reject invalidation despite cursor-independent descriptor |
| LIBACK-16 | Managed interior moves while its supported tracked root remains | Retain tracked interior/root relationship, not pin/raw-address assumptions |
| LIBACK-17 | Claimed forwarded path secretly refers to cursor inline storage | Reject unsound source selector/refinement |
| LIBACK-18 | Advance/close fault or residual cleanup invalidates supposedly surviving backing | Reject incomplete refinement proof |
| LIBACK-19 | Adapter widens a valid output bound to cursor-scoped | Conservative widening is permitted with the same real dependencies |
| LIBACK-20 | Adapter publishes an unsupported narrower bound | Reject, including conditional/source/phase mappings |
| LIBACK-21 | Loan remains valid but another authorized alias changes element contents | Validity is not immutable contents, stable reads or purity |
| LIBACK-22 | Writable reference is used to infer exclusive access or NoAlias | No such guarantee from the role |
| LIBACK-23 | Output requires undeclared pin/retain/guard acquisition to survive | No implicit acquisition or lifetime repair |
| LIBACK-24 | A source path has no admitted capture/backing relationship | Path spelling cannot manufacture opaque backing metadata |

## Element-place alias behavior

| ID | Scenario | Required outcome |
|---|---|---|
| LIALIAS-01 | ref T item is live in the loop body | Name denotes original readonly T place, not descriptor slot or copy |
| LIALIAS-02 | mut ref T item is assigned an eligible T | Update original writable element under ordinary source validity/ownership |
| LIALIAS-03 | readonly alias is assigned | Reject immediate write |
| LIALIAS-04 | ref item is formed from a live alias | Reborrow same element with no longer extent or greater authority |
| LIALIAS-05 | Alias is read by value when T is non-Copy | No implicit copy/ownership extraction |
| LIALIAS-06 | item = _ ends a loop alias | Release binding loan, leave source T initialized and owned |
| LIALIAS-07 | Ended alias is initialization-tested or read | State test observes binding vacancy without dereference; value/place use rejects |
| LIALIAS-08 | Ended alias is reassigned/rebound/reinitialized | Reject; mode mut ref is element write, not mutable descriptor binding |
| LIALIAS-09 | Copied ref item survives original binding end | Its actual backing/invalidating-operation bound remains |
| LIALIAS-10 | By-value for has mut ref<T> item | Local holds a rebindable readonly descriptor; ordinary explicit indirection remains |
| LIALIAS-11 | Lending T is itself ref<U> | Alias names existing descriptor slot; U access requires explicit indirection |
| LIALIAS-12 | T is C, write C or a nested qualified handle/reference | Preserve slot/referent distinctions and exact stored T |
| LIALIAS-13 | Writable alias would move/retire T and leave container slot vacant | Lending alone supplies no extraction/surviving-state contract |
| LIALIAS-14 | Writable alias addresses immutable field or prohibited replacement | Reject ordinary source/member invariant violation |
| LIALIAS-15 | Each true step enters a body and then continues | Separate body alias activations; retire binding loan before next advance |
| LIALIAS-16 | Alias/copy is captured by a callable or suspended operation | Preserve its actual declared destination/capture/backing/affinity obligations |
| LIALIAS-17 | Optional element is inferred with non-null var without proof | Reject unwrap; use eligible var? or proven non-nullness |
| LIALIAS-18 | Grouping or member access uses a live alias | Preserve original T place and ordinary lookup, not transparent descriptor forwarding |

## Async reservations and output loans

| ID | Scenario | Required outcome |
|---|---|---|
| LIPEND-01 | Async lending advance returns an outstanding operation | Reserve exact descriptor destination/version and required cursor access |
| LIPEND-02 | Caller or alias reads/state-tests/initializes/retires pending output | Reject until checked completion observation/cleanup |
| LIPEND-03 | Descriptor writes or IsCompleted occur before observation | No caller availability or discharged reservation |
| LIPEND-04 | Checked terminal observation completes true/false/failure | Publish live descriptor or vacancy with exact saved Boolean relation |
| LIPEND-05 | Cancellation is requested or a wait is abandoned | Reservation and responsible completion owner remain |
| LIPEND-06 | Published loan survives after operation/task retirement | Its independent element backing bound still applies |
| LIPEND-07 | Task is retained but element owner/guard ends | Retained task does not preserve element lifetime |
| LIPEND-08 | Broad earlier loan is live when next async advance starts | Reject invalidating advance |
| LIPEND-09 | Available refinement permits earlier loan through next advance | Also check pending operation's actual compatible access/effects/captures |
| LIPEND-10 | Valid earlier writable loan conflicts with pending captured access | Non-invalidation does not grant disjointness or concurrent mutation |
| LIPEND-11 | Loan crosses a body suspension | Require actual backing/root/guard/affinity support |
| LIPEND-12 | Async wrapper awaits inner reference output | Inner loan is private to wrapper until outer checks/cleanup and publication |
| LIPEND-13 | Outer wrapper faults after inner descriptor publication | Release unpublished loan, leave referent ownership unchanged |
| LIPEND-14 | Branch join retains one pending and one vacant descriptor path | Preserve guarded reservation; no ordinary state-testable maybe-live conversion |
| LIPEND-15 | Destination/task physical storage is reused | Fresh logical operation/output version; old result cannot revive descriptor |
| LIPEND-16 | Output/responsibility is forwarded | Preserve phase, exact formal/destination/source maps and one responsible owner |

## Loop, close and escape

| ID | Scenario | Required outcome |
|---|---|---|
| LILOOP-01 | Three lent elements then exhaustion | Acquire once; publish before body; end body loan before next step; close once |
| LILOOP-02 | Empty lending source | No body/element/default descriptor; close once |
| LILOOP-03 | Body continues, breaks or returns an independent result | Retire body locals/alias loan, then next advance or required close in order |
| LILOOP-04 | Advance/body/loan cleanup faults | Close cleanup-safe adopted traversal; do not retry or enter unpublished body |
| LILOOP-05 | Acquisition faults | No cursor close; actual private/partial cleanup |
| LILOOP-06 | Close faults after a primary failure | Preserve causal failure chaining and residual cleanup; no second close |
| LILOOP-07 | Async exit arrives while advance is pending | Settle operation, handle published loan, then await close before header exit |
| LILOOP-08 | Cursor was never advanced | Close existing traversal obligations without starting producer body |
| LILOOP-09 | Producer/body cleanup already ran | Close/residual cursor cleanup cannot replay it |
| LILOOP-10 | Broad loan is returned/stored past loop close | Reject incompatible invalidation/escape |
| LILOOP-11 | Refined cursor-independent loan has genuinely surviving backing | Escape can preserve actual source/guard/retention obligations |
| LILOOP-12 | Escaping loan borrows header's temporary owner/guard | Reject past its cleanup, despite Copy/boxing/root equality |
| LILOOP-13 | Temporary descriptor only forwards proven longer-lived backing | No invented descriptor-place dependency; retain real backing and refinement |
| LILOOP-14 | A copied/reborrowed loan outlives body binding | Loop checks surviving dependency before advance/close, not just local retirement |
| LILOOP-15 | Nested loop makes named outer transfer | Clean/close intervening alias/traversal scopes in ordinary order |
| LILOOP-16 | Synchronous spread sees only lending witness | No automatic dereference/copy; require explicit eligible value adapter |
| LILOOP-17 | Eager construction captures a reference-valued value source | Ordinary value dependencies/capture remain; no deferred/lending mode |
| LILOOP-18 | Invalid escape would work with retained per-item owners/arena | No hidden lifetime-extension storage or transfer of source cleanup |

## Independent relationships and boundaries

| ID | Scenario | Required outcome |
|---|---|---|
| LIRET-01 | Retained use records exact timing/access/source/witness/output identities | Preserve semantic identities independently of names/row positions |
| LIRET-02 | Output selector names Boolean return or init parameter descriptor slot | Reject; relationship selects initialized inner reference value of exact init formal |
| LIRET-03 | Required lending/output/backing/refinement rule support is absent | Reject before affected use, not value/readonly/async fallback |
| LIRET-04 | Replacement weakens relied-on backing refinement or changes invalidation/authority | Incompatible retained contract |
| LIRET-05 | Replacement has stronger compatible implementation-only lifetime facts | Do not silently upgrade generic retained broad bound |
| LIRET-06 | Forwarding/refinement removes a source, phase or cleanup dependency without proof | Reject incompatible mapped contract |
| LIRET-07 | Linker remaps member/formal/output/source/condition rows or strips debug names | Preserve complete semantic relationships and schedules |
| LIRET-08 | Consumer uses producer-published lending facts without access to the private body | Permit reliance after required decoding, compatibility and caller checks; producer remains responsible for truth |
| LIRET-09 | Optional Attr data, held opcode or ref layout is treated as a mandatory lending carrier | No physical allocation/codec is selected |
| LIRET-10 | Example uses return DependsOn to describe init-output/receiver capture | Wrong selector/relationship; no `"this"` or new annotation syntax adopted |
| LIRET-11 | Span/List/MatchedAccess name is treated as automatic lending witness/refinement | Require separately admitted exact witness and backing relationship |
| LIRET-12 | Associated Item<'a> or generated lending producer is inferred | Those forms retain independent unselected schemas |
| LIRET-13 | New standard lending roles are treated as required freestanding substrate | Required references/task/backing capabilities remain profile-explicit |
| LIRET-14 | Logical relationship model passes supplied fixtures | Not arbitrary source-body lifetime, GC, concurrent execution or physical codec conformance |
