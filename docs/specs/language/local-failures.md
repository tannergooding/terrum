# Local exception objects and typed catches

This chapter owns the source/runtime contract for exception objects, common
exception views, catch selection and propagation responsibility.
[Source productions](../grammar-spec.md#33-statements-and-local-declarations)
provide the syntax. [Normal and exceptional exits](../language-contracts.md#33-normal-and-exceptional-exits)
own fail-fast behavior, explicit cleanup and structured transfer.

Ordinary authored exceptions are class-only managed objects implementing
the self-typed `IException<TSelf>` contract. For these exception objects,
the object itself is the propagation event; there is no separate runtime
event wrapper or payload-copy catch model. The exact Core
`UnreachableException` is a narrow built-in Copy-value failure payload,
specified below; it does not generalize exception eligibility to user
structs.
Source recovery is an explicit unsafe escape hatch, and catch continuation
does not prove that general program state has been restored.
[Runtime requirement construction/defaults](requirement-failures.md) have
their own selected source/Core contract.

## 1. The common exception contract

The Core nominal interface is `IException<this TException>`. Implementing
exception types are classes and bind `TException` to their exact implementing
class. This replaces `[Exception]` eligibility and payload-only nominal
category interfaces. A same-named attribute, method or unrelated interface
does not acquire the registered identity.

The common contract provides:

| Member/contract | Required behavior |
|---|---|
| `Text Message` | Readonly and non-null durable message text |
| `static Text DefaultMessage` | Type-owned default used by the no-argument creation path |
| Common creation paths | Create from the default, from nullable `Text` (`null` selects the default), or from typed eager `[TextTarget]` content; typed content is materialized as durable `Text` and is not retained |
| `IException<_>? InnerException` | Readonly nullable authored cause, independent of runtime propagation history |
| `IException<_>? ParentException` | Readonly runtime-attached propagation parent; never supplied by an authored factory |
| `IFormattable` | A common formatter witness; the default body forwards to `Message.TryFormat(writer, format)` |

Exact Core member spellings for source-visible cause and runtime parent
storage remain subject to their owning library/runtime integration. Formatting
does not promise a type name, stack trace, or causal-chain rendering.
Creation and message materialization use ordinary checked-failure rules; if
no exception object can be materialized to represent a failure, the runtime
fails fast rather than fabricating a null or default exception.

The common view `IException<_>` existentially hides the self type of an
already-constructed exception object. It exposes common instance members,
including the formatting witness, but not self-dependent static factories or
the hidden concrete type. This view preserves the same managed object
identity; it does not box, introduce class inheritance or use virtual
dispatch. A type test may narrow to a concrete exception and select its
checked witness through `via<T>`. This use of `_` is distinct from `void`
and from `_` as a direct-call inference hole. Its exception-specific
operation surface does not establish generic existential semantics; the
complete-type contract owns the general `G<_>` view. The Task-specific
operations on `Task<_>` are owned by
[task responsibility](task-responsibility.md#existential-task-view).

### Built-in UnreachableException payload

`Terrum.UnreachableException` is the exact public, nongeneric, zero-data
Core `Copy` struct selected for execution of the Body `unreachable`
instruction. It has a public zero-parameter constructor with no authored
checks or effects. It is not an implementation of the class-only
`IException<TSelf>` contract.

Executing `unreachable`, or explicitly throwing this exact registered
payload, creates a fresh ordinary failure event whose typed payload is
`UnreachableException`. The runtime-owned event context carries the
instruction/throw origin and causal replacement relationship; these are not
fields in the zero-data payload. Copying the payload does not copy or
preserve event identity.

An exact `catch (UnreachableException value)` matches the built-in payload
and binds its Copy value. `catch (IException<_>)` remains the existential
view of ordinary exception objects and does not match this payload; bare
catch still matches its local failure event. A `CapturedException` may
consume and retain the complete event, including the payload and its event
context. This is the only selected value-payload exception and does not
adopt a general payload-copy catch model.

## 2. Object identity, storage and lifetime

A thrown value is a completely initialized, live exception class object. The
runtime treats that exact object as the propagation event and retains its
minimal origin instruction address plus its runtime-owned parent link.
Catch bindings refer to the object, not a typed payload borrowed through
`ref<E>` or copied under `Copy`. A common `IException<_>` view is another
checked view of the same object, not an allocated wrapper.

Exception eligibility does not provide `Copy`, `Blittable`, `ManagedStorable`,
`Sized`, purity, nonthrowing construction, thread transfer/sharing, native
representation or an allocator choice. An exception class and its object
dependencies must satisfy the ordinary complete type, initialization,
ownership, backing, rooting and lifetime contracts of each operation.
Managed reachability keeps the object alive only under its ordinary backing
contract; it does not extend unrelated borrows, preserve external resources,
or pin a movable address.

`InnerException` is authored cause data. `ParentException` is runtime
propagation history for ordinary exception-object events: when a new object
replaces a pending exception, the runtime attaches the original object as
parent, preserving identity rather than copying a payload or synthesizing an
aggregate. A root event has no parent, and an object cannot parent itself.
For `UnreachableException` value-payload events, causal replacement is held
in runtime event context; the zero-data payload has no parent field. The
physical/runtime representation remains implementation work, not a
source-visible factory parameter.

## 3. Fresh throw and propagation responsibility

`throw expression;` evaluates its operand once and raises the selected live
exception object. The expression must produce a class implementing the
registered `IException<TSelf>` contract, the exact Core `UnreachableException`
payload, or an explicitly checked `CapturedException` owner. Operand
evaluation or object construction may itself fail; that actual failure
propagates under the ordinary failure rules.
No event, null value or default exception is fabricated to represent it.

Throwing an exception object outside a handler establishes its origin at the
throw operation. In an active handler, throwing the same active object
preserves its original origin and `ParentException` chain. Throwing a
distinct exception object makes it primary at the new throw site and attaches
the pending exception object as its runtime `ParentException`. It cannot
self-parent. Throwing an `UnreachableException` value creates a fresh event at
the throw operation and preserves the pending event as runtime-owned causal
context; copying that value never revives the old event. Throwing a captured
owner consumes and propagates the same existing event rather than constructing
another exception.

The statement has no normal continuation. Exception objects do not imply
automatic cleanup of outputs, owners or other frame state. Those obligations
are discharged only by explicitly authored/selected contracts. Event
materialization may not be elided where needed for propagation, parent
capture or diagnostics; optimization does not alter the source guarantee.

## 4. Catch subjects and bindings

Resolve and normalize the written catch type independently of the current
failure. A concrete exception class matches its exact complete type after
transparent alias normalization. `IException<_>` matches any live object
implementing the registered common contract and binds a per-value existential
view; it does not expose or infer the hidden self type. No numeric,
user-defined, adapter, boxing or reference-covariance conversion participates
in catch selection. A concrete type test may narrow the common view and
select the concrete interface witness through `via<T>`.

| Source catch input | Binding and requirement |
|---|---|
| `catch (ParseFailure failure)` | Binds the matching managed exception object; no payload copy or `Copy` requirement |
| `catch (IException<_> failure)` | Binds the same object through the common existential view |
| `catch (UnreachableException failure)` | Binds the exact built-in failure payload by value; does not bind a class object or common `IException<_>` view |
| `catch (ParseFailure _)` | Tests the concrete exception type without declaring a local |
| `catch (IException<_> _)` | Tests the common exception contract without declaring a local |
| Bare `catch` | Matches contract-catchable local failures without a source-visible exception binding |

A named exception binding is initialized after its type match and before
its filter executes. Its scope is that candidate's filter and body, not
sibling catches. It is an ordinary managed object handle or common view,
not a borrowed payload descriptor or an owner transfer. A class handle does
not imply a deep snapshot of mutable state. The same object remains the
propagation event unless a new failure replaces it.

For example, with an admitted `ParseFailure` class implementing
`IException<ParseFailure>` and ordinary `Inspect` support:

```terrum
public class ParseFailure : IException<ParseFailure>
{
    public int32 Offset { get; }

    public ParseFailure(int32 offset)
    {
        Offset = offset;
    }

    // Common IException members are omitted here.
}

unsafe
{
    try
    {
        throw new ParseFailure(int32(7));
    }
    catch (ref<ParseFailure> failure)
    {
        Inspect(failure.Offset);
        throw failure;
    }
}
```

The catch binds the exception object directly. `throw failure;` of this same
active object preserves the original origin and parent chain. The example is
illustrative of the approved source contract, not a claim about current
compiler support or a complete implementation of the common interface.

## 5. Selection, recovery and propagation

Exception handling is fail-fast by default. `try/catch`, `try/finally` and
combined handling require an enclosing explicit `unsafe` context. Catch
filters execute once in source order after a type match. A false filter
continues to the next candidate. A filter failure becomes primary and links
the pending exception through `ParentException`; it is not false and does not
restart completed work.

Normal catch fallthrough propagates the same pending event. Intentional
recovery is explicit: any non-failure control transfer that exits the active
catch—including `return`, `break` or `continue`, labeled or unlabeled—swallows
that event and traverses each crossed `finally` in structured order. A
transfer contained within a nested loop that does not leave the catch does
not swallow it. A labeled `break` may target a bounded structured
`if/else`, `switch` or `try` construct; `continue label` remains loop-only.
Bare `throw;` is not admitted. Explicit `throw failure;` of the same active
exception object preserves its origin and parent chain.

A distinct failure raised by a handler or crossed `finally` becomes primary
and retains the pending exception object as `ParentException`. The authored
`InnerException` remains separate. Filters, catch bodies and `finally`
blocks are ordinary source regions with their own effects; they are not
normal method epilogues or implicit cleanup. If dispatch finds no handler by
`main`, execution aborts fail-fast without normal cleanup. Thread, process or
platform abort may also prevent an explicit `finally` from completing.

The state visible after a catch is generally indeterminate. No affected-state
subset is tracked or certified by the compiler; definite-assignment
diagnostics and explicit reinitialization do not prove semantic recovery.
Continuing to execute code after a catch remains the author's unsafe
responsibility. Exceptions do not promise automatic owner/output cleanup or
rollback. Explicitly selected cleanup runs only under its own contract.

[Lexical Capture](captured-exceptions.md) transfers responsibility for the
exact active event to the Core affine owner without ending valid catch
bindings. After successful publication, this handler activation cannot
Capture or explicitly throw the transferred event again. Later failure
cannot duplicate an event retained by another responsible owner.

## 6. Retained propagation, requirements and foreign mapping

Retaining provenance beyond a handler requires an explicitly checked
captured-event protocol. It retains or transfers the actual exception object,
its causes and dependencies through valid responsible holders. A common
interface view, another handle to the object or a similarly named Capture
method does not supply that protocol. An escaping reference into a handler
or an exited frame remains invalid without a valid retained source
relationship.

[Core CapturedException](captured-exceptions.md) is the selected opaque affine
owner. Its exact direct no-argument Capture is valid only in the active source
handler body; consuming `throw move captured;` preserves the existing event.
It is not an ordinary `IException<_>` object or native packet. Existing catch
bindings retain their original scope and object dependencies. Repeatable/
shared propagation and cross-thread permission are not selected. Implementing
`IException<TSelf>` does not imply either capability or preserve provenance
for a distinct exception object. Complete physical owner/handler/cause
programs remain unassigned.

[Runtime requirement selectors](requirement-failures.md) name a concrete
`IException<E>` implementation and its selected common creation path. An
explicit E wins over its semantic callable's RequireException recipe or Core
fallback. The interface contract supplies the factory path but not Copy,
default-value or implicit-conversion behavior. Only a completely constructed
exception object becomes the check's fresh event; predicate or factory
failure remains its own actual failure. Missing support is not a normal false
result, another guessed failure or fabricated default.

The [contained foreign bridge](../runtime/foreign-failure-bridge.md) must
explicitly map its domain-owned packet to a supported `IException<_>` object
or captured event, or select a declared normal-status translation. A raw
packet is not automatically an exception object. The mapper runs before
local type tests, not during arbitrary catch lookup, and preserves its actual
affine state, causes, origin/runtime and helper lifetime.

### Native exception normalization

Under an admitted target/runtime `NativeExceptionUnwind` capability, a native
exception from an imported call may unwind inward according to that target's
ABI and be normalized before source catch matching. A native call convention
alone does not enable this capability. Native exception propagation is not a
caller-propagated language effect. The runtime maps recognized events to
their exact Terrum exception classes and otherwise materializes the Core
`NativeException` fallback; every materialized object follows the same
identity, catch, parent-replacement, `finally` and propagation rules as a
local exception.

`NativeException` is one opaque public exception class implementing
`IException<NativeException>`. Its stable Terrum-owned snapshot exposes
read-only target-profile identity (`TargetProfileId` and
`TargetProfileRuleRevision`), event-domain text (`EventDomain`), native-width
event code (`EventCode: nuint`) and `Message`. The fallback message is
`A native exception occurred.`; any more descriptive message is
Terrum-owned, and foreign exception text is never copied. The snapshot does
not retain the native exception handle or context.
`GetNativeContext()` is caller-unsafe and returns opaque `ptr<void>?`; a null
result means the target/event cannot safely expose context. Non-null context
availability, lifetime and copy requirements follow the selected target ABI.
Unsafe access does not extend context lifetime or waive known provenance and
access constraints.

On Windows x64, ordinary SEH events normalize to `AccessViolationException`
for `EXCEPTION_ACCESS_VIOLATION`, `StackOverflowException` for
`EXCEPTION_STACK_OVERFLOW` after sufficient unwind recovery,
`OverflowException` for `EXCEPTION_INT_OVERFLOW`, and
`DivideByZeroException` for integer divide-by-zero. Other admitted events
become `NativeException` without decoding producer-language payloads.
Process termination, forced unwinds, `longjmp` and cancellation are excluded.
An uncaught native exception fails fast. Terrum exceptions do not unwind
outward through native frames; native exports/callbacks contain reverse
boundary propagation. The separately selected contained foreign-failure
bridge is not this unwind path.

Compiler/runtime failure catalogs must distinguish catchable checked failures
from fail-fast, process termination and unadmitted hardware faults.
Implementing `IException<TSelf>` cannot turn one of those terminal conditions
into a recoverable local event or promise cleanup after termination.
[The initial scalar catalog](scalar-failures.md) supplies exact Core
OverflowException, DivideByZeroException and ArgumentOutOfRangeException
identities and source-domain mappings. Those are class exception objects;
their source-visible fields and runtime event identity remain distinct.

## 7. Publication, compatibility and admission

Publish the exact `IException<TSelf>` interface/witness identity, complete
exception type and subject identities, ordered handler/filter contracts,
active object/provenance relationship and required runtime support. Catch
binding no longer depends on payload Copy. Positive or negative witness facts
used to remove a catch remain replacement dependencies, not guesses from
today's body.

Removing the interface implementation, changing a matched complete type or
witness, or changing propagation/capture obligations is not justified by
equal machine signatures. Added contracts
still undergo the [ordinary retained replacement checks](../representation/binding-and-replacement.md);
they do not universally invalidate unrelated clients or automatically make
every replacement compatible.

Trusted selective readers remain selective, but missing retained interface/
witness, exception type, handler, cause or required runtime support declines
before affected use. A source name, optional debug record, raw runtimePolicy,
reserved opcode or private body cannot replace complete required records.
This logical revision assigns no Feature ID, TypeSpec shape, opcode, target
selector or exception-region layout.

[Conformance cases](../conformance/source-local-failures.md) distinguish
supplied finite identity/ownership/selection facts from actual source,
runtime, root/lifetime and physical-codec implementation evidence.
