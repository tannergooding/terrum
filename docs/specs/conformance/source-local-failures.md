# Local exception and catch conformance

These cases apply to [local exception objects and typed catches](../language/local-failures.md),
the exact self-typed interface/witness contract and capability-compositional
exception routing. They do not claim compiler/runtime support or allocate a
physical exception codec. Each fixture supplies the stated complete
declarations and runtime contracts; actual evidence limits remain explicit.

## Exception class and common contract

| ID | Fixture | Required outcome |
|---|---|---|
| EXRG-01 | Class binds `IException<this TException>` to its exact implementing type | Accept the self-typed common contract under ordinary interface-witness rules |
| EXRG-02 | Struct, enum, union or static class is used as an exception type | Reject; exceptions are class-only managed objects |
| EXRG-03 | Same-named `IException`, attribute or member has another resolved identity | It does not satisfy the registered common contract |
| EXRG-04 | Class has `Message`, `DefaultMessage` or factories with matching names but no witness | Reject; shape/name does not supply the interface contract |
| EXRG-05 | Common view `IException<_>` is used for a constructed exception object | Preserve object identity and common instance witness without boxing |
| EXRG-06 | `IException<void>` is substituted for `IException<_>` | Reject; void is not an existential self-type projection |
| EXRG-07 | Self-dependent static factory is requested through `IException<_>` | Reject; existential view exposes common instance members, not hidden-self factories |
| EXRG-08 | Concrete exception test narrows a common view through `via<T>` | Select only the exact concrete witness and complete type |
| EXRG-09 | Common `Message` is null or `DefaultMessage` is not the selected durable Text | Reject the common contract; `Message` is non-null |
| EXRG-10 | Nullable Text factory input is null or empty | Null selects `DefaultMessage`; empty text remains a valid explicit message |
| EXRG-11 | Typed `[TextTarget]` factory receives eager interpolated content | Materialize durable Text; do not retain generated content |
| EXRG-12 | Authored `InnerException` is compared with runtime `ParentException` | Keep user cause and runtime propagation parent distinct |
| EXRG-13 | Runtime attaches the pending exception as its own parent | Reject self-parenting; preserve the original exception object identity |
| EXRG-14 | Common default formatter is selected | Forward `Message.TryFormat(writer, format)` without adding type/stack/causal text |
| EXRG-15 | Stack-trace field, CLR `ToString`, boxing or virtual dispatch is inferred | No such common exception guarantee |

## Complete subjects, representation and constraints

| ID | Fixture | Required outcome |
|---|---|---|
| EXTY-01 | Transparent alias resolves to an exception class implementing the exact self contract | Preserve target definition and witness identity |
| EXTY-02 | Alias expansion cycles | Diagnose; do not invent an exception identity |
| EXTY-03 | Unrelated wrapper contains an exception field and has equal layout | It is not itself an exception object |
| EXTY-04 | Exception object retains backing, owner or resource dependencies | Enforce those ordinary dependencies; common eligibility does not waive them |
| EXTY-05 | Generic T requires `IException<T>` and is instantiated with a conforming class | Check the selected complete self/witness contract |
| EXTY-06 | T lacks the exact self witness or the witness is malformed | Diagnose; body/name/layout inference cannot repair it |
| EXTY-07 | Distinct nominal modules, generic arguments or representations share bits | Keep exception identities distinct |
| EXTY-08 | `IException<_>` hides the self type inside `List<Task<_>>` | Each value has its own existential; do not unify hidden arguments |
| EXTY-09 | Exception object is retained after catch | Preserve ordinary dependency/backing limits; a root does not extend unrelated loans |

## Fresh throws and responsibility

