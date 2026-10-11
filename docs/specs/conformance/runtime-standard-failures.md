# Standard runtime failure conformance cases

These cases apply to [standard categories](../runtime/standard-failures.md)
with admitted construction, propagation, cleanup and initialization.
Finite event/state traces are not a runtime scheduler, allocator, source
compiler or physical handler implementation.

| Case | Situation | Required outcome |
|---|---|---|
| SRTF-01 | Exact five selected Core payload identities | Distinct public nongeneric zero-data Exception/Copy structs |
| SRTF-02 | Changed visibility/storage/constructor/check/effect shape | Reject, not a same-name replacement |
| SRTF-03 | Aliases and nominal catch subjects | Alias preserves identity; other sibling payloads do not match |
| SRTF-04 | Runtime ensure normally returns false | Construct PostconditionException once at the originating clause |
| SRTF-05 | Runtime ensure normally returns true | No failure construction/event |
| SRTF-06 | Predicate itself fails | Keep its actual event; no postcondition payload |
| SRTF-07 | False ensure follows return/finally/local/parameter cleanup | Preserve the saved result and completed order; do not replay cleanup |
| SRTF-08 | False ensure follows live explicit output publication/effects | No successful completion, but no vacancy/rollback of the actual live outputs |
| SRTF-09 | Terminal false-check policy versus actual predicate failure | Terminal check promises no local event; actual predicate failure is not reinterpreted |
| SRTF-10 | Static ensure cannot be proved | Compiler diagnostic, not runtime payload |
| SRTF-11 | Checked contiguous index is negative, at length or much wider than machine index | IndexOutOfRangeException |
| SRTF-12 | Invalid place formation precedes a compound update | No element read/getter/RHS/store/address access |
| SRTF-13 | Receiver/index staging effects occur before rejection | Evaluate once; retain completed effects |
| SRTF-14 | Invalid original count is narrowed/converted before checking | Reject changed category or apparently valid index |
| SRTF-15 | A user indexer or operand fails under its declared contract | Preserve that failure; no name-based standard remapping |
| SRTF-16 | Index is within its actual admitted extent | Ordinary successful access |
| SRTF-17 | Allocation exhausts under admitted recoverable publication | Fresh OutOfMemoryException; no successful allocation |
| SRTF-18 | Recoverable allocation lacks required event/resource support | Decline before affected execution |
| SRTF-19 | Physical exhaustion occurs under an explicit unrecoverable/terminal host contract | That outcome, not a fabricated catchable event |
| SRTF-20 | Allocation fails after partial acquisitions | Reverse actual partial cleanup; retain prior effects |
| SRTF-21 | Zero-data OOM payload is used to infer a heap or free transport | No such guarantee |
| SRTF-22 | Group's ordered initialization starts | One running unit with actual acquired-state authority |
| SRTF-23 | A permitted independent caller demands a running unit | Wait; do not rerun or expose partial state |
| SRTF-24 | Same-flow demand cycles through an unfinished unit | Fresh InitializationCycleException at the cyclic access |
| SRTF-25 | Cross-flow unfinished completion dependencies form a cycle | Fail cyclic demand, not indefinite waiting |
| SRTF-26 | A non-Copy initializer payload escapes | Initializing flow receives the same actual event without payload copy/wrapper |
| SRTF-27 | Failed unit outlives initial event handling | Retain failed state, not shared event ownership |
| SRTF-28 | Waiting and later callers access that failed unit | Separate fresh InitializationFailedException events with no implicit original cause |
| SRTF-29 | Repeated failed access | No automatic initializer retry |
| SRTF-30 | Initializer catches/settles a cycle failure and then validly completes | Unit may publish complete state |
| SRTF-31 | Lazy unit fails after its group completed | Group stays complete; lazy unit fails |
| SRTF-32 | Lazy failure escapes during group initialization | Group fails with that actual escaping event |
| SRTF-33 | Same declaration has different generic/load identities | Independent initialization units/state |
| SRTF-34 | Partial cleanup replaces initializer failure | Deliver replacement with original cause; later access does not share either event |
| SRTF-35 | Preparation for a failed-unit access itself fails | Keep actual preparation failure; no fabricated InitializationFailedException |
| SRTF-36 | One actual event escapes through several initializing units | Same responsible event; failed markers do not duplicate it |
| SRTF-37 | Copy catch, borrowed catch and consuming capture/rethrow | Ordinary payload copy versus original-event responsibility remains distinct |
| SRTF-38 | Synchronous, establishment, async and producer phases | Preserve actual selected channel and source owner |
| SRTF-39 | Any required construction/domain/state/policy/event/cleanup layer is missing | Decline before affected use |
| SRTF-40 | Actual generated cleanup, effects and allocation-failure paths | Document-only: finite traces do not prove execution |
| SRTF-41 | Actual multithreaded initialization/wait/publication and cycle detection | Document-only: logical dependency graphs are not a scheduler implementation |
| SRTF-42 | Actual payload/handler/cause/static-state source-free codecs | Document-only: no physical encoding follows from logical categories |
| SRTF-43 | All allocator/host exhaustion and reserve strategies | Document-only: no universal recoverable-OOM implementation claim |
| SRTF-44 | Actual source/effect/constant checking and required failure programs | Document-only: supplied eligibility is not compiler/interpreter conformance |
