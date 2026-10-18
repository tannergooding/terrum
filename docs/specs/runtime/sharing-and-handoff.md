# Thread capabilities, sharing and handoff

This chapter owns logical `thread-capabilities/1` and `sharing-and-handoff/1`.
It specifies source capability contracts and admissible authority transitions.
The [memory model](../memory-model.md) owns access observations and ordering;
[dependencies](../language/dependencies-and-views.md) and
[ownership](../language/ownership-and-cleanup.md) own backing and retirement.
These rules supply neither a thread/lock API catalog nor a physical codec.
[Concurrent allowed executions](concurrent-executions.md) supplies observation,
mixed-SC/fence, causal and lifetime-event rules. Logical protocol admission
still needs the actual operation, representation and target profile; it does
not prove an arbitrary implementation's permitted observations.

## Capability eligibility is not current authority

A capability is a checked, published property of a normalized complete type
form. Its instance's present access, actual dependencies, protocol state,
execution placement and cleanup responsibility remain separate.

| Capability | Published eligibility | Not granted |
|---|---|---|
| ThreadTransferable | The value and carried obligations can cross a thread boundary under its admitted handoff contract | Concurrent aliases, mutation, loss of a loan or thread/executor affinity |
| ThreadShareable | The form's exposed operations and reachable authority can satisfy its declared concurrent-use protocol | Current exposure, an acquire/release operation, deep immutability or an atomic compound update |

Neither capability implies the other. A payload can stay owned and cleaned
on its home execution while a checked shared alias exposes only permitted
operations elsewhere. That alias needs its own complete crossing contract;
sharing is not permission to transfer the payload's ownership or cleanup.

Copy, unique, GC reachability, readonly access, equal layout and NoAlias
establish neither capability. Readonly is shallow: a readonly slot containing
`write C` still exposes that writable handle. Current confinement does not
permanently bind a value to an OS thread, but a checked handoff must preserve
any actual thread/executor affinity of use, continuation or cleanup.
An ordinary move within one execution is not a cross-thread handoff.

## Explicit source opt-in and conditional contracts

User-defined nominal storage types require explicit opt-in. The resolved Core
ThreadTransferable and ThreadShareable schemas are no-argument, nonrepeatable
primary-type attributes. Each selects its own capability; a same-named user
attribute, interface or method selects nothing. Eligible targets are nominal
structs, classes, unions and enums, not aliases, type formals, callables,
static-class containers or behavioral interfaces without a storage form.
Constraint occurrences such as `[ThreadTransferable]` ask for the checked
property, not the presence of an annotation on the supplied argument.

An opt-in can own ordinary constraint clauses immediately after its attribute
and before the next attribute or declaration prefix. Those clauses bind the
annotated type's exact formals and associated/member identities, including
formals declared later in its header. They are a conjunctive, capability-local
condition, not runtime attribute arguments or constraints on the whole type.
Only these two schemas admit this context. Ordinary type-header constraints
retain their existing type-wide meaning.

```terrum
[ThreadTransferable]
where T : [ThreadTransferable]
[ThreadShareable]
where T : [ThreadShareable]
public struct Snapshot<T>
    where T : [Copy]
{
    public T Value;

    public Snapshot(T value)
    {
        Value = value;
    }
}
```

The fragment assumes admitted Core identities and storage/protocol support.
Every Snapshot argument must be Copy. Transferability and shareability are
separately available only when their respective conditions hold. An argument
lacking one capability can still form Snapshot and use its ordinary confined
operations. No new `where [Capability] T : ...` production is introduced.

An omitted capability condition means an unconditional promise for that
declaration's admitted complete forms under its exported protocol. The checker
must establish that promise, not silently infer extra generic conditions.
Conditions use existing supported conformance, exact associated-identity and
member-contract terms. Unknown properties, unresolved selectors, invalid
formal kinds and unsupported condition truth diagnose; arbitrary runtime
predicates or private-body queries are not capability conditions.
Conditional clauses on a secondary partial type cannot author or alter the
primary opt-in. Repeated opt-ins reject even when their clauses agree.

