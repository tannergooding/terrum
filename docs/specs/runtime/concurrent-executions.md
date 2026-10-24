# Concurrent allowed executions

This chapter owns logical `concurrent-executions/1` for admitted CPU access to
ordinary coherent cacheable memory. It defines allowed observations, not a
physical encoding, atomic API catalog or target instruction mapping.
[Sharing and handoff](sharing-and-handoff.md) supplies actual authority and
protocol admission; [lifetimes](../lifetimes.md),
[dependencies](../language/dependencies-and-views.md) and
[ownership](../language/ownership-and-cleanup.md) supply storage and retained
obligations. The [memory-model overview](../memory-model.md) explains the
selected policy and separate device, progress and target boundaries.

An execution is allowed only if it satisfies every applicable rule below,
its ordinary source execution contracts and its admitted access protocol.
Passing an observation check cannot repair missing authority, invalid values
or ended storage. Stronger target behavior may realize a subset of allowed
observations; no target may introduce a forbidden observation. These rules
do not promise freshness, fairness or eventual completion.

## 1. Events, locations and candidates

A candidate is a finite execution or finite prefix, including the source
evaluations, outcomes and protocol transitions needed to justify its events.
A returned read has an actual source; an uncompleted operation is not credited
with a returned value, successful update or completed publication. Finite
prefix validity is not a proof of whole-program termination.

Every dynamic event has an identity independent of names, addresses and table
indices. A memory event identifies its execution context, operation and
outcome, storage object and lifetime generation, exact accessed extent,
normalized carrier/representation, width, alignment, observer domain and
admitted access protocol. Equal pointer bits do not establish that identity.

A **coherent location** is one exact extent of a live storage generation
under compatible whole-value non-tearing accesses. All admitted ordinary,
ordered and RMW accesses to that location participate in the same coherence
rules. Neither dedicated atomic-object storage nor a storage annotation is
required. Incompatible overlapping extents, bytewise foreign access and
tearing aggregates require their separately admitted protocols; this chapter
does not invent a whole-value read source for them.

A read has a read port. A modification has a write port. A successful RMW has
both ports on one indivisible event; failed comparison or checked arithmetic
has only the read port. Fences, lifecycle and other protocol events need not
have either port. A store of unchanged bits is still a modification when the
source operation performs it. Result omission does not remove a port or its
effects.

Creation, valid initialization, publication, actual quiescence, retirement
and reuse are distinct events or checked phases. Each coherent location has
an actual first initialization modification, not an invented zero value.
Its bits and validity must be justified by the source construction contract.
Further initialization writes remain modifications of that same generation.

## 2. Source sequencing and communication

**Sequenced-before**, `sb`, is the strict source sequencing relation within
one execution context. It includes the applicable evaluation, call, outcome,
cleanup and continuation order. It is not machine issue order. A candidate
must respect selected branches, checked failures and partial completion;
unreached evaluations have no effects to publish.

**Reads-from**, `rf`, relates an actual modification `w` to a read `r` whose
returned bits are those stored by `w`. Each read has exactly one source of its
own coherent location and generation. Matching bits alone do not select that
source. A successful RMW cannot read from its own write port.

### Matched release/acquire endpoints

For each actual pair `w rf r`, choose each applicable endpoint as follows:

| Side | Permitted endpoint |
|---|---|
| Release | `w` itself if it performs release, or a release fence `P` with `P sb w` in the writer's execution |
| Acquire | `r` itself if it performs acquire, or an acquire fence `Q` with `r sb Q` in the reader's execution |

Each such pair creates `P sw Q`, where `sw` means **synchronizes-with**.
Release fences include Release, AcquireRelease and Sequential; acquire
fences include Acquire, AcquireRelease and Sequential. A Sequential access
has the acquire/release side applicable to its actual ports.

This defines access/access, fence/access, access/fence and fence/fence
publication. A fence's carrier may be a relaxed access or an ordinary
whole-value non-tearing access expressly permitted by its checked protocol.
The carrier still has an exact location, source and lifetime. An unrelated
fence, equal observed bits or an unspecified notification creates no edge.

