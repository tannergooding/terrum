# Consuming builder completion conformance cases

These cases apply to [builder-completion/1](../language/construction-handlers.md#consuming-builder-completion)
with otherwise admitted target recipes, acquisition, accepts and eager
captures. They specify logical source/contract outcomes, not execution of
a compiler or builder implementation. Acquisition, fragment/spread catalogs
and physical records remain independently required.

## Nominal requirement and selected signature

| Case | Situation | Required result |
|---|---|---|
| BCSIG-01 | Construction retains a checked complete Core IBuilder<TBuilder,TResult> witness and static TryBuild member | Use those exact nominal identities and the established result relationship |
| BCSIG-02 | Builder has a same-named static method but no selected Core witness | No source-completion capability from the name/signature |
| BCSIG-03 | Builder implements a same-spelled user IBuilder | No Core completion role |
| BCSIG-04 | Candidate completion takes ref, implicit ref or write ref builder instead of by value | Reject wrong consuming input mode |
| BCSIG-05 | Candidate completion is an instance mut TryBuild | Explicit reusable API only; not this static requirement |
| BCSIG-06 | Result is returned normally or supplied through a non-init/reference/default position | Reject wrong required output mode/signature |
| BCSIG-07 | Selected TResult differs in complete qualification, representation or nested arguments | Retain exact selected output type and ordinary published target relation; no inverse retargeting |
| BCSIG-08 | Required result relation or one selected completion witness is missing/ambiguous | Reject affected construction; recipe does not choose by return type or method name |
| BCSIG-09 | Ordinary Copy builder implements the role | Source transfers its private local; protocol does not make the type unique or retire unrelated copies |
| BCSIG-10 | Builder cannot be stored/passed/transferred under its complete type contract | Reject unsupported source construction; no boxing or invented transfer |
| BCSIG-11 | Selected implementation cannot satisfy result validity/backing/cleanup obligations | Reject invalid conformance, not a false runtime success-shaped fallback |
| BCSIG-12 | IBuilder completion is present without acquisition or acceptance contracts | Missing operations still reject the affected use |

## Source transfer and result states

| Case | Situation | Required result |
|---|---|---|
| BCEXEC-01 | Successful nonempty construction reaches completion | Transfer builder once, make source local vacant, publish one complete live result |
| BCEXEC-02 | Empty construction completes successfully | Same transfer/publication relation without inventing captures or requiring a payload byte |
| BCEXEC-03 | Selected logical result is void | True initializes the unit output; false leaves it vacant despite equal zero-byte storage |
| BCEXEC-04 | Completion returns false normally | Builder remains consumed, output vacant, plain construction raises ordinary require failure |
| BCEXEC-05 | Explicit caller invokes the consuming protocol and observes false | Keep Boolean outcome and vacant output unless authored caller code requires success |
| BCEXEC-06 | Completion throws before constructing any result | Propagate the original exception; consumed builder cleanup, no restored source/default output |
| BCEXEC-07 | Completion throws/refuses after creating a private partial result | Clean partial result and residual state exactly once; no completed readable output |
| BCEXEC-08 | Later argument/invocation check fails after builder transfer but before body entry | Ordinary staged-value cleanup; no body execution, source revival or second caller drop |
| BCEXEC-09 | Source result destination is already live | Reject invalid init destination before the affected call; no implicit replacement |
| BCEXEC-10 | Normal true is paired with vacant/invalid output, or false with live output | Invalid completion implementation contract, not a permitted result relation |
| BCEXEC-11 | Source attempts completion twice on the same transferred local | Second use lacks a live builder; no retry/reset semantics |
| BCEXEC-12 | A source construction site is evaluated again in its supporting scope | Acquire a new live builder under ordinary reusable-local rules; never revive the consumed value |
| BCEXEC-13 | A capture/accept operation fails before completion | Completion is unreached; current builder/holders clean their actual owned state |
| BCEXEC-14 | Consumer holds ordinary TextTarget content or an external writer | IBuilder completion does not impose single-use or consumption on those values |
| BCEXEC-15 | Complete output is formed, but a post-body check/authored cleanup action faults before normal call completion | Retire the unpublished output under exceptional vacancy; builder stays consumed and the actual failure propagates |

## Ownership, backing and cleanup

| Case | Situation | Required result |
|---|---|---|
| BCOWN-01 | Successful result receives an accepted owning value/storage | Cleanup responsibility transfers to result; builder cleanup does not retire it |
| BCOWN-02 | Successful completion leaves private scratch or residual owning state | Callee cleans residual obligations before normal completion returns |
| BCOWN-03 | Refusal leaves accepted owning values with the supplied builder | Callee cleans those values exactly once; caller's builder local is already vacant |
| BCOWN-04 | Failure leaves some values in partial result and others in builder | Each actual owner cleans its own subset once; no double drop or restored source |
| BCOWN-05 | An owning capture was never accepted/transferred | Its original live holder remains responsible independently of completion |
| BCOWN-06 | The complete published result later ends its own lifetime | Perform result-owned cleanup once, not a repeated builder cleanup |
| BCOWN-07 | Result borrows storage destroyed with the consumed builder | Reject missing surviving backing relationship |
| BCOWN-08 | Result legitimately transfers the builder's backing owner | Preserve root/lease/ownership dependencies with the result, not a stale builder anchor |
| BCOWN-09 | Copied reference/handle capture points to external backing | Preserve that actual backing dependency; consumption is not a snapshot or lifetime extension |
| BCOWN-10 | Source builder has aliases or overlapping init output regions | No implicit NoAlias; all ordinary access, output, validity and dependency obligations must hold |
| BCOWN-11 | Required generated owner cleanup may throw or suspend | Reject unsupported cleanup contract; no silent failure suppression |
| BCOWN-12 | An authored finally/defer action faults during completion unwind | Preserve ordinary causal chaining after exact-once owned cleanup; no transaction |
| BCOWN-13 | Explicit reusable builder is adapted nominally to consuming completion | Adapter publishes result and cleans actual residual state; no post-success reset inferred |
| BCOWN-14 | A reusable same-named API is called directly | Its declared post-completion state applies; no compiler-invented consumption or adapter |

## Independent contracts and evolution

| Case | Situation | Required result |
|---|---|---|
| BCVER-01 | Consumer lacks builder-completion/1 or the exact selected witness/result record | Reject affected use before execution |
| BCVER-02 | Producer replaces by-value completion with a reference-taking/reusable role | Changed consuming contract; not compatible by equal return type or output |
| BCVER-03 | Producer permits false/live output or weakens exceptional cleanup | Incompatible required result/cleanup relation |
| BCVER-04 | Producer changes private scratch implementation while preserving every retained obligation | No new incompatibility solely from the private implementation choice |
| BCVER-05 | Source completion is optimized or its private builder storage is combined | Preserve transfers, effects, failure order, result/backing identity and cleanup; equal final output is insufficient |
| BCVER-06 | IBuilder is treated as Pure, ConstEval, allocator permission or an emitted value codec | No such implication; separately checked contracts remain required |
| BCVER-07 | Completion record is remapped at linking | Preserve exact complete interface/member/result identities and dependency mappings, not names |
| BCVER-08 | Logical completion approval is written into a held/unassigned metadata slot or opcode | No physical carrier/allocation is admitted by this rule set |
