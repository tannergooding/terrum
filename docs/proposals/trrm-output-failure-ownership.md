# Joint output, failure and ownership interchange

**Status: executable output modes and core checked exceptions are accepted
TRRM 1.0 GA requirements; their joint physical admission, encoding and
conformance remain open.** This proposal does not change the selected
[output contracts](../specs/language-contracts.md#22-declared-outcome-relationships),
[failure and cleanup rules](../specs/language-contracts.md#33-normal-and-exceptional-exits),
[dependencies](../specs/language/dependencies-and-views.md) or
[task responsibility](../specs/language/task-responsibility.md).

## Current boundary

The [bounded executable subset](../implementation.md#executable-trrm-scalar-interchange)
and [private output profiles](../implementation/output-profiles.md) are
implementation evidence, not the complete GA contract. The provisional
output-only profile excludes checked exception handling and richer cleanup;
the former root-free catch-only first-profile candidate excludes outputs,
ownership and loans. Neither proves their required composition.

Private rule numbers, historical opcode catalogs, an optional Attr row or a
runtime-policy flag cannot activate a missing semantic capability. No Feature
ID is registered for this proposal. The existing
[manifest contract](../specs/representation/semantic-manifests.md#2-governing-baseline-and-capability-registration)
requires an exact governing edition, prerequisite closure, representation,
admission rules and independent conformance. Required GA behavior does not
become an optional source feature merely because its encoding is unfinished.

## Joint admission gate

Define one coherent admission contract for the following interacting facts.
Separate encoding components may implement it, but their composition must be
specified and independently consumed together.

| Surface | Required preserved facts |
|---|---|
| Declarations and witnesses | Exact complete types, parameter modes, receiver access, member identity, effects, predicates and published refinements |
| Places and values | Active/vacant/live state, initialization authority, qualified ownership, partial aggregates, roots and actual backing |
| Output relationships | Exact discriminator, formal/destination and value version, permitted normal statuses, publication phase and initialized-null distinction |
| Calls and establishment | Ordered acquisition/transfer, retained sources, synchronous establishment failure, operation identity and no stranded work |
| Failure | Operation-site state, selected checked category, original captured event, payload/cause closure and handler inputs |
| Cleanup | Unpublished state, moved inputs, ordinary finally/defer, nonthrowing/nonsuspending automatic retirement and remaining child obligations |
| Async completion | Captures, reservations where expressly admitted, terminal checks/cleanup, observation and every-exit settlement |
| Static initialization | Exact owning-group state, failure and dependency interaction under the separate [metadata proposal](static-initialization-metadata.md) |
| Binding and replacement | Source-independent validation, exact prerequisites, directed compatible replacement and unchanged destination on rejected admission |

Specify every applicable declaration, value, program, operation, phase,
owner/root/handler and dependency carrier in
[logical records](../specs/representation/logical-records.md) and its physical
encoding. A layer may be explicitly inapplicable; it cannot simply be omitted.
The assigned OutputState and Dependency clauses are not complete codecs for
all these relationships. The
[reference-call proposal](reference-call-metadata.md#1-which-dependency-relations-belong-in-the-first-callable-profile)
remains the owner of its genuinely pending dependency encoding choices.

## Phase and publication obligations

| Phase/outcome | Required boundary |
|---|---|
| Validation or establishment fails before an operation exists | Retire acquired private/partial state; publish no operation, result or initialization output |
| Inputs/owners transferred into an established call | Preserve the actual responsible holder on every exit; do not restore consumed caller values or invent rollback |
| Conditional normal refusal | Preserve the declared vacant outputs and remaining valid caller state; no fabricated default value |
| Conditional normal success | Publish complete values only under the exact declared discriminator/output relationship |
| Non-Boolean normal status | Encode the authored permitted-status/output-state relationship, without reading vacant storage to discover the status |
| Body, postcondition or authored exit fails | No successful result/output publication; clean unpublished owners and preserve earlier external effects |
| Checked failure reaches a handler | Reconstruct its exact operation-site state, event/category, causes and responsible resources before admitted handling |
| Ordinary finally/defer fails | Preserve selected causal chaining and ordering; do not replay earlier cleanup |
| Automatic retirement | Run the eligible complete/partial transitive cleanup schedule without throwing or suspension |
| Async operation completes inline or after suspension | Preserve the same establishment, capture, terminal cleanup and observation contract |
| Completion owner moves or is adapted | Preserve or explicitly discharge every actual payload/output obligation; no task-shaped implicit output lifting |

Discriminator reassignment, output replacement and later publication create
different value versions; an old correlation cannot authorize a read or
cleanup of a reused destination. An initialized optional-null value is live,
not a vacant output. An inspection-only decoder cannot confer executable
support by recognizing the row shapes.

## Reader, linker and runtime obligations

Publish a support matrix distinguishing inspectable declarations from
bindable, transformable and executable programs. State which structural,
type, phase, ownership and dependency facts each consumer validates and
which producer-body facts it explicitly trusts. Producer trust cannot waive
missing codecs, incompatible contracts or unsupported required capabilities.

Before affected use, reject missing/unknown prerequisites, invalid
references, inconsistent operation/output versions, incomplete handler
state, unsupported roots/loans or incompatible replacement. Apply admission
to uncalled bodies and inactive paths as well as reached operations.
Do not infer authorization from persisted bits, infer conformance from
matching members, or reinterpret native faults as Terrum checked failures.

## Required independent acceptance evidence

Use producer/consumer bytes without producer source to exercise conditional
outputs, transferred owners, checked and postcondition failure, original
captured-event delivery, cleanup and imported dependencies in the same
program. Include:

- Complete success, normal refusal, every permitted enum status, checked
  failure and initialized-null, with discriminator/output reuse.
- A formed owning result whose postcondition fails, and a moved input whose
  callee fails after acquisition, with exactly-once responsible retirement.
- Nested handling, faulting ordinary cleanup and nonthrowing automatic child
  cleanup, preserving selected event/cause and field/acquisition ordering.
- Roots, loans and provider lifetimes across imports, inline/suspended async
  completion and explicit owner transfer/settlement.
- Malformed/missing/overlapping state records, unsupported prerequisites,
  incompatible replacement and late invalid bodies, without publishing or
  replacing a destination artifact.

An output-only fail-fast round trip, a root-free catch-only fixture, a private
format revision or a table of logical traces is useful bounded evidence,
not joint GA conformance. Exact wire records, capability revisions, supported
target/runtime profiles and full acceptance implementations remain pending.

## Proposed native boundary composition

**Recommendation: compose a synchronous raw-native slice now, without
pretending that it completes the joint GA codec.** The
[native metadata proposal](trrm-native-interop-metadata.md) supplies concrete
unapproved NativeOutput, NativeParameter and InteropDependency records, exact
candidate capabilities and executable pointer/call shapes. These are
extensions for review, not registrations or optional replacements for the
accepted core output/failure requirements above.

The absence of a current canonical record is not an argument against adding
one. Early TRRM evolution is expected; preserve the settled semantics, propose
the actual carriers and revise standards/gates after consolidation. Keep
rejection only at an actual unsupported/malformed consumer boundary, not as a
decision to leave needed metadata permanently out of scope.

### Three distinct output subjects

| Subject | Recommended carrier and meaning |
|---|---|
| Raw pointer cell or pointee region | Proposed NativeOutput, candidate Contract kind `0x0012`: exact formal/depth, integer-result predicate and validity on each normal status. This does not create a checked init destination or an owner. |
| Checked Terrum initialization destination | Existing OutputState kind `0x0002` where its Boolean applicability is exact, plus the complete admitted output/place/publication codec. No raw-pointer alias or reserved flag replaces that codec. |
| Responsible owning result/output | Complete ordinary ownership/transfer/cleanup and dependency records under the joint gate above. NativeOutput alone cannot transfer responsibility, synthesize an owner or choose its release operation. |

For a pointer-to-pointer output, initializing the pointer cell does not
initialize, pin, type-identify or own the pointee. A live null value is not
vacancy. A caller-selected IID plus an untyped pointer output does not
establish a particular interface; a typed wrapper must fix or validate the
selection in authored code.

### Concrete synchronous phase and correlation model

Recommend deriving the following logical record at each call from its
standard Body and declaration contracts, rather than trusting producer-written
flow/authorization flags:

```text
InvocationOutcome(
    callSite, selectedCallableContract, phase,
    statusValueId, destinationPlaceOrBacking, storageGeneration,
    declaredOutcomeState, responsibleHolder, carriedDependencies)
```

This is a proposed logical verifier fact, **not** a new opaque Blob or
unassigned physical table. For the bounded synchronous raw-native slice, its
physical inputs are the existing Body call/value/place IDs and CFG, the
proposed pointer/address/indirect/vtable instructions, NativeOutput and
InteropDependency clauses, and the exact selected Module/Import contracts.
Status and destination versions are reconstructed at the call and preserved
through real value/field dependencies. They are not durable booleans attached
to a reused local. A producer persisting a more general handler/resume/owner
summary still needs the full joint schema above; it cannot reuse this
derived fact as its missing wire representation.

| Phase | Required transition |
|---|---|
| Prepare | Evaluate/stage arguments once in source order, retaining actual pointer/pin/owner/code backing; prepare only the storage that the selected ABI and modes permit. |
| Invoke | Transfer only the explicitly declared responsibility. A direct import, indirect function pointer and vtable call apply the same complete argument/output/effect contracts. |
| Observe normal status | Associate validity with this invocation's exact status value and destination generation. Read no unspecified output to discover the status or guess success from its bits. |
| Complete wrapper | Establish actual complete output values and dependencies; run required checks and unpublished-state cleanup before safe result/output publication. |
| Refuse/fail | Preserve declared vacancy or live failure outputs exactly. Retire only actual private/partial responsibility, without reading an unspecified foreign output or reviving consumed inputs. |
| Retain/retire | Keep declared captured sources, holders, code/load dependencies and earlier native effects on every applicable exit. No automatic rollback, pin acquisition or refcount operation is implied. |

Any status reassignment, pointer replacement, destination reuse or later
invocation must preserve the actual original subject or establish its own
correlation. Copying a status does not invalidate the original relationship,
and unrelated calls need not erase it while its destination state survives.
A saved successful status cannot authorize a load or cleanup of a new failed
output. An initialized private staging scalar/buffer remains distinct from
the safe wrapper's destination; its existence does not let the compiler
publish a default value on refusal.

NativeParameter's authored no-retention promise applies to all outcomes, not
just the success branch. A declared capture into an existing holder may
survive a later wrapper exception. InteropDependency records the whole
source/destination/phase relation; actual loans, backing and holder lifetime
still constrain the call. Unknown raw provenance is not proof of fresh
backing, and unsafe authorization does not discard known dependencies.

### Failure, cleanup and artifact publication

Raw native status is an ordinary authored value, not a universal checked
exception or HRESULT policy. Native-callable entries must contain faults or
terminate and satisfy their declared checked effects; NoThrow alone is not a
foreign fault barrier. The separately selected
[contained bridge](../specs/runtime/foreign-failure-bridge.md) remains the route
for its explicit failure packet and local re-raise, with its complete joint
ownership/output requirements. No foreign unwinder crosses live Terrum
frames because a method acquired a native metadata row.

A wrapper releases a failed foreign output only when its authored contract
establishes that the value and responsibility actually exist and that release
is permitted on that outcome. An unspecified or unwritten pointer is not a
cleanup target. Conversely, an explicitly live failure owner must not leak
merely because the normal result reports refusal. Neither case warrants
compiler-owned API acquisition or COM cleanup recipes.

For COM calls, initializing an output parameter even when the method fails
prevents an indeterminate value; it does not by itself establish that the
result denotes a live object or transfers a releasable reference. The exact
method contract must establish that failure-state meaning and ownership before
a wrapper reads, publishes or releases the output.

Rejected compilation/linking has a separate publication boundary: validate
and finish producer-owned staging before replacing the destination artifact.
Do not truncate a prior valid file or leave a partial success-shaped image.
Runtime output cleanup and filesystem artifact cleanup are separate
responsibilities; one cannot stand as evidence for the other.

### Review disposition and required evidence

Dropped pointer backing at indirect calls, incomplete nested unsafe traversal
and premature output publication are implementation defects wherever those
facts already exist. Missing physical output/owner/phase carriers remain
metadata gaps. The
[native review partition](trrm-native-interop-metadata.md#review-feedback-defects-versus-contract-gaps)
records that distinction without claiming old findings are current or fixed.
An uninspected later diff is not acceptance evidence.

For this synchronous composition, require source-free producer/consumer
cases with normal success/refusal, initialized-null, nonnegative and explicit
integer statuses, valid versus unspecified failure outputs, repeated
destination/status reuse, pointer-cell versus pointee validity, indirect and
vtable forwarding, retained backing/code origins and rejected artifact
destination preservation. Add actual unique/partial cleanup and checked
failure cases when their joint codecs are admitted, rather than calling
Copy-only native bytes complete ownership support.

No new test result or current-patch approval is asserted here. These proposals
give concrete reviewable choices and acceptance obligations; canonical
registration, implementation correction and final-input qualification are
still separate work.