There is no release-sequence traversal. A relaxed RMW that reads a release
does not relay that release to a reader of its own write. An acquire-release
RMW can instead acquire from its actual source and release its own update.
Its own incoming and outgoing edges, and their transitive consequences, are
explicit. Dependency-only consume supplies no publication edge.

Other admitted primitives may publish specifically defined start, completion,
guard release/acquisition or quiescence edges. Their endpoint identity,
scope and conditions must be explicit. Queueing, task ownership, consuming
await, terminal status, cancellation, guard-shaped values and pointer
replacement supply none by name alone.

**Happens-before**, `hb`, is the transitive closure of `sb`, `sw` and those
specifically admitted primitive edges. It must be irreflexive. An `rf` edge
alone is not an `hb` edge; SC placement alone is not publication.

## 3. Per-location coherence

Each coherent location has one strict total **modification order**, `mo`, on
its actual modifications, beginning with its initialization modification.
There is no modification-order entry for a read-only failure, fence,
retirement or an access to another generation.

For distinct applicable events of one location:

| Relationship | Required consequence |
|---|---|
| Modifications `a hb b` | `a mo b` |
| Modification `w hb r` | `w` is the read's source or precedes its source in `mo` |
| Read `r hb w`, where `w` modifies | The read's source strictly precedes `w` in `mo` |
| Reads `a hb b` | The source of `a` is the source of `b` or precedes it in `mo` |
| Successful RMW `u` | Its source is the immediately preceding modification before `u` in `mo` |

A read therefore cannot read a write that happens after it, go backwards
across an `hb`-ordered read, or miss an `hb`-preceding modification in favor
of an older source. With competing unordered writers, coherence may still
permit more than one source. With conflicting writers excluded and the last
write established by `hb`, the read sees that write. Coherence alone does
not order independent locations or make every poll observe a new value.

Define **from-read**, `fr`, by `r fr w` when `rf` supplies `r` from `v` and
`v mo w`, excluding the two ports of the same RMW event. Define
**extended coherence**, `eco`, as the transitive closure of `rf`, `mo` and
`fr` on distinct access events of one location. It is irreflexive.
These relations describe source observations, not wall-clock freshness.

## 4. Required access order

Value justification and access ordering are separate requirements. Define
**preserved access order**, `pa`, on covered memory accesses by the following
rules and any additional ordering promised by their admitted protocol.
Here a fence or boundary lies between accesses in `sb`.

| Contract | Required `pa` edges |
|---|---|
| A read performs Acquire or Sequential | From that read to following covered reads and writes |
| A write performs Release or Sequential | From preceding covered reads and writes to that write |
| Acquire fence | From preceding reads to following reads and writes |
| Release fence | From preceding reads and writes to following writes |
| AcquireRelease fence | The union of the preceding two fence rules, not an added store-to-load edge |
| Full boundary | All preceding reads and writes precede all following reads and writes |
| Full RMW attempt | In addition, preceding accesses precede the attempt and the attempt precedes following accesses, including read-only failure |
| Necessary semantic dependency | A read precedes another access whose existence, address, stored value or outcome requires that read's value in its valid justification |

Necessary dependencies use the all-input independence rule in section 7,
not a syntactic mention or speculation about the chosen outcome. Dependencies
inside one indivisible RMW do not create a self-edge. Required source fault,
failure and observable-effect precedence also remains in force; it cannot
be removed by assuming that an unresolved preceding operation will succeed.

The transitive closure of `pa` and `rf` must be irreflexive. This requires
finite delivery of sources consistent with the order actually requested.
It does **not** add every `sb`, `mo` or `fr` edge to one global delivery
order. In particular it does not impose multicopy atomicity or prohibit all
independent-store load-buffering outcomes.