| ID | Fixture | Required outcome |
|---|---|---|
| EXTH-01 | Throw operand has observable evaluation and is a complete exception object | Evaluate once; no normal continuation |
| EXTH-02 | A fresh exception object is thrown outside a handler | Establish its origin at the throw operation |
| EXTH-03 | `throw failure;` throws the same object active in the current handler | Preserve its original origin and parent chain |
| EXTH-04 | Handler throws a distinct exception object | Make it primary at the new throw site; attach the pending event as ParentException |
| EXTH-05 | A named class handle denotes the same exception object | Preserve object/event identity; copying a handle does not create a new event |
| EXTH-06 | Operand evaluation or exception factory fails | Propagate that actual failure; fabricate no outer/default/null exception |
| EXTH-07 | Operand is vacant, partial, or not an IException<TSelf> object | Reject the throw |
| EXTH-08 | Source uses bare `throw;` | Reject; same-object propagation uses `throw expression` or catch fallthrough |
| EXTH-09 | Exception factory failure cannot be represented as an exception object | Fail fast; do not fabricate a fallback event |
| EXTH-10 | Exception eligibility is treated as Copy, Pure, Blittable or thread permission | Reject inferred unrelated capability |
| EXTH-11 | Exception object carries unsettled task/guard/resource obligations | Enforce those obligations; catch/throw does not silently settle them |
| EXTH-12 | No handler is found by `main` | Abort fail-fast without implicit frame/owner/output cleanup |
| EXTH-13 | Event materialization is elided despite propagation, parent capture or diagnostic need | Preserve required event identity/origin; elision is not a source guarantee |
| EXTH-14 | Actual object allocation, parent retention, cleanup and resource failure | Requires runtime/state evidence beyond supplied finite obligations |

## Explicit catch bindings

| ID | Fixture | Required outcome |
|---|---|---|
| EXCB-01 | Concrete exception class matches a typed catch | Bind the matching managed object; do not borrow/copy its event responsibility |
| EXCB-02 | `IException<_>` matches a constructed exception | Bind a common existential view preserving object identity |
| EXCB-03 | Concrete or existential named catch binding lacks Copy | Accept; a catch binding does not copy the exception object |
| EXCB-04 | Concrete or existential catch uses `_` | Test the exact type/contract without declaring a binding |
| EXCB-05 | Same-named non-IException interface is used as a catch subject | Reject unless it independently has the selected complete witness/type relation |
| EXCB-06 | `IException<void>` is used as the common catch view | Reject; hidden self type is not void |
| EXCB-07 | `catch (ref<E> failure)` attempts to borrow a typed payload | Reject; typed catches bind exception objects, not payload references |
| EXCB-08 | Concrete nominal type or complete arguments differ | No conversion, adapter, box or name-based match |
| EXCB-09 | Common view is narrowed through a concrete type test | Select only the concrete type's checked witness |
| EXCB-10 | A match has an effectful filter | Initialize the object/view binding before its once-only filter |
| EXCB-11 | Filter type does not match | Execute no filter or binding-dependent code |
| EXCB-12 | Catch binds `Message`, `InnerException` or `ParentException` | Preserve common property nullability/access; authored cause and runtime parent remain distinct |
| EXCB-13 | Catch attempts to consume or transfer event responsibility through its binding | Reject; event responsibility is not transferred by binding |
| EXCB-14 | Catch uses writable, pinned, pointer or initialization-reference input syntax | Reject unsupported catch binding form |
| EXCB-15 | Actual object rooting, witness selection and existential representation | Requires compiler/runtime evidence, not finite binding facts |

## Selection, recovery and propagation

| ID | Fixture | Required outcome |
|---|---|---|
| EXSEL-01 | Multiple typed handlers or filters exist in one region | Test in source order; select first matching clause |
| EXSEL-02 | First matching filter is false and next sibling matches | Continue selection in order; keep filter effects, do not replay |
| EXSEL-03 | Catch body falls through normally | Propagate the same pending event by default |
| EXSEL-04 | Catch exits explicitly with return, break or continue | Swallow its pending event and traverse crossed finally regions |
| EXSEL-05 | Nested loop transfer does not leave the active catch | Do not swallow the pending event |
| EXSEL-06 | Labeled break exits a bounded if/switch/try construct from catch | Permit structured exit; swallow event and run crossed finally |
| EXSEL-07 | `continue label` targets a non-loop construct | Reject |
| EXSEL-08 | Filter or handler raises a distinct failure | Make new failure primary and link pending event as ParentException |
| EXSEL-09 | Handler explicitly throws the same active exception object | Preserve its origin and parent chain |
| EXSEL-10 | Failure occurs in a filter | Replace pending event; do not treat as false or restart sibling selection |
| EXSEL-11 | `try/catch`, `try/finally` or combined handling lacks enclosing unsafe | Reject; exception constructs require explicit unsafe authorization |
| EXSEL-12 | Source uses bare `throw;` inside or outside a handler | Reject; there is no empty throw production |
| EXSEL-13 | No handler is found while dispatch searches outward by main | Fail fast without implicit frame/owner/output cleanup |
| EXSEL-14 | Code continues after a catch using possibly affected general state | Treat state as indeterminate and continuation as unsafe; no compiler recovery proof |
| EXSEL-15 | An applicable finally is crossed by structured control or propagation | Run it under the selected region contract; its failure becomes primary with pending event as parent |
| EXSEL-16 | Actual nested typed/filter propagation and exception-region routing | Requires executable source/runtime evidence for all entered/exited regions |