For every condition-satisfying form, establish the capability over the actual
representation, exposed operations, reachable authority, generic arguments,
roots/backing, code/load lifetime and transitive cleanup. Type-wide requirements
and capability-local assumptions remain distinct. Ordinary generic bodies may
rely only on their available declaration facts; a capability-local premise
does not become an unconditional constraint for every use of T.
Use of a conditional fact requires its conditions established at that use.
Failure to establish them with the supported checker is not permission to
inspect a specialization or guess a synchronization algorithm.

Core explicitly publishes both eligibility facts for the mandatory complete
scalar value forms and void. This does not make storage currently shared or
make a wide scalar access indivisible. Optional values and tuples preserve
their component's complete conditions under Core's declared structural rules.
References, owning carriers, callable environments, tasks and views require
their own admitted family/representation contracts and actual carried bounds;
there is no blanket rule based on pointer size or outer T alone.
An alias preserves its normalized target's facts; a nominal wrapper or adapter
needs its own opt-in and proof. Erasure/projection preserves every required
capability and carried obligation without acquiring a missing one.

## Published proof and admitted protocols

Source checking establishes declared capability and protocol truth. The
initial mechanical floor composes these explicit protocol forms:

| Protocol | Required checked facts |
|---|---|
| Confined handoff | Exact extent, complete crossing eligibility, all usable aliases, responsible holder, destination placement and actual end/return endpoints |
| Immutable publication | Defined immutable exposed extent, excluded conflicting mutation, complete initialization, publication communication and retained lifetime |
| Scoped exclusion | Exact protected extent and acquisition identity, reader/exclusive modes, token authority, ordering, alias/reentrancy rules and checked release |
| Shared scalar access | Permitted competing paths, compatible location/generation/width, valid access and explicit load/store/RMW order and representation support |

The checker validates these transitions and their composition with ordinary
calls, failures, captures and cleanup. A method name, annotation, hardware
instruction or lock-shaped object is not a primitive proof. Every admitted
primitive contract identifies its semantic operation, permissions and effects;
unsupported primitives or missing contracts reject before affected use.

An arbitrary synchronization algorithm is not inferred safe merely because
its source is available. Custom/native implementations can use the existing
explicit unsafe scopes and implementation responsibility behind an ordinary
safe declaration. No extra trusted-implementation attribute is required.
They must establish the safe exported contract, including obligations that
can be checked mechanically; the unsafe boundary identifies the remaining
implementation responsibility rather than supplying a proof of it.
An unsafe callable instead exposes its actual caller obligations.

Binary consumers follow the existing explicit producer/audit trust policy
for semantic implementation facts. Provenance or a record flag is not truth;
untrusted implementations need the required audit or are declined. Caller
checking still validates authority, conditions, compatibility and required
profiles. Trust never waives malformed framing, unknown required semantics,
missing retained dependencies or a mismatched call/representation contract.

## Confined handoff and return of authority

A handoff binds actual source/value and storage generations, not names,
pointer bits or equal type arguments. Its extent covers every authority and
dependency that the crossing can use or invalidate. Whole-source published
contracts remain conservative; private field inspection cannot narrow them.
Whole-value transfer requires a complete value. A structurally eligible child
can cross independently only under its own full contract and surviving
sibling/parent obligations.

Before remote access is enabled, establish crossing eligibility and placement,
retain actual backing/roots/code, and account for every still-usable alias to
conflicting state. A writable handle sent while a usable sender alias remains
is not exclusive handoff. An immutable binding or NoAlias on one invocation
does not make the other aliases unavailable.

A permanent owning transfer vacates its named affine source and transfers its
resource, completion and cleanup responsibility to an actual receiving holder.
Failure before commitment leaves authority with its actual current holder;
argument/acquisition effects already performed are not rolled back.
Failure after commitment must retain a responsible receiver/transit holder
or settle under the checked failure contract. It never revives the sender's
value or silently abandons adopted work.

A temporary handoff may retain sender aliases only when their relevant use is
checked unavailable for the full remote extent. It retains an actual return
obligation/holder and the sources needed for return. Reads, writes, state tests,
new derived aliases of reserved storage, conflicting retirement and storage
reuse cannot bypass that reservation. A test of a local carrier binding alone
does not access its referent or establish returned authority.
Ending a local descriptor is not access to its referent,
but cannot discharge the holder's remote/return obligations.
Descriptor rebinding can create a different local value when ordinary storage
and dependency rules permit it. Return binds the retained alias and source
generations, not the descriptor's current name; it cannot overwrite a rebound
descriptor or revive one whose lifetime already ended.

