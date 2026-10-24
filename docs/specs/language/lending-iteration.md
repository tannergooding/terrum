# Reference-output lending iteration

This chapter owns logical `lending-iteration/1`: separate readonly/writable,
synchronous/asynchronous lending roles, element-place bindings and published
output dependencies. [Value iteration](iteration.md) retains common nominal
selection, traversal ownership and close requirements.
[References](references.md) owns descriptor formation and immediate access;
[lifetimes](../lifetimes.md) owns actual backing, roots, guards and cleanup.
[Async value iteration](async-iteration.md) owns completion-output reservations.
None of these roles is inferred from a reference-valued item, member names
or private implementation behavior.

Lending publishes a reference to an existing element rather than an
independent element value. The loop's element binding aliases that place;
it is not an ordinary local holding a copied reference descriptor. Its
lifetime and advance/close bounds are the declared ones below. See the
shared [alias, reference and loan definitions](../language.md#definitions-and-notation).

## Nominal protocol identities

Core supplies four distinct cursor roles and their corresponding source
factories. The readonly synchronous requirements are:

```terrum
public interface ILendingCursor<T>
{
    public mut bool TryMoveNext(ref<init ref<T>> item);
    public mut void Close();
}

public interface ILendingSequence<T, TCursor>
    where TCursor : ILendingCursor<T>
{
    public TCursor GetEnumerator();
}
```

The writable synchronous requirements are:

```terrum
public interface IWritableLendingCursor<T>
{
    public mut bool TryMoveNext(ref<init write ref<T>> item);
    public mut void Close();
}

public interface IWritableLendingSequence<T, TCursor>
    where TCursor : IWritableLendingCursor<T>
{
    public mut TCursor GetEnumerator();
}
```

The corresponding asynchronous requirements retain synchronous acquisition:

```terrum
public interface IAsyncLendingCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(ref<init ref<T>> item);
    public mut Task<void> CloseAsync();
}

public interface IAsyncLendingSequence<T, TCursor>
    where TCursor : IAsyncLendingCursor<T>
{
    public TCursor GetAsyncEnumerator();
}

public interface IAsyncWritableLendingCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(ref<init write ref<T>> item);
    public mut Task<void> CloseAsync();
}

public interface IAsyncWritableLendingSequence<T, TCursor>
    where TCursor : IAsyncWritableLendingCursor<T>
{
    public mut TCursor GetAsyncEnumerator();
}
```

Each exact resolved Core requirement carries the output/backing relationship
defined below. Async advances additionally inherit the exact completion-phase
reservation/publication relationship. A matching signature, Task<bool>, Current
property or `async` modifier supplies neither relationship.

These interfaces do not inherit value iteration or each other. Writable
descriptor attenuation is an ordinary value operation, not covariance of
`ref<init write ref<T>>` storage into `ref<init ref<T>>`. Implementing a writable
role does not silently provide a readonly witness. An explicit adapter can
publish the other role with checked acquisition, output adaptation and backing.
One underlying traversal implementation may fulfill several exact requirements;
their identities, source selection and caller contracts remain distinct.

T is the exact complete stored element type, including inner access,
ownership and optional qualification. No universal Copy bound applies.
The source must expose a live addressable T place with the selected authority,
and the cursor/output must support their actual storage and access contracts.
An inhabited `void` place has no exposable payload address, so these roles do
not manufacture `ref<void>`; by-value iteration can still publish unit items.
An optional T element may be initialized and absent without being exhaustion.

## Mode selection and element bindings

Select the eligible nominal source family from the written mode before
checking ambiguity:

| Loop form | Source factory or direct cursor | Published descriptor |
|---|---|---|
| `for (ref T item in source)` | ILendingSequence<T, TCursor> or ILendingCursor<T> | ref<T> |
| `for (mut ref T item in source)` | IWritableLendingSequence<T, TCursor> or IWritableLendingCursor<T> | write ref<T> |
| `await for (ref T item in source)` | IAsyncLendingSequence<T, TCursor> or IAsyncLendingCursor<T> | ref<T> |
| `await for (mut ref T item in source)` | IAsyncWritableLendingSequence<T, TCursor> or IAsyncWritableLendingCursor<T> | write ref<T> |

Within that family, use the common exact witness deduplication and
factory/direct-source ambiguity rules. A value witness alongside a lending
witness is not a second relationship for the selected lending mode.
Two eligible lending factories, or a factory and direct lending cursor,
still diagnose. Neither the element annotation nor body selects a different
witness. A generic definition needs its declared available relationship;
specialization does not change its bound role or expose private refinements.
No readonly/writable, synchronous/asynchronous or value/lending fallback occurs.

Here the iteration-specific `ref`/`mut ref` prefix selects an **element-place
alias**. The explicit T, or inferred `var`/`var?`, names the element type, not
the hidden reference descriptor type. While live, `item` denotes the original
T place through the published descriptor, with readonly/writable immediate
access respectively. It is not a copied T or the hidden output's descriptor
slot. `ref item` reborrows that same element; it does not borrow compiler
storage. Grouping and ordinary member lookup retain this place category.

For example, with the corresponding eligible witnesses:

```terrum
for (ref int32 element in source)
{
    Inspect(ref element);
}

for (mut ref int32 element in writableSource)
{
    element = int32(1);
}
```

Inspect retains its own ordinary readonly input/effect contract.
A typed alias requires the exact stored T; it cannot change `write C` storage
to C storage or alter a nested reference's type. Inferred optional elements
follow the ordinary presence rules; a non-null `var` does not unwrap them.
Reading an alias as a value still requires its actual Copy/transfer contract.
Borrowing a non-Copy element is valid without permission to copy it.

The prefix is not ordinary local descriptor mutability:
`for (mut ref<T> item in valueSource)` has a mutable by-value ref<T> binding
under value iteration. Its name denotes a descriptor slot and ordinary `*item`
projects its referent. In `for (mut ref T item in lendingSource)`, assignment
instead updates the eligible source element; it does not rebind the alias.
For T = ref<U>, a lending alias names a source descriptor slot, and accessing U
still requires explicit indirection. No general transparent reference use or
new `ref T` parameter/local declaration is introduced.

An alias has its own binding availability and lifetime, separate from the
element's ownership. `item is init` tests that binding without dereferencing
an ended alias. `item = _;` ends the binding and releases its remaining loan;
it does not vacate or destroy T. The name stays in its ordinary body scope,
but cannot be read, reborrowed, rebound or reinitialized after that end.
Outstanding copied descriptors or reborrows retain their actual dependencies;
ending the original binding does not end them.

Writable lending permits only ordinary eligible element writes/replacement,
preserving the source's storage, validity and ownership invariants. It grants
no element ownership transfer, immutable-field bypass, structural collection
mutation or permission to leave a source-owned slot vacant. Moving/retiring
the referent needs an independently declared extraction/surviving-state
contract; the lending role and writable alias alone do not supply it.
Neither readonly nor writable lending is automatically exclusive or NoAlias.

## Acquisition and traversal ownership

Evaluate the source once in the iteration header. Readonly factories use
ordinary readonly receiver evaluation without copying/consuming a non-Copy
source. Writable factories require their ordinary `mut` receiver access:
an eligible writable inline place or a handle with the required referent
authority. A mutable descriptor binding does not grant writable view backing,
and writable contained handles do not make readonly container slots writable.
Do not copy or materialize a readonly source to evade an invalid writable borrow.
An otherwise eligible temporary may use ordinary header materialization,
retaining its actual access, backing and cleanup obligations.

Invoke the selected GetEnumerator/GetAsyncEnumerator once and adopt its exact
complete TCursor. Both calls are synchronous; preparation can be explicitly
awaited before source evaluation under its ordinary task contract.
Acquisition retains declared effects/failures and does not start a generated
producer body. Failed acquisition cleans private/partial result state without
closing a vacant cursor. A synchronous factory cannot strand started borrowed
work or traversal obligations needing unperformed async cleanup.

A direct cursor instead stages/adopts its existing value with ordinary
Copy/transfer behavior. A named affine cursor requires explicit move.
No factory, reset, cloning, source consumption or ownership rollback is implied.
Retain every source/cursor storage, backing, root, lease, guard and affinity
dependency through traversal and required close. The cursor's acquisition
relationship and each emitted loan's relationship are separate obligations.

After successful adoption, the consumer owns that traversal's close
responsibility, not the source elements or collection. Cursor/header mutation
must satisfy its exact complete carrier and backing contract; a mutable local
does not upgrade a readonly handle or acquire pinning/sharing authority.

## Reference output and publication

Each advance receives a vacant destination for the exact descriptor type,
not vacant T storage. `ref<init ref<T>>` initializes a readonly descriptor;
`ref<init write ref<T>>` initializes a writable descriptor. Its initialization
reference supplies destination authority, not element authority.
Manual use of the initialized descriptor still follows explicit indirection.
Normal synchronous output lifting may apply under the existing
[omitted-output rules](calls-and-inference.md#omitted-outputs-and-retained-observations);
it preserves descriptor/backing obligations and creates no element ownership.
Async destinations remain explicitly supplied.

| Published outcome | Destination and element |
|---|---|
| True | One complete valid descriptor to a live permitted T place, after required exit checks/cleanup |
| False | Vacant descriptor destination and exhausted traversal; no element binding |
| Checked failure | No caller-readable descriptor; private/partial loan state cleaned and cursor cleanup-safe |

Async publication uses observed terminal completion, not task return.
False is permanent exhaustion for this traversal, not temporary unavailability,
an absent optional element, a caught fault or a null/default reference.
A true output cannot point into ended locals, invalid storage or insufficient
authority, or borrow the hidden descriptor slot as a fabricated T element.
Cursor-owned scratch storage is possible only as an actual live T place
with the stated cursor dependency.
Its owning cursor may retire that scratch T during its declared cleanup after
dependent loans end; descriptor/alias retirement itself never retires T.

The implementation retains private loan/output obligations until publication.
A body, postcondition or authored cleanup fault after descriptor formation
prevents publication and releases that unpublished loan once.
It does not destroy the referred-to element or roll back actual cursor effects.
Every checked exit preserves state safe for required close and residual
cleanup, without granting retry or traversal recovery after failure.

Saved Boolean/output facts retain exact destination versions.
Moving/replacing/retiring the output invalidates old availability facts,
not the live dependencies of a transferred or copied loan.
Descriptor retirement releases its own loan obligations, not T ownership.
No Copy, allocation, pin, purity or independent cleanup capability follows
from this publication shape.

## Output dependencies and checked refinements

The standard output has a **cursor-bound default**. Its published possible
origins include cursor storage and the cursor's checked source/backing set.
Retain that broad bound at the caller. Before the next advance, close,
cursor retirement/move or other operation that may invalidate it, every
dependent output, alias, copy and reborrow must have ended.
Different descriptor slots, cursor copies or equal addresses do not remove
this restriction. Keeping the cursor alive alone does not preserve backing
that an owner, guard, reallocation or structural source mutation invalidates.

A checked **published backing refinement** may narrow this relationship.
It can rule out cursor-storage borrowing, identify already-retained source
backing, and prove that specified later advance/close operations preserve
the element's validity. A surviving reference then follows that backing,
not a fictitious loan of a retired cursor descriptor.
Refinements can discharge one boundary without discharging another:
surviving advance does not imply surviving close, cursor movement, source
mutation, disposal or suspension.

For example, a scratch-backed cursor keeps the broad bound. A cursor over
stable span elements can publish that it only forwards the span's actual
backing and that advance/close do not invalidate those elements.
A list cursor additionally needs its declared storage/guard contract:
rooting the list or passing a version check does not prevent replacement,
reallocation, removal or disposal. These are relationship examples, not
automatic Span/List witnesses, a general view ABI or a new guard API.

The logical relationship schema retains these fields:

| Field | Required meaning |
|---|---|
| Owner and role | Exact Core requirement, selected witness/target and `lending-iteration/1` |
| Output selector | Initialized descriptor value of the exact init formal, its complete type and destination/version; not the Boolean result or descriptor parameter slot |
| Publication | Invocation or completion phase, exact outcome predicate, private/partial cleanup and true/live false/vacant failure/vacant relation |
| Element origin | Permitted existing T places; receiver storage versus forwarded backing, exact source/path identities and actual access |
| Dependencies | All permitted source bounds and per-instance backing, root, lease, guard, storage, validity and affinity obligations |
| Invalidation | Operations/effects that can end those dependencies, including advance/close and their failure/cleanup paths |
| Refinement | Exact published narrower source/invalidation relationship, its condition/evidence and selected member/witness identity |
| Forwarding and retention | Exact formal/source/phase mappings, destination/capture extent and responsible loan/completion/cleanup ownership |

All possible listed sources remain caller obligations unless an available
checked refinement excludes them; private branch selection is insufficient.
Opaque backing paths must come from an already-admitted capture/view
relationship. A path spelling, same address or receiver field containing an
allocation cannot establish that relationship or hide receiver-storage use.
Publication cannot require an unperformed pin, retain, allocation or guard
acquisition merely to make a false lifetime promise appear valid.

Check a refinement against every permitted implementation exit and affected
operation, including faulting advance/close and residual cleanup. It must
retain all remaining backing/access obligations, not merely claim a stable
address. Independent generic callers use the broad bound unless their
declaration exposes that exact refinement and establishes its condition.
Devirtualization, specialization and body inspection do not change source
validity or make a stronger implementation-only contract available.
Longer validity is not immutable contents, stable reads, exclusivity or purity.

An adapter/forwarder must map the initialized descriptor's source and
invalidation relationship as well as its ordinary output types and states.
It may conservatively widen the result bound, but cannot publish a narrower
one that its selected target cannot fulfill.
`[return: DependsOn(...)]` names an ordinary returned result; it is not this
init-output selector or a receiver/async capture declaration.
The logical fields above are selected.
[Dependencies and views](dependencies-and-views.md) supplies general
DependsOn/Captures/ForwardsBacking targets, direct receiver `this` and
exact formal recipes, including member-contract constraints.
An init-formal target describes the published inner descriptor.
Return-targeted DependsOn describes the declaration's ordinary result: the
Boolean for a synchronous advance, the completed Boolean payload for an
ordinary async implementation, or the task for a non-async task-returning
implementation. It does not select the initialized descriptor or the
responsible completion operation.
ForwardsBacking alone supplies no advance/close preservation guarantee.
Physical relationship codecs remain separately gated. This lending rule
does not adopt Item<'a>, MatchedAccess or a Stable marker.

## Pending async output and surviving loans

An async lending advance reserves the exact descriptor destination/version
and cursor access with its returned operation. Caller value/state tests,
competing init, retirement/reuse or overlapping invalidating cursor operations
are forbidden until checked terminal completion observation and cleanup.
Physical descriptor writes, IsCompleted, cancellation requests and abandoned
waits do not release that reservation. Observed true publishes the complete
loan; false/failure leaves the descriptor destination vacant.
The operation's init authority does not grant the caller early element access.

After publication, the loan has its own declared backing extent. Ending the
reservation or task does not end a still-live loan; retaining the task alone
does not preserve the element's backing. The cursor-bound default blocks the
next advance/close while any such loan survives.
An available refinement can permit an earlier loan to survive later advance,
but the pending operation's actual access/effect/capture contract must still
be compatible with using it. Non-invalidation is not disjointness, NoAlias,
exclusive access or permission for concurrent mutation.
Suspension requires every actual backing, root, guard and affinity contract.

Forwarding retains exact operation/output/source identities. An async wrapper's
inner publication makes its descriptor available to the wrapper, not the
outer caller; outer checks/cleanup precede outer publication and release
unpublished inner loan state on failure. Neither mapping nor waiting extends
the element lifetime or duplicates completion/close responsibility.
Guarded joins and saved outcome copies retain the existing reservation rules.
General task/supervisor APIs and later-completion output lifting are unchanged.
[Core consuming await](task-responsibility.md) moves the affine task into
its wait while preserving this reservation; a named owner requires explicit
move. Its settlement does not retire a published element loan or discharge
the cursor's separate close obligation.

## Loop order, close and escape

After acquisition, invoke one selected advance and, for async, await its
checked completion before binding an element alias or entering the body.
Each true establishes one alias activation in the body scope; false enters
no body. Retire body locals and that binding's remaining loan on every exit
before the next advance or required close.
This retirement does not destroy T or erase independently retained reborrows.
Every surviving loan must satisfy the selected invalidation relationship.

Required synchronous Close runs once on adopted traversal exit; required
CloseAsync runs once and is awaited before dependent header/source state ends.
Settle pending advancement and any published loan obligations before closing.
Close handles never-advanced and failed-advance cleanup-safe state without
running unentered producer bodies or replaying completed cleanup.
Ordinary failure/close chaining and residual cleanup apply; no suppressed
failure, successful-exhaustion fallback or implicit second close occurs.
Generated owner destruction remains nonthrowing and nonsuspending.

Escape after another advance or cursor close requires an available refinement
and genuinely surviving source/backing/guard obligations. Borrowing a temporary
owner or guard in the header cannot escape its cleanup; forwarding a temporary
descriptor can escape only when its independently surviving backing is proven
and descriptor/cursor storage is not a remaining dependency.
Returning/storing/capturing a loan preserves those facts and the destination's
ordinary retention/suspension contract. Copy, boxing and eager capture cannot
repair a short lifetime or silently transfer the source's cleanup responsibility.
An invalid escape rejects; the loop does not retain a per-item arena or prolong
an owner to make it work.

Synchronous collection spreads continue to select value iteration.
A lending-only source is not automatically dereferenced/copied into builder
items; an explicit value adapter must satisfy its actual Copy/transfer and
backing contract. No ref spread, async spread, deferred construction,
or associated lifetime-generic item is added.
[Generated lending bodies](generated-producers.md) use the independently
selected named affine producer registration; these output roles alone do
not generate a frame or shorten its loan bounds.

## Independent contracts and representation boundaries

Retained callers preserve the selected timing/access/source mode, complete
T/TCursor and descriptor forms, exact Core requirements/witnesses, output
phase/version, element origins, broad/refined dependencies, invalidation,
capture/responsibility and required loop/close schedule.
Replacement cannot add source capture, weaken a relied-on refinement, change
element authority or advance/close invalidation, or publish through an unrelated
task/output relationship. A stronger compatible implementation does not
silently upgrade the caller's retained broad contract.

Implementations claiming support preserve these relationships. The producer
is responsible for published body facts; consumers validate selected record
structure, references and support before affected use, then may rely on those
facts without semantic re-proof. Linking/remapping preserves semantic role,
member, formal,
output, source and condition identities independently of row numbers and
debug names. [Metadata requirements](../representation/logical-records.md#lending-iteration-encoding-pending)
specify retained logical obligations, not a new table, reference layout,
feature bit, optional Attr field or opcode allocation.
[Conformance cases](../conformance/source-lending-iteration.md) distinguish
these contracts from compiler/runtime or physical codec implementation.