## Retention, requirement failure and foreign mapping

| ID | Fixture | Required outcome |
|---|---|---|
| EXCP-01 | A common exception view is retained after handler exit without capture | Reject escape of handler responsibility; view alone is not a captured owner |
| EXCP-02 | Exception object backing or a cause depends on an exited frame/guard | Reject invalid dependency despite managed rooting |
| EXCP-03 | Wrapper or same-named method claims preserved propagation | Require the exact registered CapturedException protocol |
| EXCP-04 | Captured event has an admitted consuming transfer contract | Consume original responsibility; do not duplicate the event |
| EXCP-05 | Repeatable captured propagation lacks checked sharing/thread facts | Reject unsupported repeat/crossing; marker is insufficient |
| EXCP-06 | Requirement selector names an E without the common checked factory | Decline missing factory/catalog; no default or false-success substitute |
| EXCP-07 | Predicate evaluation fails instead of returning false | Preserve actual predicate failure, not selected requirement failure |
| EXCP-08 | Foreign packet is mapped to an IException<_> object | Check mapping before local tests and retain domain/cause dependencies |
| EXCP-09 | Raw packet, unadmitted foreign domain or hardware/fail-fast condition is raised | No automatic object eligibility/catchability or cleanup guarantee |
| EXCP-10 | Actual captured object/cause roots, allocator and image retirement | Requires runtime/protocol evidence beyond finite responsible holders |
| EXCP-11 | Independent replacement retains an earlier frame/guard/generation-dependent cause | Check the complete cause closure; replacement independence cannot hide invalid sources |
| EXCP-12 | Cause still has a live input borrow or unsettled protocol responsibility | Preflight retirement; do not partly release another cause and then abandon this one |
| EXCP-13 | A cause owner is reattached, duplicated or used to create an owning cycle | Reject invalid responsibility graph rather than duplicate release |

## Publication and independent admission

| ID | Fixture | Required outcome |
|---|---|---|
| EXAD-01 | Independent consumer retains an `IException<TSelf>` implementation/witness | Check exact interface identity, self type and complete class identity |
| EXAD-02 | Source/debug names replace required eligibility or witness records | Decline missing semantic support; optional names are insufficient |
| EXAD-03 | Replacement removes the retained IException implementation/witness | Incompatible affected use |
| EXAD-04 | Replacement changes complete exception type, self argument or object representation | Reject changed relied-on subject/witness contract |
| EXAD-05 | Replacement changes the common exception members or propagation/capture obligations | Reject changed relied-on contract |
| EXAD-06 | Catch elimination relied on positive or negative interface-witness facts | Retain those facts and recheck replacement |
| EXAD-07 | A new IException implementation is added without changing retained client facts | Apply ordinary compatibility; not universally breaking or automatically valid |
| EXAD-08 | A selected body is accessed by a selective reader | Keep selective access and required retained semantics; no preliminary all-Body scan |
| EXAD-09 | Missing exception/object/handler/cause codec is treated as runtimePolicy support | Decline before affected source-free use |
| EXAD-10 | An exception class or interface spelling is assigned implicit policy | No implementation or feature admission follows from source names |
| EXAD-11 | Prior contained native bridge and ordinary direct calls are retained | Preserve original supported contracts; new local marker supplies no shared unwind ABI |
| EXAD-12 | Complete independent serialized handler/capture and source execution | Requires real producers/readers/runtime; logical unit allocates none of those codecs |
