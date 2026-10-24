# Standard runtime failure categories

This chapter owns `core-runtime-failures/1` and
`failed-initialization-access/1`. It selects the following Core exception
classes
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

<a id="1-exact-core-identities-and-construction"></a>
## 1. Exact Core identities and creation

| Exact public Core identity | Selected failed operation |
|---|---|
| Terrum.PostconditionException | A runtime ensure predicate normally completes false |
| Terrum.IndexOutOfRangeException | An admitted standard checked index is outside its valid extent |
| Terrum.OutOfMemoryException | Allocation exhaustion under an admitted recoverable-publication contract |
| Terrum.InitializationCycleException | Cyclic demand for completion of an unfinished static group or lazy unit |
| Terrum.InitializationFailedException | Waiting or later access requires completion of an already failed unit |
| Terrum.UnreachableException | Execution of an admitted Body `unreachable` instruction or explicit throw of this exact Core value payload |

The first five identities are distinct public nongeneric managed classes
implementing their exact self-typed
[`IException<TSelf>` contract](../language/local-failures.md#1-the-common-exception-contract).
The common contract supplies durable `Message`, static `DefaultMessage`,
creation paths, authored `InnerException`, runtime-owned `ParentException`
and `IFormattable`. These standard classes add no category-specific index,
length, allocator or stack-trace fields. The selected factory contract does
not require a particular constructor signature or physical class layout.
Aliases preserve identity; same-spelled provider types do not substitute.

`Terrum.UnreachableException` is the narrow value-payload exception selected
by the instruction design: a public nongeneric zero-data `Copy` struct with
a public zero-parameter constructor and no authored checks/effects. It is not
a class or an `IException<TSelf>` implementation. A runtime failure event
contains that typed payload plus its origin/causal context; those event facts
are not fields in the struct. Exact `catch (UnreachableException)` binds the
payload by value, bare catch handles its event, and the common
`IException<_>` class view does not match it. No general struct-exception or
payload-copy catch model is implied.

On a selected checked failure, invoke the exact class's common creation path
once. The resulting exception object is the fresh responsible propagation
event at the failed semantic operation. Provenance, causes and dependencies
belong to that object/event. Active or consuming captured propagation preserves
the same object and event; copying a separately permitted handle does not
duplicate event identity or responsibility. The common class/factory contract
does not promise failure-free or allocation-free
preparation, a particular layout, `Pure`, `ConstEval` or thread capability.

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

OutOfMemoryException is the selected exception only when an allocation
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
class or common creation path alone or silently change a selected recoverable operation
to terminal handling. Stack exhaustion, loader/evaluator resource budgets and
unsupported runtime admission remain separate categories.
No allocator API, automatic retry, mandatory emergency heap or suppressed
allocation failure is selected.

## 5. Static and lazy initialization failure

Each existing loaded closed-type group and separate lazy field has its own
initialization state. The default protocol has these logical transitions:

The runtime owns lifecycle coordination and enforcement for these units.
Artifacts provide typed group/field identities and initializer bodies; the
runtime may store state in a side table or artifact-provided cells. This does
not prescribe a physical state layout or operation spelling.

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
or a reusable exception object. Original event responsibility follows the
invoking flow, including the original exception object/event and any captured
owner lifetime.

A waiting or later demand creates a fresh InitializationFailedException
at its own access. It does not rethrow, copy, borrow or implicitly capture the
original event, and does not attach that event as an automatically shared
cause. its own actual factory and enclosing causal context still apply.
Different failed accesses have different events even when their exception
messages are equal. Their creation does not reevaluate the initializer.

Failed unpublished units do not automatically retry. A failed lazy field
after completed group publication fails only that lazy unit. If its failure
escapes an enclosing group initializer, the group fails with that actual
event as well. Different generic/load identities remain independent.
Publication/waiting supplies only the memory guarantees already required by
the admitted sharing contract; it grants no later mutation authority.

An explicit unsafe request may reset only its exact failed group or lazy
field, and only with exclusive recovery authority, completed partial-state
cleanup and dependencies that remain valid. Ordinary access never triggers a
retry, and the request does not reset sibling units or the module. Catching a
fresh failure event does not reset the unit. No separate failure-suppression
or mark-complete operation is selected; a future unsafe transition to
`Complete` requires a demonstrated need and its own validity, cleanup and
publication contract. The exact-unit request is one unsafe call to a
registered runtime-owned member, not a dedicated Body operation; its operand
is a closed tagged exact-unit reference distinguishing an eager group from a
lazy field and resolving to the exact loaded-unit identity. Quiescent unload
rules remain unchanged. The callable identity/signature and physical
tagged-reference schema, shared captured-failure family and persistent
original-event cache remain unassigned.

## 6. Executed `unreachable`

When a Body `unreachable` instruction executes, it creates a fresh ordinary
failure event with exact Core payload `Terrum.UnreachableException`. It
dispatches handlers and performs required cleanup under the ordinary
exception-unwinding contract; it is not `failfast`, undefined behavior or a
callable-owned `RequireException` fallback. Its origin is the instruction
and semantic step location; optional SourceMap data supplies source
coordinates without defining the event identity.

The payload is a zero-data Copy struct with a total zero-parameter constructor.
Creating the payload does not allocate or run authored failure-prone
construction. Event preparation/transport still follows its admitted runtime
contract. If that preparation actually fails, propagate that real failure;
do not fabricate an `UnreachableException` event. Copying the payload value
does not duplicate event identity. CapturedException transfers the complete
event, and explicitly throwing the payload creates a new event at that throw
site.

## 7. Phases and independent admission

All mappings retain the failed source site, complete predicate/index/
allocation/unit domain, factory identity, state, policy, effects,
cleanup/cause responsibility and execution dependencies.
Before async establishment the channel is synchronous; established async
failure uses failed completion even inline. Producer creation and admitted
advance/close keep their ordinary channels. An outlined helper does not
become another semantic owner.

Static proof, invalid source, unsupported interchange and required-evaluator
exhaustion are diagnostics/admission outcomes, not these runtime exceptions.
Actual admitted constant failure prevents a required result; this catalog
does not grant constant programs or failure codecs.
No Feature ID, Attr tag, payload ABI, opcode, exception region or static
state layout is assigned. Independent source-free use still needs complete
declaration/value/operation/handler/cause/init records and runtime support.

[Conformance cases](../conformance/runtime-standard-failures.md) cover the
logical mappings, fresh versus original events, result/cleanup preservation,
initialization state and supported-publication boundaries.
