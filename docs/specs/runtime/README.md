# Abstract runtime

The abstract runtime defines execution and storage obligations independently
of a target's representation, a semantic artifact's encoding or current
compiler/runtime support.
The [semantic operation display map](../il-instruction-set.md#operation-identity-encoding-and-presentation)
does not prescribe runtime helpers or merge semantic operations with their
actual runtime support.

| Chapter | Canonical responsibility |
|---|---|
| [Sharing and handoff](sharing-and-handoff.md) | Published thread capabilities, conditional eligibility, actual authority, admitted handoff/publication/exclusion protocols, failure and trust boundaries |
| [Concurrent executions](concurrent-executions.md) | Exact location/generation events, matched communication, coherence, required access order, mixed SC/full boundaries, grounded causality and actual retirement |
| [Memory model](../memory-model.md) | Shared CPU policy, indivisibility and library direction, comparative terminology and independent target/platform boundaries |
| [Lifetimes](../lifetimes.md) | Reference lifetimes, backing, roots, access, loans and value retirement |
| [Foreign failure bridge](foreign-failure-bridge.md) | Explicit paired containment, affine domain-owned failure state, typed mapping/causes, synchronous callback responsibility and retained runtime support |
| [Standard failure categories](standard-failures.md) | Fixed postcondition/index/recoverable-allocation/cycle/failed-unit payloads, source/event/output state, initialization delivery and complete support gates |

Cross-cutting source chapters also own [dependencies](../language/dependencies-and-views.md),
[ownership and cleanup](../language/ownership-and-cleanup.md),
[local failure eligibility and catch responsibility](../language/local-failures.md),
[async execution](../language/async-execution.md),
[task responsibility](../language/task-responsibility.md) and
[producer lifecycles](../language/generated-producers.md).
Their specified abstract obligations are authoritative at those paths;
this index references the same rules rather than copying or redefining them.
The [shared definitions](../language.md#definitions-and-notation) introduce
copying, aliases, references, lifetime, scope and supporting borrow/loan/lease terms.

[Sharing conformance cases](../conformance/source-sharing-and-handoff.md)
distinguish capability eligibility, actual protocol state and retained
authority. [Concurrent execution cases](../conformance/runtime-concurrent-executions.md)
separately constrain actual observations. Passing finite logical cases does
not establish arbitrary source/algorithm proofs, target mappings or physical
codecs.

[Foreign bridge cases](../conformance/target-windows-x64-failure-bridge.md)
also distinguish owned logical failure transport from its independently
compiled native wire and actual Terrum dispatch/root/lifetime obligations.

[Standard failure cases](../conformance/runtime-standard-failures.md) distinguish
actual initializer-event delivery from fresh failed-unit accesses, and selected
recoverable publication from terminal/host exhaustion. No shared event cache
or universal allocator runtime is inferred.
