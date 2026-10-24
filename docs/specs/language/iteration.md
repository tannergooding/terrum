# Value iteration

This chapter owns logical `value-iteration/1`: standard synchronous by-value
cursor/sequence identities, loop acquisition, item publication and close,
and their use by collection spreads. [Source productions](../grammar-spec.md#33-statements-and-local-declarations)
and [ordinary execution](expressions-and-statements.md) retain statement
syntax, scopes and transfers. [Lifetimes](../lifetimes.md) and
[interface witnesses](interface-witnesses.md) retain access, backing and
nominal conformance. No iterator keyword, per-method role attribute, implicit
adapter or physical operation assignment is added.

“By value” describes the published item, not a deep copy of everything it
references. A checked-reference item is itself a descriptor with its supporting
dependencies; it does not select the separate
[reference-output lending protocol](lending-iteration.md). See the shared
[copying and aliasing definitions](../language.md#definitions-and-notation).

## Nominal protocol identities

Core's resolved Terrum.ICursor and Terrum.ISequence requirements have these
logical signatures:

```terrum
public interface ICursor<T>
{
    public mut bool TryMoveNext(ref<init T> item);
    public mut void Close();
}

public interface ISequence<T, TCursor>
    where TCursor : ICursor<T>
{
    public TCursor GetEnumerator();
}
```

The type/member/witness identities, not their names or a matching member
list, establish these roles. Conformance uses ordinary target-owned nominal
witnesses. The sequence member has the ordinary readonly receiver contract;
it does not authorize inline source mutation or consumption. Its result is
the exact complete TCursor satisfying the exact ICursor<T> instance.
Do not erase ownership/access qualifications, substitute an interface handle
or choose a cursor from its private body.

There is no universal `[Copy]` item constraint, separate synchronous
IConsumingCursor identity or standard Current property. Each implementation
must be able to publish its exact complete T by value under its declared
construction/copy/transfer and backing contract. A collection exposing
borrowed elements can copy them only when their actual contract permits it;
conformance does not allow moving them out of that collection.
An owning producer/cursor can instead transfer its own available item.
Named affine values still require ordinary explicit `move`.

The acquired cursor must support live header storage and the selected
mutable-receiver operations. A mutable local supplies place/slot authority,
not writable referent access, pinning, sharing, Copy or NoAlias.
A class/descriptor cursor retains its exact required handle/backing authority;
an unsupported readonly carrier is not silently upgraded.
Likewise each output needs eligible vacant T storage and ordinary validity,
initialization and lifetime support. The interfaces do not create these
representation capabilities from a nominal name or a claimed byte extent.

Ordinary public APIs named MoveNext or Current can remain useful, including
inside an explicit adapter. They are not operations synthesized by standard
iteration. An adapter implements the selected nominal protocol and preserves
its actual effects, state, ownership and dependencies without caller-side
member-name discovery.

## Source selection

By-value loops and synchronous collection spreads admit two source forms:

| Source contract | Traversal value |
|---|---|
| ISequence<T, TCursor> | One TCursor result of its selected GetEnumerator member |
| ICursor<T> directly | The supplied cursor value, with its existing traversal state |

Select one unambiguous complete item/cursor/source relationship from the
source's published available witnesses. Repeated inherited/imported paths
to one witness/member identity deduplicate. Distinct eligible relationships
reject, including a source offering both a factory and direct-cursor role.
Equal item types or an implementation returning itself do not merge two
acquisition contracts. An explicit checked adapter or ordinary explicitly
selected acquisition can expose the intended single source role.

Selection does not inspect loop bodies, source elements, builder bodies,
literal widths, runtime values or a Current getter. An explicit iteration
binding must satisfy the ordinary exact type/access rules after selection;
it is not an inverse search for a convenient source witness.
Generic bodies use their declared available relationships; later concrete
specializations cannot repair missing source guarantees or change a
previously bound relationship.
Source mode, normalized item/cursor terms, inherited requirement identities
and bound targets survive lowering and independent consumption.
The ordinary `var`/`var?` presence rules still apply. An initialized optional
item is not silently unwrapped to satisfy a non-null binding.

These standard roles are by-value. [Lending iteration](lending-iteration.md)
selects distinct readonly/writable roles for `ref`/`mut ref` element-place
aliases and their output/backing/invalidation contracts. They cannot borrow
the private by-value output slot as if it were a source element.
A by-value T may itself be a reference descriptor or contain a checked loan
or view; this retains T's actual dependencies, not a different iteration mode.
[Async value iteration](async-iteration.md) supplies its separate
completion-output reservation and async acquisition/advance/close contracts.
No blocking or task wrapper is inferred from these synchronous interfaces.

## Acquisition and ownership

Evaluate a loop's source expression once under the selected input role.
For a sequence, use ordinary readonly receiver evaluation: preserve an
eligible source place/handle and its actual backing, or materialize an
otherwise eligible temporary in the iteration header's supporting scope.
Do not copy or consume a non-Copy collection merely to obtain a cursor.
Invoke the selected GetEnumerator once, before any advancement.
Its declared effects/failures remain; readonly receiver access is not purity
or a claim that repeated acquisition returns independent or identical cursors.

Retain every source place, temporary, root, lease and guard on which the
returned cursor depends through traversal and required close.
A getter can return an independently owning cursor, borrow receiver storage
or forward source backing only under its checked published result relationship.
Neither body inspection nor descriptor copying invents that relationship.
The broad source relationship cannot be narrowed from a current private body.
Its source/retained-destination annotations and checked member conditions
follow [dependencies and views](dependencies-and-views.md).
Backing-only removes an own-storage alternative, not a source guard or the
required close responsibility.
Acquisition failure uses ordinary call/result/partial-initialization cleanup;
the caller does not invoke Close on a vacant acquisition result. Any complete
cursor formed privately before failure retains its actual close and cleanup
obligations with its responsible holder; failed publication does not erase them.

For a direct cursor, stage the supplied value using ordinary copy/transfer
rules and adopt that exact value into the header. A fresh owning result
can transfer directly; a named affine cursor requires an explicit move:

```terrum
for (var item in move cursor)
{
    Use(item);
}
```

Here Use has its own ordinary input/effect contract; the example does not
grant copying of a non-Copy item. No GetEnumerator call, reset, cloning,
second cursor or restart occurs on this path. A Copy cursor retains its
declared alias/backing and copy semantics; it is not automatically an
independent traversal or a duplicated resource-release capability.
The original moved binding remains vacant on exhaustion, early exit or
failure. There is no ownership rollback.

After successful factory acquisition or direct-value adoption, the consumer
has the required close responsibility for that traversal. This does not
dispose the source collection. Ordinary source-temporary cleanup still runs
at its own header endpoint after dependent cursor close; a borrowed original
collection remains with its owner.

## Advancement and item publication

Each TryMoveNext call receives one vacant destination of exact complete T.
It advances the selected cursor once and completes with this relation:

| Completion | Item state |
|---|---|
| Normal true | Exactly one fully initialized valid T, published after normal call checks/cleanup |
| Normal false | Vacant; this traversal has reached its end |
| Checked exceptional completion | No caller-readable completed item; partial/private output follows exceptional vacancy and cleanup |

False means exhaustion, not temporary lack of data, a successful default item,
refusal to allocate, or an exception converted to a Boolean. Later standard
advancement of that exhausted traversal cannot resume production or yield
another item; any explicit reset/new traversal needs its own API.
The false call does not itself discharge required close.
It can retain declared call effects, so its occurrence is not erased merely
because no item is returned.

Zero-data `void` is still a live initialized item on true and vacant on false.
An optional absent T is likewise a complete initialized value when its
actual representation/validity is admitted; absence is not exhaustion.
Do not manufacture a default T or read a vacant/partial destination.
Ordinary direct callers prepare vacant storage; the operation does not
implicitly retire an arbitrary live destination for them.

The selected implementation owns its private partial item until publication.
A body, postcondition or authored cleanup fault after forming it prevents
publication and cleans that unpublished item without fabricating success.
Actual cursor mutations/effects are not rolled back. Following failed
advancement, the minimum safe protocol obligation is closing actual live
state, not retrying/resuming a possibly invalid traversal.
The advancement contract must preserve cleanup-safe state for required
Close and residual value cleanup on every checked exit, including an exit
check/cleanup fault. Performing required cleanup is not unsafe recovery.
Recovery additionally requires ordinary unsafe authorization and an explicit
surviving-state/recovery contract; neither catching the fault nor vacancy
proves the cursor invariant.

Published items retain their exact copy/transfer, backing, guard, root and
lifetime obligations. Publishing a by-value descriptor does not make its
reachable state independent of the cursor/source.
An item borrowing cursor state can restrict later advancement and close;
an item forwarded from independent backing follows that backing instead.
Any narrower relationship is a checked published contract, not a large-value
ABI optimization or private-body assumption.
No NoAlias, purity, ConstEval, implicit cloning or heap allocation follows
from the Boolean/output shape.

## Loop order and close

After acquisition/adoption, create eligible item storage and call TryMoveNext.
Enter the body only after true has published the complete item.
The source binding denotes that item without a mandatory intermediate copy;
additional authored value uses retain ordinary Copy/explicit-move rules.
The binding has the iteration body's logical lifetime.
Retire its remaining owned state and body locals on normal exit, continue,
break, return or failure before advancing again or closing the cursor.
A taken ownership transfer leaves no second owner to retire.

Prepare a vacant destination for each following call under those ordinary
cleanup/loan rules. Reusable physical storage is permitted; a growing
per-iteration arena or retained item list is not introduced.
An outstanding item/source loan cannot be ignored to vacate or reuse storage,
advance, close or return a result. Legal escape requires genuinely surviving
published backing, not a hidden loop temporary.
Inner/named-loop transfers perform their ordinary intervening cleanup.

Invoke the selected Close once for every adopted traversal, on exhaustion,
break, return or failure, including advancement or body failure.
Do not close uninitialized acquisition output. Closing ends that traversal
and discharges its actual cursor-owned/retained close obligations.
Already completed producer/body cleanup cannot run again, and subsequent
ordinary cursor-value cleanup handles only residual obligations not already
discharged/transferred. An implementation cannot cause double release by
independently running the same close obligation in both phases.

Close is an ordinary synchronous protocol operation with its selected
effects and possible authored failure, not an automatically nonthrowing
`[Cleanup]` destructor. Generated owner cleanup remains nonthrowing and
nonsuspending. A close fault preserves ordinary causal failure/chaining and
remaining cleanup; it is not silently suppressed or replaced with successful
exhaustion. A close that did not complete does not prove all obligations
discharged. No implicit second protocol-close attempt or ownership revival
is introduced on that path.
Manual consumers uphold the same valid-use, close and recovery obligations;
they cannot implicitly reopen a closed traversal or repeat completed cleanup.
Additional closed-use/reset APIs require their own declared contracts.

## Eager collection spreads

[Construction capture](construction-handlers.md#eager-capture-and-acquisition)
still evaluates every reached spread source once in source order before
builder acquisition. A sequence source captures the checked receiver/place/
handle and backing for later GetEnumerator; it does not acquire a cursor or
start a producer body there. At the spread's drain position, acquire its
cursor once and perform the standard advance/close path.

A direct-cursor source instead stages and adopts an already-created cursor
at its eager capture position, using ordinary explicit move/Copy rules.
Register that capture's close responsibility then, without advancing it.
This distinction matters when a later capture or builder acquisition fails,
or an earlier accept refuses before this spread is reached:
close the captured direct cursor during ordinary abandonment even though it
was never advanced. Do not acquire an unreached factory cursor, resume an
unstarted producer body, or treat Close's cleanup effects as enumeration.

For a reached spread, each true publishes one exact TItem under the selected
construction item relationship. Transfer that item into the builder's
by-value TryAdd input under ordinary copy/transfer rules; do not reread
Current, synthesize a conversion or move from borrowed collection storage.
After normal false, close that spread cursor before later drain fragments.
Acceptance refusal or advancement/acceptance failure closes reached state
and stops later drain operations. A consumed rejected input is cleaned by
its actual holder; the spread cannot clean it again.
If an item was published but not transferred, clean its remaining obligations
before cursor close. Other already-captured direct cursors still require
their own abandonment close in ordinary cleanup order.

Retained item/backing must survive builder storage and final publication
when the target requires it. A short-lived cursor loan cannot be made valid
by Copy, boxing, scalar equality or eager capture.
There is no speculative count/length pass, replay, full-enumeration promise,
automatic async spread or collection materialization.
Generated producer, lending/async spread and nonstandard extensions remain
separately admitted; none reintroduces incremental construction handlers.

## Producer and extended protocol boundaries

An already-created one-shot producer can expose the direct cursor contract;
a factory-style sequence can expose GetEnumerator where its actual captures
can satisfy that acquisition contract. The selected source form does not
invent copying of affine captured state or universal re-enumeration.
Existing producer rules keep body start on first advancement and distinguish
creation, advancement, completion and close.
The [generated-producer chapter](generated-producers.md) selects owned named
IteratorCursor shells, Iterator callables, direct affine results and checked
capture/creation/advance/completion/close phases. Factory/reusable sequences
remain explicit wrappers; private state-machine layout and physical programs
remain separately admitted. This is not body-driven result inference or a
claim that every proposed concrete API has an implemented body.
Async value iteration has its own selected [completion contract](async-iteration.md),
not an inferred extension of these synchronous signatures.
[Reference-output lending](lending-iteration.md) likewise has separate selected
nominal roles; it does not change reference-valued by-value items or register
generated lending producers by itself.

## Retained contracts

| Required field | Meaning |
|---|---|
| Rules/identities | `value-iteration/1`, exact Core interfaces, inherited members and selected nominal witnesses |
| Source relationship | Sequence-factory or direct-cursor mode; complete source, T/TCursor and acquisition target, with actual access/copy/transfer |
| Output | Exact init destination, true/live, false/vacant and exceptional publication/cleanup relation, including unit/optional state |
| Traversal | Actual cursor state, end/close obligations and effects; no Current lookup, inferred restart or recovery |
| Dependencies | Source/receiver/backing, cursor/item roots, leases, guards, retained loans and lifetime/refinement promises |
| Schedule | Once-only source evaluation/acquisition, body/item retirement before advance/close, eager direct-capture close and spread order |
| Admission | Supported operations/forms/rules, complete references and compatible published contracts; producer is responsible for their semantic truth |

Implementations claiming support preserve these requirements; reader
validation is implementation-defined. Linking/remapping preserves semantic source/cursor/item and operation
identities rather than reselecting by names or physical row order.
Changing acquisition/ownership, the output relation, close timing or published
backing invalidation is incompatible with retained callers.
Physical witness/state/output/capture/program records remain separately
gated by the [metadata requirements](../representation/logical-records.md#value-iteration-contracts-encoding-pending);
no optional attribute, reserved flag/table slot or opcode acquires this role.
[Conformance cases](../conformance/source-value-iteration.md) distinguish
these logical contracts from compiler/runtime/physical implementation.
