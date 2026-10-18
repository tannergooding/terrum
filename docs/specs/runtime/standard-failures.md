# Standard runtime failure categories

This chapter owns `core-runtime-failures/1` and
`failed-initialization-access/1`. It selects the following Core payloads
and mappings without changing [local event ownership](../language/local-failures.md),
[captured propagation](../language/captured-exceptions.md),
[call phases](../language-contracts.md#3-contracts-bracket-semantic-body-completion)
or the [static initialization order](../grammar-spec.md#static-initialization-and-load-scoped-storage).

These are operation-specific categories, not a universal Exception base,
automatic wrappers for arbitrary failures or host-fault recovery.
The [scalar catalog](../language/scalar-failures.md), ordinary
[requirements](../language/requirement-failures.md) and
[invalid bounds](../language/numeric-selection.md#4-reusable-invalidboundsexception)
retain their separate mappings.

## 1. Exact Core identities and construction

| Exact public Core identity | Selected failed operation |
|---|---|
| Terrum.PostconditionException | A runtime ensure predicate normally completes false |
| Terrum.IndexOutOfRangeException | An admitted standard checked index is outside its valid extent |
| Terrum.OutOfMemoryException | Allocation exhaustion under an admitted recoverable-publication contract |
| Terrum.InitializationCycleException | Cyclic demand for completion of an unfinished static group or lazy unit |
| Terrum.InitializationFailedException | Waiting or later access requires completion of an already failed unit |

Each is a nongeneric, zero-data nominal struct with the resolved Exception
capability, ordinary Copy and one public zero-parameter instance constructor.
That constructor establishes its complete value without authored checks,
initialization code or other authored effects. No message, index, length,
allocator, type-name, original-payload or stack-trace fields are required.
The identities are distinct; aliases preserve identity but same-spelled
provider types and equal zero bits do not substitute.

On a selected checked failure, construct the exact payload once and establish
a fresh responsible propagation event at the failed semantic operation.
Ordinary provenance, causes and dependencies belong to that event.
Copy duplicates only the payload; an active or consuming captured rethrow
preserves its original event. Zero data does not promise failure-free or
allocation-free event preparation, a physical layout, generic constructor
evidence, Pure, ConstEval, NoThrow or thread capability.

Known missing required support declines before affected use.
Actual preparation/transport failure follows its own admitted contract,
without a fabricated selected event, default result or lost holder.
An admitted terminal policy need not prepare an unobservable local event and
does not promise local recovery or cleanup after termination.

## 2. Runtime postconditions

A runtime declaration `ensure predicate` normally completing false selects
exact Core PostconditionException. The semantic owner is the declaring
callable or originating inherited clause, not its physical checking location
or ambient caller. RequireException and an explicit require selector do not
override this fixed postcondition category. No ensure<E> syntax is introduced.

Evaluate each reached predicate once in declared order, after the existing
body/finally/local and untransferred owned-parameter cleanup, over its saved
result and legally surviving state. An actual predicate/helper/cleanup
failure propagates unchanged; it is not a false predicate or a fabricated
PostconditionException. The failed path does not check later clauses.

False ensure publishes no successful callable completion. It does not replay
completed cleanup, reevaluate a return expression or undo transfers.
Under ordinary output contracts, it does not vacate already initialized
explicit outputs. Outputs still private or reserved under a selected protocol
follow that protocol's exceptional-cleanup and publication rules instead.
Saved results and phase-local owners retire under their actual ownership
state on defined unwinding.
The exact output/async/producer contract still selects the failure channel.
Fail-fast and unsafe recovery retain their independent rules.
Static ensure remains a proof request whose failure is a compiler diagnostic,
not runtime construction.

## 3. Checked index access

For an already admitted standard checked array/span/view access, an index
outside the operation's valid extent selects IndexOutOfRangeException.
For a single contiguous extent, validity is numeric `0 <= index < length`.
Use the original complete index and actual captured access extent before any
narrow machine index conversion or address calculation.

Negative or too-wide indices are index failures, not overflow from a
premature unsigned/narrow conversion, masked successful indices or memory
accesses performed before checking. Receiver, index and place formation
retain their ordinary once-only source timing. An actual receiver/index/
extent-evaluation failure remains that failure.
On the invalid path publish no element reference/value, perform no element
read/write and preserve completed staging effects. A compound update does
not proceed to its getter/RHS/store after failed place formation.

This does not define new array, multidimensional, slice, pointer or collection
APIs. Their already admitted shapes determine the relevant extent.
User-defined indexers retain their declared contracts; a member's index-like
name does not map its failure to this category. Hardware access violations,
stale backing and unadmitted unsafe accesses are not checked index failures.

## 4. Recoverable allocation exhaustion

OutOfMemoryException is the selected payload only when an allocation
operation's actual profile admits recoverable exhaustion and complete event
publication. Such support includes the real preparation, transport, roots,
cleanup and surviving runtime/code resources needed for the failed path.
It may use a separately established reserve or another valid strategy;
neither a heap nor one allocation strategy is mandatory.

On that exhaustion path publish no successful allocation/value and clean
actual partial acquisitions under the operation's contract. Preserve prior
effects and existing live outputs; do not fabricate empty storage, default
values, Boolean success, or a retry with weaker alignment/ownership.
If exhaustion occurs while preparing another failure, the actual preparation
failure retains its event/cause rules; no unprepared outer event is invented.

This is not a promise that every physical OOM is catchable. A terminal or
unrecoverable host failure remains that profile's explicit outcome.
An implementation must not claim recoverable-publication support from a
zero-data payload alone or silently change a selected recoverable operation
to terminal handling. Stack exhaustion, loader/evaluator resource budgets and
unsupported runtime admission remain separate categories.
No allocator API, automatic retry, mandatory emergency heap or suppressed
allocation failure is selected.

## 5. Static and lazy initialization failure

Each existing loaded closed-type group and separate lazy field has its own
initialization state. The default protocol has these logical transitions:

| Unit state and access | Required outcome |
|---|---|
| Not started; admitted completion demand wins initialization | One initializing flow enters; retain acquired partial state |
| Running; permitted independent competing demand | Wait for the unit's final completion outcome |
| Running; completion demand creates a cycle among unfinished units | Fresh InitializationCycleException at that cyclic demand |
| Running; successful completion and publication | Complete; ordinary valid accesses observe its published state |
| Running; actual failure escapes after required partial-state cleanup | Failed and unpublished; initializing flow receives that actual escaping event |
| Failed; waiting or later completion demand | Fresh InitializationFailedException for that demand |
| Complete; later valid access | No initialization replay |

An unfinished unit is not automatically a cycle. A completion-dependency
cycle, including a cross-flow cycle, cannot be repaired by indefinite waiting,
exposing partial storage or inventing a default. Internal accesses already
authorized to initialized unpublished fields are not ordinary completion
demands. A cycle event caught and settled inside an initializer does not by
itself fail a unit that subsequently completes all its required obligations.

The initializing flow receives the actual event that escapes initialization,
including ordinary causal replacement by faulting partial cleanup. It is
not wrapped in TypeInitializationException or converted merely because it
crosses an initialization gate.
The failed unit retains **failed state**, not shared ownership of that event
or a reusable copy of its payload. Original event responsibility follows the
invoking flow, including its possible non-Copy payload and captured lifetime.

A waiting or later demand constructs a fresh InitializationFailedException
at its own access. It does not rethrow, copy, borrow or implicitly capture the
original event, and does not attach that event as an automatically shared
cause. Its own actual construction and enclosing causal context still apply.
Different failed accesses have different events even with equal zero-data
payloads. Their creation does not reevaluate the initializer.

Failed unpublished units do not automatically retry. A failed lazy field
after completed group publication fails only that lazy unit. If its failure
escapes an enclosing group initializer, the group fails with that actual
event as well. Different generic/load identities remain independent.
Publication/waiting supplies only the memory guarantees already required by
the admitted sharing contract; it grants no later mutation authority.

Existing exclusive unsafe recovery/suppression and quiescent unload rules
remain unchanged. Recovery needs actual completed partial cleanup and valid
dependencies; suppression grants no initialized-state facts. No concrete
recovery API, shared captured-failure family or persistent original-event
cache is introduced.

## 6. Phases and independent admission

All mappings retain the failed source site, complete predicate/index/
allocation/unit domain, constructor identity, state, policy, effects,
cleanup/cause responsibility and execution dependencies.
Before async establishment the channel is synchronous; established async
failure uses failed completion even inline. Producer creation and admitted
advance/close keep their ordinary channels. An outlined helper does not
become another semantic owner.

Static proof, invalid source, unsupported interchange and required-evaluator
exhaustion are diagnostics/admission outcomes, not these runtime payloads.
Actual admitted constant failure prevents a required result; this catalog
does not grant constant programs or failure codecs.
No Feature ID, Attr tag, payload ABI, opcode, exception region or static
state layout is assigned. Independent source-free use still needs complete
declaration/value/operation/handler/cause/init records and runtime support.

[Conformance cases](../conformance/runtime-standard-failures.md) cover the
logical mappings, fresh versus original events, result/cleanup preservation,
initialization state and supported-publication boundaries.
