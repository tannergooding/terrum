# Local failure types and typed catch bindings

This chapter owns logical `local-failure-eligibility/1` and
`explicit-catch-bindings/1`: nominal exception opt-in, payload and catch-subject
eligibility, ordinary reference/value bindings and their relationship to
propagation responsibility. [Source productions](../grammar-spec.md#33-statements-and-local-declarations)
provide the syntax. [Normal and exceptional exits](../language-contracts.md#33-normal-and-exceptional-exits)
own incremental cleanup-before-selection and causal replacement.

These contracts do not select a universal Exception base class, mandatory
boxing, payload layout, stack-trace object or public capture API. A complete
compiler/runtime failure catalog and source-independent payload/handler/cause
codecs remain separate requirements.
[Runtime requirement construction/defaults](requirement-failures.md) have
their own selected source/Core contract.

## 1. Registered Exception capability

The resolved Core Exception semantic attribute is no-argument, nonrepeatable
and primary-contract-only. Its short source spelling is `[Exception]` under
ordinary attribute lookup. A same-named user attribute, suffix, member or
private implementation cannot acquire its meaning.

| Target | Meaning |
|---|---|
| Nominal struct, nonstatic class, enum or union declaration | Explicitly opts its admitted complete forms into use as local exception payloads |
| Interface declaration | Declares a nominal catch category; explicitly implementing that complete interface is also exception opt-in |
| Static class, alias, generic formal, field, callable, parameter or secondary partial declaration | Not an applicable annotation target |

An interface inherits the category contract of its marked base instances
through the ordinary finite, acyclic interface graph. A nominal type's explicit
checked witness to a category, including an inherited category, is opt-in.
Matching members, extension lookup, import order or another type's adapter
cannot supply that witness. An adapter may opt in for its own identity.
The exact closed interface arguments and witness origin remain significant.

Repeated paths to the same category identity deduplicate under ordinary
witness rules. Conflicting claims reject; a later import cannot choose a
winner. Repeating the annotation rejects, including across partial
placements. One direct type annotation plus a valid category witness is not
a repeated annotation: they establish the same eligibility by compatible
declared routes.

The ordinary property constraint `where T : [Exception]` requires this
resolved eligibility for the selected complete T. It neither transforms T
nor infers a type argument. The annotation does not accept a predicate,
argument, following capability-specific `where` clause or default failure
constructor. Type-header and interface constraints remain ordinary formation
constraints; only the separately specified thread-capability schemas own
their following conditional clauses.

**Rationale:** this is a checked type capability like Copy, not an empty
mandatory marker interface. It supports generic constraints and nominal
enum/union payloads without adding a conformance-list production to those
declarations. Category interfaces can express an actual authored behavior
contract; no universal category or field/member set is required.

## 2. Eligibility is not storage or lifetime permission

A fresh throw requires a completely initialized, inhabited, concrete payload
form with Exception eligibility. Retain its normalized declaration,
arguments, representation and qualifications. Eligibility alone does not
provide Copy, Blittable, ManagedStorable, Sized, Pure, NoThrow, cleanup,
thread transfer/sharing, allocation or a target representation.

Transparent aliases preserve the normalized target's eligibility. Equal bits,
Transparent layout, a stored eligible field or a wrapper name do not make an
unannotated wrapper eligible. Representation normalization may preserve an
existing fact only for the resulting exact admitted form; it cannot invent
another opt-in or erase an owning/backing obligation.

The mandatory scalar identities, `void`, raw references/pointers and optional
wrappers are not nominal annotation targets. An annotated nominal enum or
zero-data aggregate may be an exception under its ordinary complete-value
contract; a zero-data exception is not void. An optional E must first be
established present as E, not thrown as an invented null/default exception.
An explicit nominal wrapper may carry a descriptor or owner only with its
own opt-in and valid complete dependencies.

A bare interface is a behavior identity, not a storable payload. Category
membership does not manufacture an interface value, allocate a box or extract
an owner hidden behind a view. A separately admitted owning/erased
representation needs its own complete eligibility, storage and transport
contract; throwing a borrowed view cannot steal its underlying payload.

Generic code may use a proven Exception fact, but still needs the ordinary
complete-value, initialization, transfer and lifetime obligations of its
operation. An unconstrained formal, malformed witness or currently plausible
layout is insufficient. An unsupported representation is declined, not
substituted with a universal object.

The complete payload and its dependencies must survive every admitted
propagation path until handling, transfer or retirement. A reference into an
exited frame is invalid even if its bytes remain or a root keeps the frame's
allocation reachable. Current guard/lease, owner, source generation and
cleanup/code lifetime remain independent obligations. A statically proved
local handling boundary may retain still-live sources; an unproved filter,
guessed handler or invisible extension of an exited scope cannot establish it.
Adding the annotation does not make an otherwise invalid escape valid.
Retaining an earlier event as a cause retains all of its dependencies as well;
an independent replacement payload cannot hide an invalid earlier source.

## 3. Fresh throw and propagation responsibility

`throw expression;` evaluates the operand once and stages its complete value
under ordinary copying or transfer rules. A named unique operand requires
`move`; a fresh owning result may transfer directly. The propagation extent
becomes responsible for that payload before the source holder can retire.
The transferred source is vacant, with no transactional restoration after
later failure.

Operand evaluation can itself fail. That actual failure propagates; no value
or outer throw event is fabricated from the failed operand. Partial
construction retains ordinary acquired-state cleanup. Required representation,
responsibility and resource support must be established under the selected
runtime contract, not assumed from the annotation.

A fresh throw establishes its own logical origin even if its operand is a
copy made by a catch or a handle to the same underlying object. The runtime
keeps propagation provenance separately from ordinary payload fields.
Payload Copy does not copy a pending propagation event, preserve its original
origin automatically or make a unique event repeatably throwable.

The statement has no normal continuation. Its construction, copy/transfer,
cleanup and failure effects still participate in published bounds.
An internally handled failure need not escape a NoThrow body; Exception
eligibility alone promises no such handling or purity.

## 4. Catch subjects and binding modes

Resolve and normalize the written catch type independently of the current
failure. A concrete E subject requires Exception eligibility. A category I
subject requires the marked or inherited category contract. Ordinary
accessibility, constraints and declaration-space rules apply.
After normalization, one outer readonly `ref<E>` selects a borrowed E input;
a non-reference form selects a value or test-only input. An alias of a
reference preserves that mode. Nested reference constructors do not request
additional hidden dereferencing or borrowing, and aliases cannot hide an
otherwise forbidden write, pin or pointer form.

For a concrete subject, match the exact complete payload form after transparent
alias normalization. Different nominal wrappers, generic arguments,
representations or stored inner qualifications do not match. No numeric,
user-defined, adapter, boxing or reference-covariance conversion participates
in a catch type test. A category tests the pending payload's exact declared
closed witness; equal signatures or representation do not substitute.

| Source catch input | Binding and requirement |
|---|---|
| `catch (ref<E> failure)` | A readonly descriptor borrowing the complete active E payload; E need not be Copy |
| `catch (E failure)` | An ordinary complete E value copy; E must have its checked Copy contract |
| `catch (E _)` | Tests E without reading/copying a payload or declaring a local; no Copy requirement |
| `catch (I _)` for a category interface | Tests its exact nominal witness without manufacturing interface storage |
| Bare `catch` | Handles the selected runtime's contract-catchable local failures without a payload variable |

Borrowing uses ordinary `ref<E>` semantics. The name denotes a descriptor,
not an implicitly dereferenced E; use `failure->Member` or `(*failure).Member`.
Ending that descriptor does not destroy the payload. Its immediate slot
access is readonly. Inner authority actually carried by E remains unchanged:
`ref<write C>` does not upgrade the slot, and `ref<C>` does not match a
stored `write C` through reference covariance.

`write ref<E>`, pinned reference, pointer, initialization or mutable-binding
catch inputs are not admitted. They cannot replace the runtime-owned
payload, acquire a pin or introduce an initialization destination.
The ordinary written Type and Identifier-or-underscore grammar introduces
no inferred, implicit-reference or consuming catch mode.

Bare interface notation supplies no storage for a named value binding or a
`ref<I>` referent. Those bindings reject even when the concrete payload is
Copy. Use a concrete borrowed subject, a category test with `_`, or a
separately supported complete representation; a catch does not invent one.
The reference form must itself be valid even with `_`.

A named binding is initialized only after its type match and before its
filter executes. Its scope is that candidate's filter and body, not sibling
catches. Its own place is not rebindable. A value copy has its ordinary
cleanup and dependencies on all exits, including filter false/failure.
Copying a class handle or view is not a deep snapshot of mutable backing.
Neither mode transfers the propagation owner.

For example, with admitted runtime support and an ordinary Inspect operation:

```terrum
[Exception]
public unique struct ParseFailure
{
    public int32 Offset;

    public ParseFailure(int32 offset)
    {
        Offset = offset;
    }
}

unsafe
{
    try
    {
        throw new ParseFailure(int32(7));
    }
    catch (ref<ParseFailure> failure)
    {
        Inspect(failure->Offset);
        throw;
    }
}
```

This borrows the unique payload without copying or moving it. Replacing the
reference input with `ParseFailure failure` diagnoses the missing Copy
contract; using `ParseFailure _` only tests its type.

## 5. Selection, retirement and bare rethrow

Apply the existing incremental unwind rule: complete exited inner cleanup
before testing a still-live candidate scope's catches in source order.
A type mismatch forms no binding and executes no filter. On a match, form
the admitted input, then evaluate its Boolean filter once.

A false filter ends that candidate's binding before testing its next sibling.
Its copy cleanup, writes and other effects remain observable; there is no
rollback or postponed commitment. A filter failure is an escaping failure
from that recovery scope, not false or a restart of sibling catches.
Its ordinary lexical enclosing handlers and causal-replacement rules apply.
Completed cleanup is never replayed. The candidate's own try does not protect
its filter or handler body.

On normal handler completion, end handler locals, copies and borrowed inputs
under their ordinary scope rules, then discharge any propagation responsibility
still held by that handler. The runtime must not retire the payload while one of those
valid borrows still uses it. Any pending protocol/task or other obligation
inside the payload must be settled or transferred under its own contract;
catching is not permission to silently abandon it.
Other exceptional departures likewise retain or discharge that responsibility
under their actual propagation/cause contract, after the input's live uses end.

`throw;` is valid only in the active source handler context, including its
ordinary nested lexical scopes. A separately declared/invoked lambda or
helper does not gain that context merely because it was declared or called
there. A nested handler rethrows its own active failure.

Bare rethrow requests continued propagation of that same event, preserving
original provenance and ownership without payload Copy or a new origin.
Handler-scope cleanup, including ending the catch borrow, precedes transfer
out of the handler and further candidate selection. This permits rethrow
from a reference catch without moving through its readonly descriptor or
keeping that borrow alive in an exited scope.
If handler cleanup replaces the requested rethrow, ordinary causal replacement
propagates that new failure while retaining the earlier event; rethrow does
not make its cleanup failure-free.
Actual payload mutations through already-permitted aliases remain actual
state changes; provenance preservation is not a payload snapshot.

[Lexical Capture](captured-exceptions.md) transfers this responsibility to the
exact affine Core owner without ending valid catch inputs. After successful
publication, this handler activation cannot Capture or bare-rethrow it again.
Normal completion ends its inputs but does not retire the transferred event;
later failure cannot duplicate an event retained by another responsible owner.

## 6. Retained propagation, requirements and foreign mapping

Retaining provenance for propagation after a handler requires an explicitly
checked captured-failure protocol. It must retain or transfer the actual event,
complete payload, causes and dependencies with valid responsible holders.
A catch value copy, exception annotation, stored descriptor, same object
address or similarly named Capture method does not supply that protocol.
An escaping reference into the handler/event is invalid without a valid
retained source relationship.

[Core CapturedException](captured-exceptions.md) is the selected opaque affine
owner. Its exact direct no-argument Capture is valid only in the active source
handler body; consuming `throw move captured;` preserves the existing event.
It is not an ordinary Exception payload, copied catch input or native packet.
Existing catch borrows retain their original scope and complete dependencies.
Repeatable/shared propagation and cross-thread permission are not selected.
Neither Copy on E nor Exception eligibility implies either capability or
preserved provenance for a fresh `throw E`. Complete physical owner/handler/
cause programs remain unassigned.

[Runtime requirement selectors](requirement-failures.md) name an eligible E
and ordinary checked `new E()` with zero supplied arguments. Explicit E wins
over its semantic callable's RequireException recipe or Core fallback.
Eligibility supplies no constructor, default value or implicit conversion.
Only a complete constructed E becomes the check's fresh event; operand or
construction failure remains its own actual failure. Missing support is not
a normal false result, another guessed failure or fabricated default.

The [contained foreign bridge](../runtime/foreign-failure-bridge.md) must
explicitly map its domain-owned packet to a supported eligible local payload
or captured event, or select a declared normal-status translation.
A raw packet is not automatically an Exception value. The mapper runs before
local type tests, not during arbitrary catch lookup, and preserves its actual
affine state, causes, origin/runtime and helper lifetime.

Compiler/runtime failure catalogs must distinguish catchable checked failures
from fail-fast, process termination and unadmitted hardware faults.
An Exception annotation cannot turn one of those terminal conditions into a
recoverable local payload or promise cleanup after termination.
[The initial scalar catalog](scalar-failures.md) supplies exact Core
OverflowException, DivideByZeroException and ArgumentOutOfRangeException
payloads and source-domain mappings. Their Copy zero-data values do not copy
the propagation event or replace this chapter's selection/ownership rules.

## 7. Publication, compatibility and admission

Publish the resolved Exception schema, primary annotation or exact category
witness route, complete payload/subject identities and generic constraints.
Retain catch modes, Copy facts actually used, source order/scopes, filter code,
active event/provenance, per-site ownership/dependency states and all required
cleanup and runtime support. Positive or negative witness facts used to remove
a catch remain replacement dependencies, not guesses from today's body.

Removing eligibility, changing a matched complete type or category witness,
requiring Copy where borrowing was promised, or changing propagation/capture
obligations is not justified by equal machine signatures. Added contracts
still undergo the [ordinary retained replacement checks](../representation/binding-and-replacement.md);
they do not universally invalidate unrelated clients or automatically make
every replacement compatible.

Trusted selective readers remain selective, but missing retained eligibility,
witness, payload, handler, cause or cleanup support declines before affected
use. A semantic attribute name, optional debug record, raw runtimePolicy,
reserved opcode or private body cannot replace complete required records.
This logical revision assigns no Feature ID, attribute codec, TypeSpec shape,
opcode, target selector or exception-region layout.

[Conformance cases](../conformance/source-local-failures.md) distinguish
supplied finite identity/ownership/selection facts from actual source,
runtime, root/lifetime and physical-codec implementation evidence.