Authority returns only after actual remote use and required cleanup cease and
the specified return communication is established. An observed flag, terminal
status, cancellation request, queue notification or loss of the last visible
handle is insufficient. Restore only the returned authority for the actual
retained generations; independently released aliases remain released.
Remote exceptions preserve the same every-exit return/transfer responsibility.
If a protocol permits return before observation, callers still need the exact
checked endpoint before using their reserved authority.

Cross-thread publication and return require their declared memory-model
communication edges. Neither transferring pointer bits nor a source move
creates synchronizes-with. A serialized migrating flow still checks each
actual handoff, outstanding loan and continuation/cleanup affinity.

## Immutable and shared publication

Publication exposes an explicitly defined complete extent through an admitted
protocol. Establish its value validity, storage generations, root/backing
bounds and access domain before another execution can observe it.
The receiving execution must establish the specified communication relation;
an acquire reading an older value or a relaxed nonpublishing write cannot
stand in for the selected publication.

Immutable publication excludes conflicting mutation of the promised extent
for its promised duration through every permitted alias. Shallow readonly,
fixed slots and GC reachability do not establish this. An exposed writable
reachable handle needs its own compatible sharing protocol; it cannot be
silently counted as immutable data. Published immutable reads need no repeated
acquire once that publication and lifetime are established.

Publication of mutable state changes every admitted competing access path to
its selected shared protocol before exposure. A retained private/old alias
cannot bypass it by claiming that its bytes were copied while confined.
Sharing eligibility alone performs none of these transitions.
Withdrawal requires actual quiescence of readers/operations and retained
dependencies before exclusive mutation, reclamation or storage reuse.
An empty/end view retains its publication, source and lifetime obligations.
No hidden copy, pin, retain, freeze or owner registry repairs invalid publication.

## Scoped reader and exclusive guards

An admitted guard covers an exact protected extent and storage generation.
Acquisition grants a selected read or exclusive access only after its success
endpoint. A reader/writer protocol permits compatible readers or one exclusive
writer, not their overlap. A single writer alone still conflicts with readers.
The protocol must account for every path, including fields, aliases, callbacks,
iterators and references outside the wrapper.

The acquired token is affine release responsibility. Borrowing it for checked
nested use does not create another release owner. Scope exit or defined unwind
releases it once under its nonthrowing/nonsuspending release contract, after
all accesses and dependent loans whose lifetime requires it. Moving an eligible
token preserves those duties; Copy, abandonment and hidden release/reacquire
are not substitutes. A token is distinct from the protected value and from
the place containing the token.
Guard-backed aliases and loans bind that acquisition's token identity.
A later acquisition of the same guard and extent does not revive an ended
token or its views.

Release by an actual holder and a matching successful acquisition of that
same guard protocol supply the protocol's specified ordering. Mere exclusion
or a same-named method does not supply an edge. A read acquisition must import
the required writer publication; the primitive must specify communication
through its actual writer/reader epochs, not assume every reader has released
a write. Protected ordinary accesses need no separate atomic instruction
when the protocol excludes all competing conflicts.

Protected state need not itself be independently ThreadShareable. A wrapper
can encapsulate it and publish a checked ThreadShareable contract. Each actual
use and change of execution still preserves the protected state's transfer,
backing and affinity obligations. Guarding a thread-affine resource does not
authorize remote use or cleanup. A wrapper exposing an unbounded writable
reference or an unguarded operation has not established that encapsulation.

Reentrancy is explicit, not inferred from thread identity. The initial checked
scope can pass an existing token to a helper; it cannot invent a second
overlapping acquisition. Each primitive states whether and how recursive
acquisition, reader/writer upgrade or downgrade is supported, including its
failure/progress and validity effects. Missing support diagnoses; there is no
implicit upgrade, release/reacquire or deadlock-repair rule.

Callbacks using protected data require their actual token/access and capture
contracts. Calling arbitrary reentrant code cannot be assumed to keep an
exclusive invariant. A callback may only borrow for its admitted bounded use
or explicitly retain an independently permitted authority/holder; a returned
view cannot outlive the supporting guard merely because it is readonly.
Returning an independent copied value or explicitly transferred owner has its
own complete crossing/dependency obligations.

