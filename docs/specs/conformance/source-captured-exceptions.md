# Captured exception conformance

These cases reference [captured exceptions](../language/captured-exceptions.md),
[local failures](../language/local-failures.md), ordinary
[ownership](../language/ownership-and-cleanup.md) and
[failure exits](../language-contracts.md#33-normal-and-exceptional-exits).
They specify required outcomes, not implemented compiler/runtime support.
Each scenario supplies complete eligible payloads, source scopes and runtime
programs unless it explicitly tests a missing obligation.

Finite assessment supplies exact identities, lexical-region flags, handler
activations, event/owner/input states, dependency generations, ordered cleanup
actions and failure channels. It does not implement Terrum parsing, generic
inference, arbitrary body proof, root/borrow lowering, actual capture allocation,
native containment or physical codecs. CPID-14, CPLX-20, CPHN-16, CPBR-18,
CPTH-17, CPRT-18 and CPAD-17 require independent implementation evidence.
All other cases are assessable only against their stated finite facts.

## Exact Core identity and value role

| Case | Scenario | Required outcome |
|---|---|---|
| CPID-01 | Exact Core public nongeneric opaque unique CapturedException and static zero-parameter Capture | Register the distinct owner and lexical creation roles |
| CPID-02 | Same-named type from another module claims the owner role | No compiler-owned event role from spelling or layout |
| CPID-03 | Same-named member/extension claims lexical capture | No role from name/signature; require exact Core member |
| CPID-04 | Transparent alias of exact Core owner/member-containing type | Preserve normalized identity and consuming route |
| CPID-05 | Annotated wrapper contains a captured owner and is freshly thrown | New wrapper event/origin; do not flatten the captured field |
| CPID-06 | Owner or owning wrapper claims ordinary Copy | Reject duplication of its event responsibility |
| CPID-07 | CapturedException is used as ordinary Exception payload/catch subject | Reject; this distinct owner has no Exception eligibility |
| CPID-08 | Constructor/type-call/default/zero/null creates a captured owner | No such creation contract or empty complete value |
| CPID-09 | Capture registration has parameters, generic arity or a different result | Reject incorrect registered signature |
| CPID-10 | Payload, descriptor or native packet converts directly to the owner | No implicit conversion/adoption; require separate admitted operation |
| CPID-11 | Outer ref or same-bits value is treated as the captured owner | No consuming role or second owner through a descriptor |
| CPID-12 | Public fields or partial internal moves expose opaque event state | Reject unsupported structure/publication |
| CPID-13 | Registration asserts implicit sharing, thread transfer, purity or constant eligibility | None follows from this initial Core selection |
| CPID-14 | Independently produced Core bootstrap and compiler-known member registration | Document-only; need real library/compiler identity evidence |

## Lexical context and active handler selection

| Case | Scenario | Required outcome |
|---|---|---|
| CPLX-01 | Direct no-argument Capture in active source handler body | Select that activation's owned event |
| CPLX-02 | Capture in ordinary nested blocks of that body | Retain the same source handler |
| CPLX-03 | Body-owned finally/defer executes Capture while source still owns event | Use its execution-point source state and ordinary cleanup bounds |
| CPLX-04 | Capture outside any source handler | Diagnose missing lexical source |
| CPLX-05 | Capture in a filter, even with an enclosing active handler | Diagnose forbidden source region |
| CPLX-06 | Separately declared lambda inside handler invokes Capture | Lambda receives no enclosing handler context |
| CPLX-07 | Called helper invokes Capture while caller is handling an event | Helper receives no dynamic current-exception permission |
| CPLX-08 | Reentrant callback executes on the handler's thread | Callback cannot acquire the caller's source context |
| CPLX-09 | Helper is inlined or devirtualized into the handler | Preserve original source invalidity |
| CPLX-10 | Capture is converted/invoked as func/fnptr or reflected target | No first-class indirect lexical source |
| CPLX-11 | Entered nested handler invokes Capture | Select nested handler's own event |
| CPLX-12 | Nested handler already captured its event while outer still owns another | Reject reuse; do not fall back to outer event |
| CPLX-13 | Nested captured handler ends normally, then outer calls Capture | Restore outer lexical selection without retiring nested captured event |
| CPLX-14 | Nested try body has not entered a new handler | Capture still selects surrounding active handler |
| CPLX-15 | Supported async resumption changes execution thread | Retain source callable/handler activation and actual state |
| CPLX-16 | Producer resumes the same entered handler after yield | No fresh handler activation or regenerated event owner |
| CPLX-17 | Defer registers Capture before another path transfers the source event | Check ownership at cleanup execution; reject later reuse |
| CPLX-18 | Helper receives an explicit captured owner and later throws it | Valid consuming propagation without permission to Capture caller state |
| CPLX-19 | Direct Capture supplies arguments or explicit generic arguments | Reject; exact member has zero parameters and generic arity |
| CPLX-20 | Actual source parsing, nested callables, filter regions and generated ownership | Document-only; finite region flags do not prove compiler classification |

## Handler ownership, creation and publication

| Case | Scenario | Required outcome |
|---|---|---|
| CPHN-01 | Successful complete Capture publication | Exactly one owner replaces handler responsibility; event/payload/origin/causes unchanged |
| CPHN-02 | Unique non-Copy payload is captured | No payload copy, constructor or original-throw replay |
| CPHN-03 | Preparation fails before publication | No result; source still owns original event and actual failure remains distinct |
| CPHN-04 | Inner recovery handles preparation failure, then original handler retries | Retry can capture its still-owned original event |
| CPHN-05 | Preparation failure escapes original responsible handler | Replacement retains original as cause after ordinary input/cleanup exits |
| CPHN-06 | Preparation acquired multiple private states before failure | Clean remaining acquisitions in supplied reverse order; preserve actual effects |
| CPHN-07 | Destination/call failure occurs after successful publication | Actual staged/transferred owner remains responsible; no source restoration |
| CPHN-08 | Delivery fails after capture responsibility transfer | Retain the published/staged owner or admitted settlement; never lose the event |
| CPHN-09 | Same handler calls Capture twice | Reject second use |
| CPHN-10 | Same handler uses bare rethrow after successful Capture | Reject consumed source responsibility |
| CPHN-11 | Handler ends normally with captured event in independent valid holder | End inputs; do not retire that holder's event |
| CPHN-12 | Flow join mixes owned and transferred handler paths | Require owned state on every path reaching Capture/bare rethrow |
| CPHN-13 | Captured owner moves or retires before source handler tries reuse | Neither operation revives source responsibility |
| CPHN-14 | Captured event is consumed, caught by a new handler and captured again | New affine owner activation may retain the same event/origin |
| CPHN-15 | Runtime tries to publish vacant, partial or wrong-owner state | Reject incomplete publication before source transfer |
| CPHN-16 | Physical capture allocation/roots/delivery and actual OOM paths | Document-only; finite preparation outcomes are not implementation/OOM proof |

## Catch inputs and complete dependency closure

| Case | Scenario | Required outcome |
|---|---|---|
| CPBR-01 | Capture while readonly unique-payload catch input is live | Preserve valid input use and same payload under the new owner |
| CPBR-02 | Runtime attempts to copy or implicitly end that input to capture | Neither action is required/permitted as a hidden repair |
| CPBR-03 | Catch reference descriptor is copied/reborrowed within its valid source extent | Keep descriptor copies bounded to that original extent |
| CPBR-04 | Permitted alias changes payload-reachable state after Capture | Later valid input use observes actual changed state, not a snapshot |
| CPBR-05 | Copy catch input remains live while captured event retires; an ordinary payload copy escapes | Copies retain their own backing/value contracts, not event responsibility or a handler-bound descriptor extent |
| CPBR-06 | Catch descriptor escapes handler while captured owner stays alive | Diagnose original input-extent violation |
| CPBR-07 | Captured payload's source storage generation changes | Reject affected use/transfer/retirement |
| CPBR-08 | Returned captured event depends on the exited source frame | Reject escape; retained owner/root does not extend frame lifetime |
| CPBR-09 | Captured payload/cause needs a guard that has ended | Reject affected path despite unchanged bytes |
| CPBR-10 | Only a retained earlier cause has an invalid dependency | Reject; top payload independence cannot hide cause closure |
| CPBR-11 | Owner's holder slot is separately borrowed | Block conflicting move/retirement; descriptor is not another owner |
| CPBR-12 | Owner transfers to another valid holder while catch input remains live | Preserve event/input source; source holder becomes vacant |
| CPBR-13 | Handler-local captured owner would retire before live catch input | Reject ordinary cleanup edge; no topological reorder or silent input end |
| CPBR-14 | Staged return owner survives cleanup that ends original input | Permit dependency-valid owner escape after input end |
| CPBR-15 | Direct captured throw stages owner before ending handler input | Keep event live through cleanup; select outside only after input ends |
| CPBR-16 | Native/domain/code/runtime backing is retained through a cause | Preserve those complete dependencies through capture/move/retirement |
| CPBR-17 | Payload has pending protocol/task obligations at capture/transfer | Transfer actual obligations; do not mark them settled |
| CPBR-18 | Actual compiler root/borrow enforcement and arbitrary backing paths | Document-only; finite generation/borrow flags are not lowering proof |

## Consuming propagation and causes

| Case | Scenario | Required outcome |
|---|---|---|
| CPTH-01 | Named captured owner is thrown without move, even with Copy payload | Diagnose missing explicit owner transfer |
| CPTH-02 | Fresh Capture result is directly thrown | Transfer once without named move and preserve original event |
| CPTH-03 | Captured throw operand has observable evaluation | Evaluate once before transfer/cleanup |
| CPTH-04 | Operand evaluation fails before producing its captured owner | Propagate actual operand failure; do not fabricate/consume an outer owner |
| CPTH-05 | Normalized exact alias is the captured throw operand | Use same-event consuming route |
| CPTH-06 | Operand is vacant, partial, readonly descriptor or wrong nominal type | Reject missing complete owning value |
| CPTH-07 | Ordinary copied payload is freshly thrown instead | New event/origin; do not equate it with captured propagation |
| CPTH-08 | Cleanup inspects the source holder after captured throw stages | Source already vacant; pending extent owns the live event |
| CPTH-09 | Reference handler consumes just-captured owner and exits | End live inputs/cleanup before outward handler selection |
| CPTH-10 | Authored cleanup replaces requested captured propagation | Preserve requested event as cause; no replay or source revival |
| CPTH-11 | Requested event already owns an earlier cause chain | Retain the complete earlier chain beneath replacement |
| CPTH-12 | Replacement would self-cause, duplicate a cause owner or create a cycle | Reject invalid responsibility graph |
| CPTH-13 | Different still-responsible active handler is exited by captured throw | Its active event becomes a cause under ordinary replacement |
| CPTH-14 | Unrelated failure exits handler whose original event is saved elsewhere | Do not adopt that independently owned event again |
| CPTH-15 | Captured propagation delivery fails after source transfer | Pending extent retains responsibility; source stays vacant |
| CPTH-16 | Valid owner is thrown later outside any source handler/helper context | Consuming propagation requires owner, not lexical Capture permission |
| CPTH-17 | Real source/runtime staging, handler selection, cleanup and unwind | Document-only; supplied schedules do not prove generated execution |

## Retirement, effects and execution phases

| Case | Scenario | Required outcome |
|---|---|---|
| CPRT-01 | Complete owner retires with all dependencies/obligations satisfied | Exactly-once payload/cause retirement without repropagating original |
| CPRT-02 | Vacant owner storage ends or is cleaned after move | No event to retire or default owner to manufacture |
| CPRT-03 | Captured event has multiple retained causes and prior executed unwind | Execute supplied remaining retirement once; no earlier cleanup replay |
| CPRT-04 | Captured handler completes normally while owner survives | Handler ends inputs only; no duplicate retirement |
| CPRT-05 | Complete unused captured value is ordinarily discarded without live borrows | Checked nonthrowing/nonsuspending retirement, not original failure propagation |
| CPRT-06 | Handler completes without capturing or rethrowing its event | Ordinary handling retires that still-owned event |
| CPRT-07 | Source handler remains lexical after its captured owner retires | Capture/bare rethrow still reject; no resurrection |
| CPRT-08 | Pending task/guard obligation remains in payload or earlier cause | Reject unsupported retirement or enclosing exit |
| CPRT-09 | Actual admitted protocol settlement removes remaining obligation | Permit exactly-once synchronous retirement |
| CPRT-10 | Discard/replacement/cleanup is expected to insert wait/cancel/supervision | No such implicit operation or failure suppression |
| CPRT-11 | Zero-argument Capture is treated as effect/resource-free | Require actual preparation/publication/transfer/cleanup effect bounds |
| CPRT-12 | Enclosing NoThrow body internally handles a preparation failure | Bound only escaping failures; complete paths still need checked evidence |
| CPRT-13 | Capture/throw/cleanup escapes before versus after async establishment | Synchronous before; failed completion after, irrespective of suspension |
| CPRT-14 | Non-async factory returns successful Task/CapturedException data | Factory remains synchronous; owner result is not failed completion |
| CPRT-15 | Producer creation versus sync or established-async advance/close | Preserve selected source/operation failure channels |
| CPRT-16 | Producer yields owner but retains a borrow consumer retirement could invalidate | Reject invalid handoff/suspension; no automatic lifetime or thread grant |
| CPRT-17 | Fail-fast/process termination or unadmitted hardware fault is presented as captured event | No catchable capture or cleanup promise from this contract |
| CPRT-18 | Actual async/task/producer resumptions, cleanup affinity and termination | Document-only; supplied phase/state flags are not runtime execution |

## Retained contracts and independent admission

| Case | Scenario | Required outcome |
|---|---|---|
| CPAD-01 | Exact Core/context/event/owner/phase/effect/dependency programs available | Admit the affected use under its retained contracts |
| CPAD-02 | Any required creation, transfer, retirement, payload/cause or lifetime layer missing | Decline before affected operation/source transfer |
| CPAD-03 | Replacement uses same-spelled owner/member from another Core module | Reject changed selected identity |
| CPAD-04 | Replacement loses lexical source or handler transfer state | Reject changed context/ownership promise |
| CPAD-05 | Equal ABI implementation substitutes fresh payload throw for saved event | Reject changed provenance/responsibility |
| CPAD-06 | Replacement copies owner or drops source/destination vacancy obligations | Reject incompatible ownership |
| CPAD-07 | Replacement permits an input to escape its original extent | Reject changed retained borrow contract |
| CPAD-08 | Replacement changes synchronous versus failed-completion phase | Reject changed failure channel |
| CPAD-09 | Equal-layout replacement broadens required creation/cleanup effects | Reject violated retained effect bound |
| CPAD-10 | Replacement omits cause, native domain, code or backing dependency | Reject incomplete closure |
| CPAD-11 | Retirement can throw/suspend or lacks complete every-exit support | Reject incompatible retirement program |
| CPAD-12 | Unrelated valid declaration/debug record is added | Do not universally invalidate clients |
| CPAD-13 | Source/private-body/debug name or runtimePolicy claims to replace required records | Missing semantic support still declines |
| CPAD-14 | Trusted selective reader touches complete capture contract but not unrelated Body | Preserve selective access; no mandatory unrelated scan |
| CPAD-15 | Native packet mapper claims public lexical Capture outside a source handler | Require separate checked event adoption; no selected public factory |
| CPAD-16 | Generic/alias use retains exact Core role versus only Exception constraint | Admit normalized exact role; constraint alone does not establish captured propagation |
| CPAD-17 | Independent physical producer/reader executes owner/context/handler/cause codecs | Document-only; this logical unit assigns no wire/opcode/Feature/ABI |