Relaxed and admitted ordinary accesses add no acquire, release or full
boundary merely by being non-tearing. A full boundary is not a transaction,
nor publication without the communication in section 2. CompilerFence
constrains compiler transformations under its declared compiler contract;
it supplies no CPU `sw`, SC event or full CPU boundary.

## 5. Sequential consistency and full boundaries

There is one strict total order `S` on all Sequential accesses and Sequential
fences in the covered execution. It respects ordinary Terrum `hb` between
its participants, not a weaker substituted relation. Non-SC accesses do not
become participants because a neighboring operation is SC.

Sequential Fence is an SC full boundary. An RMW attempt whose actual outcome
uses Sequential is an SC access **and** a full boundary. CAS uses its selected
success or failure order; Sequential success does not upgrade an explicitly
weaker failure order. Attempted checked-arithmetic failure retains Sequential
when that was the requested order. A Sequential read or write has its applicable
acquire/release side and SC participation; the name alone does not give a
read a before-boundary or a store an after-boundary.

For an access `a`, define `Before(a)` to contain `a` if it is SC, and every
SC full boundary `X` that covers `a` and satisfies `X hb a`.
Define `After(b)` symmetrically: `b` if it is SC, and every covering SC full
boundary `Y` with `b hb Y`.

For every `a eco b`, every `X` in `Before(a)` must precede every `Y` in
`After(b)` in `S`. Together with the `hb` constraint, this is exactly the
following four boundary cases:

| Coherence-ordered pair `a eco b` | Required SC order |
|---|---|
| Both accesses are SC | `a S b` |
| `a` is SC and `b hb Y` for a covering SC full boundary | `a S Y` |
| Covering SC full boundary `X hb a` and `b` is SC | `X S b` |
| Covering SC full boundaries `X hb a` and `b hb Y` | `X S Y` |

A self-edge or cycle in these required edges rejects the candidate; selecting
a convenient total order cannot repair it. Non-SC accesses can constrain
`S` through their actual coherence and these boundaries without joining `S`.
The rule covers ordinary admitted carriers as well as explicit atomics.

In the all-SC case each read observes the last modification of its location
preceding it in `S`, or initialization when there is no such modification.
For a mixed case, use the exact `rf`, coherence, `hb` and boundary rules;
do not simply place every relaxed write into `S` or allow an arbitrary
same-bits source.

Consequently relaxed store buffering with both reads zero is allowed, but
the all-SC version and the version with a Sequential fence between each
store and load forbid it. AcquireRelease fences alone need not forbid it.
The same full-boundary distinction applies to strong RMW attempts on other
locations, including failed attempts.

Opposite IRIW observations remain allowed with independent release writers
and acquire readers. Making all four reader loads Sequential forbids those
observations even if the writers remain release: each reader's first read
precedes its second in `S`, while `eco` requires each initial-value read
to precede the other reader's read of that location's write. Those edges
cycle. A target realizing stronger acquire behavior remains conforming.

## 6. Atomic outcomes and failure effects

Every successful atomic RMW reads its immediate `mo` predecessor and computes
its update from that exact value and its evaluated arguments. A replacement
or unchanged-bit update still has the declared atomic and ordering effects.
"Updated value" means this operation's update, not the value remaining after
a later modification.

Strong CAS compares the eligible defined representation's bits, not user
equality. Mismatch is a read-only failure; a matching comparison cannot
spuriously fail. The valid compile-time-known order pairs are:

| Success | Failure |
|---|---|
| Relaxed | Relaxed |
| Acquire | Relaxed or Acquire |
| Release | Relaxed |
| AcquireRelease | Relaxed or Acquire |
| Sequential | Relaxed, Acquire or Sequential |

Every other pair is rejected before a memory attempt. Failure applies its
selected read order; Sequential failure is also a full boundary. It has no
write or release publication. Default strong CAS uses Sequential success
and failure. Explicit weak CAS and its spurious-failure choices are not
selected by this chapter.

Checked arithmetic computes the mathematical result before committing a
write. Overflow after an actual read is read-only failure:

| Requested RMW order | Retained failure effects |
|---|---|
| Relaxed | Relaxed coherent read |
| Acquire | Acquire read |
| Release | Relaxed coherent read, no release |
| AcquireRelease | Acquire read, no release |
| Sequential | SC acquire read and full before/after boundary |

There is no update, `mo` entry, release of unstored bits or fabricated
previous/updated success result. The checked failure then follows ordinary
exceptional execution. It may have acquired publication through its actual
read source. Prior argument effects and that read's effects are not rolled
back. Failures in staging, preconditions, authority or target admission before
any attempt do not invent a read or boundary.

An explicitly selected wrapping variant changes the arithmetic computation,
not the ordering, authority or lifetime contract. A retry/helper lowering
must refine the source RMW and its failure effects; exposed helper writes
followed by an overflow check are not a no-update checked RMW. Result-free,
previous-value and updated-value forms retain the same declared effects.
Unused-result elimination is permitted only with those effects preserved or
proved irrelevant under the complete source contract.

## 7. Finite causal justification

Each candidate needs a finite grounded justification for the existence,
location/generation, operands, read/write bits and outcome of its events,
and for every branch, checked-failure and continuation guard on its actual
source path. A consistent final assignment of bits is not such a proof.

A justification is a finite derivation of facts under checked source
semantics. Each derived fact has a rule and previously established premises.
Permitted rules are:

| Rule | Required evidence |
|---|---|
| Ground input | A genuine supplied input, constant or established entry/initialization fact with its actual validity and dependencies |
| Source computation | Exact typed semantics applied to established operands, including checked arithmetic, representation, effects and outcomes |
| Source path | Established entry and actual branch/normal/failure guards; all relevant earlier outcome-dependent gates remain |
| Read source | The selected `rf` source exists, has the exact established location/generation and justified stored bits |
| RMW outcome | The attempted read and arguments justify success/update or read-only failure under section 6 |
| Semantic independence | The claimed fact is identical under **every** admissible substitution for the omitted unresolved inputs, with the same relevant validity, failure and observable-effect conditions |
| Protocol/lifecycle | The actual admitted endpoint and its prerequisites establish the claimed authority, initialization, quiescence or lifetime fact |

Equivalently, begin with justified ground facts and repeatedly add conclusions
whose premises are already justified. Every required fact must enter this
least fixed point. A finite derivation can be numbered so every premise has
a lower rank. No cyclic assumption, absent event or hypothetical write is
a ground fact. Dynamic initialization computed from concurrent reads is not
automatically a new seed merely because its storage is new.

Independence is over the valid input domain before choosing the observed
outcome. It cannot assume a favorable read value, guess an address, ignore
a possible overflow, treat NaN like an integer, or skip an effectful/failing
evaluation. Establishing constant integer `r ^ r` does not establish the
equivalent claim for arbitrary expressions or floating `r - r`.
If an omitted premise can change whether a store occurs or fails, the
independence rule cannot remove it.

An unconditional constant store after a valid nonthrowing relaxed load may
have its existence, address and bits justified without that load's returned
value. For initially zero `x` and `y`, this permits:

```text
Thread A                 Thread B
r0 = y.load(relaxed)     r1 = x.load(relaxed)
x.store(1, relaxed)      y.store(1, relaxed)
```

Both reads may return `1`. The stores' facts are independently justified;
the actual reads can then use those exact sources. This does not execute
source stores before preceding evaluations or promise that a target must
realize the outcome. Acquiring loads or releasing stores also retain their
required access-order edges, which reject the corresponding delivery cycle.

Replacing each unconditional store with `store(theReadValue)` cannot
self-justify an unseeded `1`. Neither can `if theReadValue == 1: store(1)`,
or a circularly selected address. A constant bit pattern does not excuse an
unjustified store guard. Genuine independent input may seed a derivation,
but equal bits from an unselected source cannot substitute for actual `rf`.