Holding a guard across suspension or handoff requires a protocol explicitly
admitting that extent and the continuation, release, affinity and progress
contracts. Otherwise the crossed use rejects. Neither async syntax nor a
cancellation request releases a guard. The ordinary ListWriteAccess loan
guard arbitrates structural aliases/reentrancy, not cross-thread exclusion;
its name and exclusive slot access do not establish a mutex.

## Shared scalar paths, effects and captured duties

The scalar protocol admits only its stated compatible live location, width,
representation and access domain. Ordinary permitted shared accesses retain
the memory model's actual non-tearing/coherence guarantees, not an automatic
RMW, acquire or freshness promise. A potentially competing `x++` still has
load/add/store semantics and rejects unless exclusion or an explicitly admitted
compound protocol covers it. Explicit atomic operations do not create sharing
authority, roots or lifetime, and unsupported widths do not tear as fallback.

Published method/witness records preserve required or transferred protocol
authority, synchronization, reentrancy, blocking/suspension, failure and capture
effects. Synchronization and atomic/ordered observation are not pure reads.
A pure helper may read under an already established compatible input extent
only if its own effect and read-dependency contract is pure. ThreadShareable,
readonly and backing-only forwarding imply neither stable contents nor Pure.

Captured views, closures, cursors, tasks and outputs retain their exact source,
guard, return/handoff, code, root and affinity bounds until actual release.
Core Task<T> responsibility is not a transfer/share capability; its captures
and continuation/cleanup still require admitted crossing support. IsCompleted,
terminal cancellation and result observation do not independently discharge a
guard or handoff reservation. Protocol endpoints and payload/output lifetimes
remain distinct, and an operation cannot manufacture a thread capability from
successful completion.

Joins preserve every reachable reserved, guarded and responsible state with
its actual path guard and generation. An available path and a remotely reserved
path are not unconditional ordinary availability. Exceptional cleanup keeps
the actual holder; it cannot infer return/publication from the lack of a normal
result or perform an implicit blocking join.

## Failure, unsafe use and retained admission

| Boundary | Required treatment |
|---|---|
| Invalid or unestablished safe source authority/capability/protocol | Diagnose before the affected operation; no inferred repair or optional-only warning |
| Missing/incompatible required imported rule, condition or representation | Reject binding/admission before affected use, including under producer trust |
| Permitted dynamic contention or mismatch | Follow the primitive's declared wait, Try/status or checked failure contract; no token/access before acquisition success |
| Checked value/precondition/acquisition failure | Follow ordinary failure/unwind with actual staged holders and retained effects; never fabricate authority, success or a default output |
| Unsafe caller or implementation violates its stated obligations | The invalid operation lacks its promised access/value/lifetime guarantees; no automatic repair, new synchronization or arbitrary whole-program data-race UB |

Unsafe obligation failure can cause the actual invalid access and its causal
consequences, including platform faults or corrupt state under the admitted
foreign boundary. This is not a containment or memory-safety theorem for
incorrect unsafe code. It does not license the compiler to invent unrelated
behavior, declare the whole program unreachable or add an unrequested trap
merely because a competing access violated its protocol. A declared check or
fault retains its own semantics. No nominal marker hides an unsafe obligation.

Retained contracts record exact capability identities, conditional premises,
complete forms and generic mappings, admitted protocol/extent/operation
identities, authority and generation bindings, publication/return/release
endpoints, effects, backing, affinity, cleanup and required rules/profiles.
Consumers may rely only on the declared conditions and guarantees. Replacement
cannot silently remove a relied-on capability, strengthen its conditions,
add caller obligations, weaken ordering/return/guard promises or change an
observable failure/cleanup contract.
Private proof, specialization or producer trust cannot change source binding.

Logical approval assigns no capability bit, guard token layout, reservation
table, proof certificate, feature number, opcode or native helper ABI.
The [concurrent-execution rules](concurrent-executions.md) are selected;
compiler/runtime and target conformance still require evidence. Physical
records, collector-aware crossings, concrete primitive/library catalogs and
target profiles remain independently admitted.
[Conformance cases](../conformance/source-sharing-and-handoff.md) distinguish
finite supplied-fact protocol evidence from source/runtime implementation proof.
