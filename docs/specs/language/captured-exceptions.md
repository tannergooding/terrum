# Captured exception ownership and lexical capture

This chapter owns logical `captured-exception-owner/1` and
`lexical-exception-capture/1`: the exact Core owner, its compiler-known
creation context, transfer of an existing event and consuming propagation.
[Local failures](local-failures.md) owns exception objects, catch inputs and
selection; [normal and exceptional exits](../language-contracts.md#33-normal-and-exceptional-exits)
owns ordinary unwind and causal replacement. Capture changes responsibility,
not those exit rules.

The captured event may carry an ordinary class exception object or the exact
Core `UnreachableException` value payload. Capture owns the complete event
context and responsibility; it does not turn the Copy payload into an
exception object or duplicate event identity.

This initial contract selects no typed/shared capture family, payload-access
API, public handler token, ambient current-exception query, thread capability
or physical owner/handler/cause codec.

## 1. Exact Core identity and public surface

The selected Core module registers exact public `Terrum.CapturedException`:
a nongeneric opaque unique struct representing one live captured-event owner.
Its storage, event handle and payload representation are not public fields.
One complete value owns the existing event, complete payload, original
propagation provenance, all causes and their full dependencies and outstanding
obligations. An event identifier in an explanatory model is not a public API.

The one creation member is exact public static, nongeneric
`CapturedException Capture()` on that type, with zero formal parameters.
Ordinary source spelling is `CapturedException.Capture()` after lookup and
complete identity normalization. The compiler-known role supplies the
lexically selected handler source; it is not a hidden ordinary argument or
a query for a thread-global exception.

A same-named user type/member, extension, adapter, matching signature or
attribute cannot acquire either role. A transparent type alias preserves
the exact identity. This contract supplies no ordinary constructor, type-call
construction, default/zero/null owner, payload-argument overload, inferred
type argument or public factory for a native packet.
Vacant owner storage is not an empty complete CapturedException.

The owner is not Copy and is not an ordinary exception class or catch
category. It has no implicit conversion to/from its hidden payload, a
descriptor, native packet or other exception representation. No `Blittable`
or `ManagedStorable` commitment, stable layout/size guarantee, pin, heap
allocation or allocator choice follows.
Supported storage and transport retain their own complete contracts.
Opaque internal state is not structurally accessible for partial field moves.

The selected runtime publishes the actual creation, transfer and synchronous
nonthrowing/nonsuspending retirement programs, effects, resource requirements
and dependent payload/cause support. This selection supplies no Pure,
ConstEval or failure-free creation promise. Unsupported required
support declines before affected use; a spelling cannot synthesize a runtime.

## 2. Lexical source context

Capture is a direct invocation of the registered member in an active source
handler **body**, including its ordinary nested lexical blocks and body-owned
finally/defer cleanup. At the operation's execution point that exact handler
activation must still own its handled event. Registering a defer is not
executing Capture or reserving that ownership for later.

Capture is invalid in a catch filter, outside a source handler, or in a
separately declared/invoked function, lambda, local helper or callback merely
declared or called there. Body inlining, interface dispatch, reflection or
execution on the same thread does not confer source context. The
context-dependent member is not an ordinary first-class func/fnptr target;
indirect invocation cannot acquire a lexical event source.
Ordinary type aliases and qualified member lookup may still select the exact
direct invocation.

An entered nested handler selects its **own** event. If that handler already
transferred responsibility, Capture and explicit rethrow of that event reject
there; neither falls back to an enclosing handler. After the nested handler ends, the
enclosing source handler's own retained state applies again.
A nested try body without a newly entered handler does not change that source.

Callable declaration identity and handler activation, not a machine stack
address or worker thread, identify this context. A supported async/producer
resumption retains it and its actual ownership state. A separately called
helper may accept and later consume a CapturedException under ordinary calls;
it cannot invoke Capture on the caller's event.

## 3. Creation, publication and failure state

Capture evaluates the direct invocation once. Validate the selected source,
complete live event, full dependency closure and required support before
publication. Any required fallible preparation belongs to that operation;
it does not copy E, replay the original throw or run E's constructor.

Successful publication is one responsibility transition: the source handler
ceases to own the event exactly when the complete returned owner becomes
responsible. There is no observable interval with zero or two responsible
holders. This is a logical single-flow publication requirement, not an
atomic-memory operation or a concurrency grant.
The same payload and causes remain live with their actual state.

Before that transition, failed preparation leaves ownership with the handler
and publishes no result. Acquired private state has its ordinary cleanup.
The actual failure propagates, not an empty/default owner or silent success.
If an inner recovery scope handles that failure, the original handler can
still Capture its event later. If the failure escapes that handler while it
still owns the original event, ordinary causal replacement retains the
original as a cause. Completed preparation/cleanup effects are not rolled back.

After successful publication, subsequent destination/call/cleanup failure
uses the actual returned/staged/transferred owner's responsibility.
It cannot restore ownership to the source handler. A runtime delivery failure
must preserve that owner or settle it through the admitted failure contract;
it cannot lose the event between holders.

Successful Capture makes another Capture or explicit throw of that source
activation invalid, even if the owner was subsequently moved or retired.
At a flow join, source ownership must hold on every path reaching either
operation; unresolved maybe-transferred state is not permission to reuse it.
A newly entered handler after consuming propagation may Capture the same
event again: each successful capture creates a new affine owner activation,
not a globally once-capturable event.

## 4. Catch bindings and complete dependencies

Capture is valid while the source handler has a live concrete exception
binding or common `IException<_>` view. It transfers responsibility for the
event, not the managed exception object or catch binding; the source handler
cannot recapture or explicitly throw that transferred event. The binding
remains usable under its ordinary handler scope, access and object-root
contract. No payload descriptor or copied event is part of the public owner.

Keeping the captured event alive retains the same object and its causes; it
does not extend unrelated backing, guards, source generations or external
resources referenced by that object. Storing/returning the owner requires
all exception/cause dependencies, roots, code/runtime/allocator dependencies
and protocol obligations to survive that destination and every admitted
path. The implicit result-source relationship retains that actual event
closure; it is not a new general DependsOn/Captures selector spelling or a
lifetime waiver for parameterless APIs.

A managed catch handle/view is not another propagation owner. It cannot
duplicate responsibility, authorize rethrow after Capture, or prevent
conflicting transfer of the affine `CapturedException` holder. Its ordinary
object backing remains valid only under its declared dependency contract.
No owner arena, silent lifetime extension or pin is inserted.

For example, assuming the eligible, independent ParseFailure from
[the local-failure example](local-failures.md#4-catch-subjects-and-bindings)
and admitted Inspect/runtime support:

```terrum
CapturedException RetainFailure()
{
    unsafe
    {
        try
        {
            throw new ParseFailure(int32(7));
        }
        catch (ParseFailure failure)
        {
            Inspect(failure.Offset);
            return CapturedException.Capture();
        }
    }
}

unsafe
{
    CapturedException saved = RetainFailure();
    throw move saved;
}
```

The staged return owner survives handler completion; the catch binding is
limited by its ordinary handler scope. The later throw preserves the original event,
not a copied ParseFailure with a new origin. The example does not authorize
returning an event whose payload depends on the exited RetainFailure frame.

## 5. Consuming captured throw

An operand whose normalized complete type is exact Core CapturedException
uses this consuming route of ordinary `throw expression`, in the existing
statement and throw-expression forms.
A named owner requires `throw move saved;`; a fresh owning result can transfer
directly, including `throw CapturedException.Capture();` in its valid context.
No `throw<preserve>`, owner Throw member or payload Copy is introduced.
Vacant, partial, borrowed or merely same-named operands cannot supply it.
An Exception constraint alone does not establish this distinct Core role.

Evaluate/stage the operand once. Operand failure propagates its actual event;
it fabricates neither a captured value nor an outer event. On successful
transfer, the source becomes vacant and the pending propagation extent owns
the **same** event, payload, origin and existing causes. It does not execute a
new payload throw, restore the source, replay previous cleanup or mutate
shared payload state to manufacture a preserved origin.

Structured exits and applicable `finally` code run before transfer reaches
outer handler selection. This permits direct consuming throw of a just-captured
event from its handler.
While that cleanup runs, staged propagation keeps the event live.
Any handler that still owns a different active event retains it as a cause
when the captured event escapes, under ordinary causal replacement.
The handler that already transferred this same event contributes no second
cause/owner. A later unrelated failure cannot adopt an event still owned by
an independently retained captured holder.

A cleanup failure can replace requested propagation while retaining the
requested event and its dependencies as a cause. Every executed cleanup stays
executed; no self-cause, duplicated owner or lost earlier cause is valid.
After transfer, delivery failure likewise retains pending responsibility
under the admitted failure contract rather than reviving the source.

A wrapper that merely contains a captured owner has no captured-throw role.
If that wrapper is a newly created eligible exception object, throwing it forms a
new outer event with the wrapper's ordinary owned-field obligations; it does
not flatten its hidden captured field or preserve the old top-level origin.

## 6. Handler completion, retirement, phases and effects

Normal handler completion ends its locals and catch bindings in ordinary
order. If it still owns its active event, ordinary handling retires that event.
If Capture transferred it, handler completion does not retire, recapture,
rethrow or add it as a cause merely because lexical context still exists.
Its actual captured/staged/propagating holder remains responsible.
An exceptional exit uses the same ownership distinction.

Moving, returning, storing, replacing, discarding and automatically cleaning
a captured owner obey ordinary unique-value rules. Normal retirement means
the saved event is handled and its remaining payload/cause ownership is
discharged exactly once; it does not repropagate the original exception.
Use its admitted synchronous nonthrowing/nonsuspending retirement program
and actual transitive cleanup/dependency order.
Already completed unwind is not replayed. Vacancy has no event to retire.

Pending task, guard or other obligations inside any payload/cause still need
their own settlement/transfer contract before that retirement is valid.
Capture, error-handling intent, opaque storage and an owner's destruction
cannot silently wait, cancel, suppress an unobserved task failure or abandon
resources. If the complete retirement path is unavailable, the enclosing
exit/store/replace operation is not admitted; explicitly handle or transfer
the outstanding responsibility.

Capture does not change an enclosing call's failure channel.
Pre-establishment async failure is synchronous; escaping Capture, captured
throw or authored cleanup failure in an established operation is failed
completion, even without an await. A supported failed-completion delivery
retains the same transferred event and responsibility.
Producer creation remains synchronous; advance/resume/close follow their
selected synchronous or established-async operation contracts.

Suspension retains the actual owning versus transferred handler state,
captured owners, inputs and all dependencies. Yielding/moving an owner does
not let a suspended producer retain an invalid payload borrow that a consumer
can destroy. Every later resume/close must have a valid cleanup path.
Thread migration or publication still needs explicit complete
sharing/handoff, placement and affinity support; this erased owner supplies
no unconditional ThreadTransferable or ThreadShareable fact.

Effect bounds include actual creation preparation/publication, transfer,
payload/cause retirement, authored cleanup and escaping failures. Zero
arguments and opaque storage do not make Capture pure, constant-evaluable,
allocation-free or failure-free. An internally handled preparation failure
need not escape an enclosing operation, but the complete path must satisfy
its actual effect and evaluation contracts. Proving away a call cannot discard
its ownership transition or effects.
Fail-fast/process termination remains a distinct no-cleanup condition.

## 7. Foreign mapping, retained contracts and admission

The [contained foreign bridge](../runtime/foreign-failure-bridge.md) retains
its own domain-owned packet and paired mapping/raise/release contract.
A packet or native exception_ptr is not a Core CapturedException.
An admitted bridge can establish a local event and use this source Capture
in a local handler. A source-independent mapper instead needs its separately
checked event-adoption/transport contract; it cannot invoke lexical Capture
outside a source handler or manufacture the owner from two packet words.
No additional public adoption API is selected here.

Retain exact selected Core module/type/member identities and signature,
lexical callable/handler activation and responsibility state, actual
payload/cause/provenance closure, input extents, owner/source/destination
states, failure phase, creation/transfer/retirement programs, effects,
resource requirements, roots and full runtime/helper/load dependencies.
Independent consumers need those contracts, not source-name relookup,
private-body inference or optional stack-trace/debug records.

Replacement checks every expectation actually relied on. Equal layout or
machine signatures cannot substitute a fresh payload throw, duplicate owner,
earlier retirement, broader input escape, changed failure channel or missing
cause obligation. Unrelated additions do not universally invalidate clients.
Trusted readers remain selective; missing required support declines before
affected use, not after transferring an event or demanding unrelated Bodies.

This logical selection allocates no Feature ID, Attr tag, intrinsic wire ID,
TypeSpec shape, opcode, handler/cause layout or native export/ABI.
[Conformance cases](../conformance/source-captured-exceptions.md) distinguish
finite supplied identities/state schedules from actual source, root/cleanup,
runtime containment and physical-codec implementation evidence.