It would be too strong to require acyclicity of all `sb` and `rf`: that
rejects the independent-store example. It would be too weak to check only
written bits: that permits circular control/address justification. Source
path, the finite fact derivation and the required access order must all hold.
This is a semantic condition, not a requirement that every arbitrary program
receive an automatic compiler proof or a new trusted-implementation marker.

## 8. Initialization, retirement and reuse

Each access is within its actual live storage generation, with the required
value state, backing, roots, guard/lease and use/cleanup affinity. Creation
precedes its initialization, and the initialization needed for a use happens
before that use through source sequencing and actual publication.
Incomplete aggregate construction is not a valid shared payload.

An ordinary write, successful RMW or descriptor rebinding changes value state
within the existing live storage; it does not automatically create a new
coherent location or reset `mo`. Ending an object's storage lifetime and
reusing its bytes instead creates a distinct generation with its own
initialization and modification order. Referent generations and the lifetime
of a pointer/handle slot remain distinct.

Retirement requires actual protocol withdrawal/quiescence and discharge or
valid transfer of every retained access/backing obligation. Every completed
access to the retired generation happens before its retirement through
actual source/protocol endpoints. An outstanding permitted access or loan
cannot be hidden by ending a descriptor, dropping a handle or observing a
status bit. Reuse follows actual retirement; earlier aliases and tokens
do not gain access to the replacement generation.

This establishes ordering and logical validity, not a physical generation
tag, mandatory runtime check, automatic lease acquisition or reclamation
algorithm. Managed-reference atomics need independently admitted roots,
collector loads/stores/RMWs and write barriers. A GC barrier is not CPU
publication; CPU publication is not rooting. Atomic removal, matching
pointer bits, a full fence or a successful CAS supplies no reclamation proof.

## 9. Admission, transformation and conformance

Retained artifacts and independently replaceable boundaries preserve required
operation/representation/order identities, outcome effects, publication and
full-boundary promises, source dependencies and actual lifetime/protocol
requirements. An implementation cannot claim support without the required
facts and profiles. Logical selection allocates no opcode, feature number,
event table, certificate format or optional Attr meaning.

Optimization is refinement of the complete source contract. It can elide
value work, combine compatible accesses or use stronger instructions only
when it introduces no forbidden observation, fault, conflict, lifetime use
or effect. It cannot infer publication from equal bits, weaken unused RMWs,
speculate conditional stores or remove guards using the chosen outcome.
Compiler-owned helper accesses still need valid extents and compatible
protocols. Source contracts, not instruction counts, are the boundary.

Checked safe misuse is rejected under its owning authority/failure rules.
Explicit unsafe implementation responsibility assigns correctness to the
author/provider, not the consumer; it does not change the semantics of a valid
operation. Invalid access loses its promised access
guarantees; it does not license arbitrary whole-program data-race UB,
unrelated traps or invented unreachable code. This is not unsafe containment.

[Concurrent execution cases](../conformance/runtime-concurrent-executions.md)
separate relational observations and finite supplied-source justifications
from arbitrary algorithm/source proofs, target mappings, progress,
collectors and physical codecs. A finite assessor is evidence for its
stated instances, not completeness of all possible executions.

### Prior art and deliberate distinctions

The C++ draft's [coherence/SC boundary rules](https://eel.is/c++draft/atomics.order#3)
provide prior art for section 5's four projections (source revision
`c7015b485cc3db8efaa9dfb9ff0809c5394a4ed1`). Terrum requires ordinary `hb`
between SC participants, permits admitted ordinary carriers and has no
release-sequence/consume publication or whole-program data-race UB.

The [RC11 model](https://github.com/herd/herdtools7/blob/master/herd/libdir/rc11.cat)
is comparative evidence for `rf`/`mo`/`fr` and SC constraints, not an imported
Terrum model. In particular its blanket acyclic `sb`/`rf` rule is not
Terrum's grounded-justification rule. The [.NET runtime memory model](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md)
is prior art for access-based non-tearing and full-fence distinctions,
not a replacement definition of these allowed executions.
