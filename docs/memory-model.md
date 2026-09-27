# Memory model proposal

> **Design direction and proposals for review.** The default access model and
> primitive/trivial-wrapper atomicity baseline below record agreed direction.
> Remaining formal rules, APIs and target profiles are proposals, not an
> implemented language specification or a frozen ABI. Examples are pseudocode,
> not implemented Terrum syntax or library APIs; names in square brackets are
> provisional.

Call sequencing, reference equivalence, output publication, and proof effects
are covered by the companion [contract review](language-contract-review.md).
[Data layout and ABI contracts](data-layout-and-abi.md) separates required
primitive guarantees from physical layout, interchange and procedure calls.
[Implementation](implementation.md) remains the executable support boundary;
this document does not claim a concurrent runtime or these atomic APIs exist.

## Definitions

Hardware and language specifications do not use one universal vocabulary.
The vocabulary here starts from hardware access properties, not a particular
language's atomic types. A concrete hardware guarantee must identify the access
width, alignment, memory type, observer scope, and applicable target profile.
These terms describe independent dimensions; none is a synonym for
"thread-safe."

| Term used here | Meaning | Does not imply |
|---|---|---|
| Atomic load/store, or non-tearing access | One access is indivisible with respect to the other accesses and observers covered by its contract. A read does not assemble part of an old value and part of a new value from a conflicting write of the covered width. | Acquire/release ordering, freshness, an indivisible compound update, or permission to share the location. |
| Atomic read-modify-write (RMW) | The read and conditional or unconditional update form one indivisible operation relative to covered competing accesses. Exchange, compare-exchange, and fetch-add are examples. | A particular memory order, lock freedom, or atomicity of surrounding code. |
| Ordered access | A load/store with a specified ordering contract, such as acquire or release, in addition to whatever access-indivisibility guarantee applies. | Mutual exclusion or a globally most recent value. |
| Acquire / release | Acquire constrains following accesses from being observed before the acquire; release constrains preceding accesses from being observed after the release, within the specified scope. A matching communication relation can publish those preceding effects to the acquiring execution. | A full barrier in both directions, or publication merely because two unrelated operations carry these names. |
| Coherence | Constraints on the order in which modifications to one location may be observed. | An order between independent locations, or multicopy atomicity. |
| Multicopy atomicity | A propagation guarantee about a write becoming observable to the covered observers together rather than reaching different observers separately. The architecture must state the observer scope and whether the writer can observe its own store early. | Merely a non-tearing load/store, per-location coherence alone, or sequential consistency of an entire program. |
| Fence or barrier | A compiler barrier constrains compiler transformations; an architectural/CPU barrier constrains specified machine observations within a scope. A language operation may require both. | A cache flush, immediate observation by every core, durability, or an atomic multi-access transaction. |
| Full memory fence | Orders the covered reads and writes before the fence against the covered reads and writes after it. The contract must specify compiler effects, architectural scope, and how fences participate in communication. | A global SC order for all operations, mutual exclusion, or completion to persistent storage. |
| Mutual exclusion / lock | At most one participant holds the relevant exclusive access at a time. A synchronization lock also specifies ordering from release by one holder to acquisition by the next. | An indivisible representation for the protected object, or protection for accesses that bypass the protocol. |
| Interlocked operation | An operation explicitly interlocked against competing accesses, commonly an atomic RMW. A library family may also include non-tearing reads or fences; each operation states its indivisibility and ordering separately. | That every family member is an RMW, or that its name alone specifies a full fence or SC. |
| Sequential consistency (SC) | A specified set of operations admits one total order consistent with the required execution order. | A property of every non-tearing access, or a synonym for an unspecified "full barrier." |
| Publication | A defined synchronization relation makes prior initialization or writes available to another execution under the relevant access contract. | A wall-clock freshness deadline, lifetime extension by itself, or atomicity of the whole published payload. |
| Lifetime/reclamation | The period during which storage remains a valid object that may be accessed under its contract. | Mutual exclusion, safe publication, or an ordering edge merely because a pointer value is atomic. |
| Ordinary access | An access without a separately requested ordering or RMW operation. A checked sharing contract may still permit it concurrently under a specified protocol. | That it tears, is automatically illegal when shared, is fresh, or can be freely merged/speculated in every context. Ordering depends on the applicable access contract. |
| Volatile access | A language- or platform-specific contract that must be qualified below; the word alone does not select one. | A portable cross-language meaning or an automatic atomic RMW. |
| Readonly access | A restriction on mutation through one access path. | Immutability of reachable state or permission to share concurrently. |
| Immutable value/graph | A value or defined reachable extent that cannot be changed through any permitted alias for the promised lifetime. | Transferability, safe publication, or thread-safety of external state it references. |
| Transfer | A checked handoff of usable authority between execution contexts. | Concurrent use by the sender and receiver. |
| Sharing | A checked permission for concurrent aliases, governed by an access and lifetime protocol. | Correctness of every algorithm using those aliases. |
| Mutation authority (`mut`, `write`) | Permission to update a place or referent as allowed by its declaration and access path. | Cross-thread permission, synchronization, or ownership transfer. |

## Goals and scope

Terrum should give safe programs a portable, compiler-independent account of
communication between concurrent executions without making ordinary
application code depend on hardware-level concurrency details.

> Safe code cannot concurrently access memory unless its checked contract
> explicitly permits it. Ordinary code is nonconcurrent by default; mutation
> authority does not change that. Once concurrent access is permitted, access
> atomicity, ordering, publication, and lifetime guarantees govern what other
> threads may observe.

This model covers ordinary cacheable memory and CPU-to-CPU communication.
Device registers, DMA, persistent storage and side channels have distinct
observers and completion rules. A CPU fence is not a universal operation on
those domains.

The following access model records agreed direction. The later candidate
formalism and target discussion explain its consequences and possible
extensions. [Recommendations and decisions](#recommendations-and-decisions)
at the end isolate the choices that still need approval.

## From single-threaded code to shared memory

### 1. Nonconcurrent access is the default

Without an explicit cross-thread contract, memory is used within one execution
thread, not concurrently accessible to other threads. Readonly defaults
encourage state that does not change after initialization, but a readonly path
alone cannot prove that another permitted alias cannot change the value.
Even genuinely immutable state still needs an appropriate transfer or sharing
contract to cross the thread boundary.

This is why default safe code has no inter-thread access races: there is no
permitted competing thread accessing its storage. It is not because every
ordinary read and write secretly uses synchronization.

### 2. Mutation preserves single-threaded consistency

`mut` and `write` permit the corresponding updates, not concurrent access.
A scalar update or aggregate copy may use one instruction, several stores,
or no memory instruction at all when a value can stay in a register. With no
permitted concurrent observer, tearing between those machine operations is
not a source-level concern.

The implementation must still preserve all single-threaded observable
behavior: evaluation order, aliases, calls and callbacks, checked failures,
and cleanup. It cannot expose intermediate machine steps through one of those
observations unless the source contract permits that observation, including
any explicitly permitted partial completion on failure. Source sequencing is
the contract; physical instruction order is an implementation choice within it.

For example, an explicitly requested non-temporal store operation may leave
ordering or completion obligations to its caller. Its contract must specify
the fence or completion operation required before dependent accesses or
publication. If the compiler instead selects non-temporal stores to implement
an ordinary assignment or copy, those obligations remain the implementation's
responsibility; instruction selection cannot weaken the source contract.
See [non-temporal stores and cache management](#non-temporal-stores-and-cache-management).

### 3. Explicit sharing introduces observable access atomicity

Sharing admits competing observers under a contract covering the complete type
form, reachable authority and dependencies, not just an outer annotation.

For permitted shared CPU access to ordinary coherent cacheable memory, naturally
aligned primitive loads and stores up to the machine pointer size are atomic.
Trivial wrappers preserve their primitive carrier's atomicity, and managed
reference accesses are non-tearing. The compiler and runtime must provide the
required storage alignment and preserve these guarantees; they are not guesses
based on current instruction selection. Target profiles must document the
concrete representation and access requirements.

An ordinary access satisfies that guarantee without an `Atomic<T>` wrapper.
A trivial wrapper here has one primitive carrier and preserves its storage
and access contract; merely having a small total size is insufficient.
For example, `struct S { int8 x; int8 y; }` contains two independently
accessible fields. Its two-byte size does not promise an atomic whole-`S`
load or store, even if a particular backend sometimes uses one instruction.
Exact wrapper eligibility belongs in the [layout companion](data-layout-and-abi.md).

Nor is `x++` an atomic RMW: its update has the load/add/store behavior of
`x = x + 1`, even when each load and store is individually atomic. Atomic
access does not protect object lifetime, order independent locations, or
guarantee a fresh value. Those are separate contracts.

### 4. Synchronization adds ordering and compound guarantees

Ordered accesses and fences establish the specified inter-thread ordering and
publication relations. Atomic RMWs make one update indivisible; locks or
equivalent checked protocols can protect compound invariants. Their roles
are complementary, not interchangeable.

A writer can initialize a large payload with many ordinary stores, then
publish it through an appropriate release/acquire protocol. A reader that
observes that publication can use the complete payload without an atomic
whole-payload copy, provided its lifetime is valid and conflicting mutation
is excluded. A lock can instead protect a larger mutable payload: its
indivisibility to other participants comes from excluding their access during
the update, not from the payload's machine representation.

## Comparative evidence, not normative vocabulary

| Context | Atomicity and synchronization vocabulary | Meaning of `volatile` |
|---|---|---|
| C# language | The specification guarantees atomic ordinary reads/writes for listed types, including `int` and references, but not general compound updates. `Interlocked` supplies operations such as atomic increment and compare-exchange. | Volatile field reads have acquire semantics and writes have release semantics. This can support publication; it does not make `++` atomic or impose one global SC order. |
| .NET runtime | Its implementation contract separately specifies alignment and atomic accesses, including properly aligned primitive/enum accesses up to pointer size and atomic managed-reference accesses. `Volatile` and `Interlocked` APIs add their documented guarantees; `Interlocked` methods have full-fence semantics in this runtime contract. | C# field decoration causes the compiler to emit volatile accesses. The IL `volatile.` prefix supplies ordering, not an unconditional non-tearing guarantee for every width, alignment, or block operation. |
| C++ | `std::atomic` and `std::atomic_ref` are language/library concurrency contracts, not just promises that a machine load will not tear. Even relaxed operations have the specified atomic-access/coherence rules. A conflicting unsynchronized ordinary access can be a language data race despite a non-tearing hardware instruction. | The standard qualifier governs volatile evaluations and implementation-defined access details. It is not the C# acquire/release inter-thread synchronization contract. Target-specific extensions must be identified separately. |
| Rust | `Atomic*` operations follow the documented C++20-derived ordering model, translated to an access-based model. Rust's permitted access mixtures are not identical to C++ atomic-object rules. | `read_volatile`/`write_volatile` constrain observable accesses, principally for I/O. They do not provide atomic synchronization between threads. |
| ISA/hardware | Manuals distinguish indivisible accesses, atomic RMW instructions, propagation/coherence properties, and ordering instructions or attributes. Ordinary loads/stores may already be indivisible for specified widths, alignment, memory types, and observers. | A processor does not implement a universal language-level `volatile` qualifier. It executes the instructions and memory attributes selected by the implementation. |

Qualify cross-language uses of `volatile`. None means "always read the latest
value" or universally "bypass the CPU cache." The C# language minimum, CLI
rules and .NET runtime contract are distinct specifications. This comparison
does not select any of them as Terrum's foundation.

## Sharing capabilities are distinct

The terms below describe separate permissions. `[Send]` and `[Sync]` are
provisional labels already used in design discussion, not selected syntax.

| Capability | Proposed meaning | Does not imply |
|---|---|---|
| Transferable (`[Send]`) | A value and its live dependencies may cross an execution-thread boundary under a checked transfer contract. Transfer must account for every still-usable authority to mutable state. | Concurrent sharing, deep immutability, or thread affinity. |
| Shareable (`[Sync]`) | The type's exposed operations are safe when aliases are used concurrently under the memory model. The proof covers reachable authority and generic arguments, not just the outer object's fields. | Exclusive access, immutability, or freedom from logical races/reentrancy. |
| Thread-affine | A value may be used only on its designated thread or executor. This is a separate capability/restriction, not a synonym for `Send` or `Sync`. | Transferability or concurrent sharing. |
| Readonly | This access path cannot mutate the immediate place it denotes, as defined by the access model. | Deep readonly, immutable reachable objects, race freedom, or safe publication. |

Ordinary `List<T>` is not concurrently shareable by default. An opted-in
synchronized implementation must account for every shared operation and
exposed value, callback, iterator and reference. Its internal ordinary accesses
receive the protocol's guarantees, not an implicit RMW contract. Merely adding
a readonly handle or a lock-shaped wrapper is insufficient.

The proposed checker has these obligations:

- Verify source implementations, reachable authority and synchronization
  protocols. Credit supported primitives and protocol forms, not a declaration
  claiming an unsupported custom algorithm is safe.
- For generic types, the proof is conditional on the complete arguments. A
  `ConcurrentBox<T>` cannot be shareable for arbitrary `T` if it exposes
  unsynchronized access to `T`.
- For opaque imported types, the provider publishes a versioned contract that
  identifies its checked proof or explicitly trusted implementation boundary.
  Consumers validate their use of that contract; a native implementation is
  not thereby machine-checked.
- Preserve authority and lifetime restrictions through wrappers and projections.

Sending a mutable handle while retaining an independently usable alias does
not transfer exclusive authority. A temporary handoff may retain an alias
only if its unusability and the return of authority are checked.

Readonly is shallow: `List<write C>` retains writable object handles even when
its slots are readonly. A sharing proof must account for those capabilities.
Similarly, a write guard is a synchronization mechanism only if its ordering,
reentrancy and lifetime behavior establish that contract.

A synchronized wrapper can be shareable without making its contained list
independently shareable: encapsulate the authority, serialize access, and
prevent protected references from escaping, including through callbacks.
Moving a value out and sharing a borrow impose different generic requirements.
Requiring every private field to be shareable would incorrectly reject this
encapsulation pattern.

## Candidate event formalism

This proposed access-based formalism covers locations with one specified
width and compatible non-tearing accesses, whether ordinary or explicitly
ordered under their contract. It neither requires dedicated atomic objects
nor defines incompatible overlapping-width accesses. Adoption and extensions
are tracked in the [approval checklist](#approval-checklist).

- A dynamic memory event identifies an execution, a storage object and byte
  range, an operation (read, write, or read-modify-write), and the access and
  sharing contracts that govern it, including width, indivisibility, and
  ordering guarantees. Object creation, initialization, destruction, and
  reclamation also affect the validity of accesses.
- **Sequenced-before** (`sb`) orders evaluations and memory events in one
  execution according to the source program's abstract sequencing rules.
  Reordering machine instructions is allowed only when it preserves the
  language's observable behavior.
- **Synchronizes-with** (`sw`) is created by specified communication
  operations. This candidate includes a release operation on a location
  synchronizing with an acquire operation on that same location when the acquire
  reads the value written by the release. A relaxed operation does not create
  this edge. Other synchronization primitives must explicitly define their
  acquire/release edges.
- **Happens-before** (`hb`) is the transitive closure of `sb` and `sw`, plus
  any separately specified thread-start, thread-join, lock, or task-completion
  synchronization edges. A specified thread-start operation can publish
  arguments, and a specified join can publish completion effects. Queuing work
  through an unspecified mechanism, sharing pointer bits, cancelling work, or
  observing that a cache line changed does not supply such a contract.
- Each location covered by these coherence rules has a **modification order**
  (`mo`): a total order of the covered modifications, consistent with `sb` for
  modifications by one execution. It is per location, not a global order.
- A covered non-tearing read has a **read-from** (`rf`) source: one modification
  of that same location, or its initial value. The allowed source is constrained
  by modification order, happens-before, and atomic coherence. An atomic
  read-modify-write is one indivisible read and write event; it cannot read
  from an arbitrary older modification and then overwrite a later one.
- For other reads, the source-level value and any permitted tearing are
  constrained by the ordinary-access baseline and the checked sharing
  protocol. This candidate does not yet define a universal read-from relation
  for ordinary accesses.

Where the protocol excludes competing writes, a read observes the last write
to that location in the established happens-before order. With permitted
competing writes, happens-before alone does not select one value; the location's
coherence and access rules constrain the allowed observations. This proposal
does not make ordinary memory globally sequentially consistent across threads.

### Conflicting accesses and safe/unsafe boundaries

Two events conflict when they access overlapping bytes of the same live
storage and at least one is a write. A conflict is not automatically an error:
its validity depends on the sharing, ordering and lifetime contracts. An
**access-contract violation** occurs when an access or combination of accesses
falls outside those permissions. Non-tearing ordinary accesses are not
automatically illegal, and an explicit atomic access does not by itself
authorize a conflicting ordinary access. A checked protocol may permit
unordered non-tearing accesses and define their observations. "Data race" in a
comparison with another language retains that language's definition, not an
implied Terrum undefined-behavior rule.
Accesses overlapping initialization, destruction, or reclamation must also
obey the object's lifetime contract. Read/read overlap alone is not a conflict.

Unsafe code, native code, and foreign devices can violate checked access
contracts. This candidate does not import C++'s rule that an ordinary data race
makes the program undefined. Its proposed value guarantees do not validate a
dangling pointer, destroyed object, or out-of-bounds access.

Bounded access semantics are not fault containment: a raced value can affect
control flow, indexes and subsequent writes beyond the original location.
Corrupting a pointer, lifetime or runtime metadata can invalidate continued
execution. The [unsafe-boundary recommendation](#unsafe-boundaries) separates
these consequences from granting the optimizer arbitrary behavior merely
because unauthorized concurrent access occurred.

No-out-of-thin-air rule: a non-tearing shared read's value must be justified by
an initial value or a permitted write event whose value is computed from
already justified inputs. Where tearing is permitted, each observed portion
must have a justified source under the applicable access rules; the combined
value need not have been written by one whole-value store.
A compiler or runtime must not invent cyclic speculative values merely because
they make an otherwise invalid access self-consistent. This rule does not
promise a particular value for an access outside its contract or turn a
potentially torn ordinary read into an indivisible observation. Constraints
on torn representations belong to the remaining unsafe-access decision.

Justification must include the control conditions that cause a write to occur,
not merely its stored bit pattern. Speculating a conditional store cannot make
the assumed read that enabled it justify itself. This is a causality condition,
not a requirement that every reads-from edge follow wall-clock observation
order; ordinary permitted weak-memory reorderings remain possible.

### Atomic coherence

For locations covered by the candidate coherence rules:

- Atomic modifications have one `mo`; two modifications ordered by `hb` must
  appear in that order in `mo`.
- A read cannot observe a modification older than a modification that happens
  before the read.
- If an atomic read happens before a later modification, the read's `rf`
  source must precede that modification in `mo`.
- If one read happens before another read of the same location, the
  second cannot read from an older `mo` source. This includes `hb` across
  executions, not just two reads in one execution.
- A read-modify-write reads the modification immediately preceding its own
  write in `mo`; there is no intervening modification to that location.

The intent is a compact, auditable coherence model, not accidental inheritance
of C++'s release-sequence, dependency-consume and fence rules.

Within the explicit-order candidate, one option is direct release-to-acquire
communication only:
a relaxed RMW does not implicitly relay an earlier release's publication.
An acquire-release RMW that reads a release can relay effects through explicit
acquire, sequencing, and release edges. This is a deliberate conservative
difference from importing a release-sequence rule. No dependency-only
`consume` ordering is proposed.

## Candidate explicit-order surface

The table and litmus outcomes describe a candidate explicit-order model,
independent of eventual API spelling. Named operations or per-operation order
arguments can express the same contracts. The
[default-access recommendation](#shared-access-default) selects among these
strengths; it does not change baseline indivisibility or require a dedicated
atomic object.

| Ordering | Read effect | Write effect | Inter-thread publication |
|---|---|---|---|
| Relaxed | Atomic, coherent read from an allowed `mo` source | Atomic modification in `mo` | None by itself |
| Acquire | Following operations retain acquire ordering; creates `sw` only when its `rf` source is a release on the same object | Invalid for a store-only operation | Imports prior effects only through that communication edge |
| Release | Invalid for a load-only operation | Prior operations are ordered before the release | Exports prior effects to a matching acquire |
| Acq-rel | Acquire semantics on the read side and release semantics on the write side of one read-modify-write | Same | Communicates through the object's `rf`/`mo` relation |
| Sequentially consistent | Acquire behavior where applicable | Release behavior where applicable | Participates in a single total order of SC operations consistent with `sb` and `hb`; not a promise that relaxed operations are SC |

The SC order must agree with the modification order of each atomic object for
SC modifications. In the all-SC case, each load reads the last modification
of that object preceding it in the SC order, or the initialized value if no
such modification precedes it. These rules constrain the all-SC litmus tests
below. This is not a complete formal definition for mixtures of SC and non-SC
modifications. In particular, relaxed atomics remain relaxed even in a program
that uses SC operations elsewhere.

A failed compare-exchange is a read, not a release of a value it did not store.
Its failure ordering must therefore be a supported load ordering, never release
or acquire-release. The API must specify both success and failure ordering and
whether spurious failure is possible. A successful weak compare-exchange still
provides its full atomicity/order contract. Failure ordering and spurious
failure are distinct properties; proposed defaults appear under
[Stronger operations and explicit fences](#stronger-operations-and-explicit-fences).

### Publication example

Proposal pseudocode:

```text
payload = 42                  // ordinary write
ready.store(true, release)

if ready.load(acquire):       // if it reads the release's true
    use(payload)              // sees the preceding write
```

Assume the checked sharing contract permits the reader and writer to access
this object and its lifetime spans both operations.
The payload is published only if the acquire reads from the release (or a
precisely specified release RMW). If the acquire reads an older `false`, or a
relaxed store, the required `sw` edge does not exist. If `payload` can also be
mutated concurrently, its separate access protocol must still be valid.

### Relaxed store-buffering example

Proposal pseudocode, initial `x = y = 0`, all operations relaxed:

```text
Thread A                    Thread B
x.store(1, relaxed)          y.store(1, relaxed)
r0 = y.load(relaxed)         r1 = x.load(relaxed)
```

The result `r0 == 0 && r1 == 0` is permitted. Each atomic access is indivisible
and coherent for its own object, but there is no cross-object synchronization.
Making just the two loads acquire is not enough to forbid this outcome when
neither load reads from a release operation that publishes the other store.

If all four operations are sequentially consistent, `r0 == 0 && r1 == 0` is
forbidden: it would require each store to follow the opposite load in one
total order while each store precedes its own thread's load, forming a cycle.

### Independent-readers example (IRIW)

Proposal pseudocode, initial `x = y = 0`, stores release and loads acquire:

```text
Thread A       Thread B       Thread C                       Thread D
x = 1 release  y = 1 release  r0 = x acquire; r1 = y acquire  r2 = y acquire; r3 = x acquire
```

If `r0 == 1, r1 == 0, r2 == 1, r3 == 0`, each reader has acquired one
release, but the independent per-object communications do not order the two
reader executions with each other. The proposal permits this result for
release/acquire operations. An all-SC version must forbid it because the
single SC order cannot place both readers' first observed store before their
second observed initial value. In that version, both stores and all four loads
are SC. This permissive acquire/release result is a choice
of the candidate model, not a conclusion that C#/.NET or every target mapping
admits it. Stronger target behavior does not invalidate the weaker portable
contract.

### Fences, `volatile`, and interlocked operations

A full fence orders covered accesses on both sides; that alone does not
define SC participation or an `sw` edge. A formal fence model must cover its
interaction with ordinary accesses, ordered accesses, RMWs and communication.
The .NET full-fence contract and C++ `atomic_thread_fence` are not interchangeable
definitions. The
[strong-operation recommendation](#stronger-operations-and-explicit-fences)
deliberately asks for both SC and full-fence obligations, not one inferred
from the other.

Acquire/release shared-RAM access, possibly spelled `volatile`, can publish
payload through the communication relation above. Device accesses instead
obey their width, side-effect and ordering contracts. Neither "volatile is
not atomic" nor "volatile cannot publish" is a useful unqualified rule.

## Access widths, representation, and progress guarantees

Atomicity is a language contract, not a guess from natural alignment. Each
target profile must publish a machine-readable capability table covering:

- supported non-tearing ordinary accesses, ordered loads/stores, and atomic
  RMW types and widths, listed separately;
- required address alignment and storage representation;
- whether each operation is lock-free, wait-free, or may use a runtime lock;
- process and shared-memory scope, if any;
- availability on each CPU/OS feature profile and behavior when the required
  feature is absent;
- managed-reference/root-barrier support, if reference atomics are offered.

The [agreed baseline](#3-explicit-sharing-introduces-observable-access-atomicity)
is a requirement on these profiles, not an optional capability. In particular,
the implementation supplies aligned atomic `int32` loads/stores on intended
32-/64-bit targets. Interoperability with foreign bytewise writers requires
a separate compatible-access contract.

### Unaligned access

Unaligned reads and writes must be explicit and have a target-defined
contract. Some hardware supports non-tearing unaligned accesses within a
cache line but not across a line boundary; other hardware faults or requires
decomposition. Neither behavior is a portable guarantee. A profile must state
the exact width, alignment, memory type and boundary conditions it supports,
including any page-boundary restrictions.

A checked unaligned helper can assemble a value from smaller valid accesses
on a target that cannot load it directly. That provides an unaligned value
access, not an atomic snapshot. An API promising an atomic unaligned access
must meet its stronger target conditions or reject the operation. Ordinary
aligned storage must not silently become unaligned through layout decisions.

### Overlap, representation access, and unions

Creating overlapping typed views or reinterpreting live storage is an unsafe
boundary unless a specific checked abstraction establishes its validity.
This is not a prohibition on ordinary checked aliases to the same object.
The hazards include incompatible aliasing and mutation authority,
initialization, lifetime, representation validity, and concurrent accesses of
different widths. Atomicity of one access does not validate the others.

Prefer explicit value-level helpers for supported representation operations:
for example, obtaining the backing `int32` bits of a `float32`, or constructing
the corresponding floating value from valid bits. Such a helper transfers a
bit pattern; it does not create a second typed reference to the same storage.
Its contract must distinguish bit preservation, including signed zero and NaN
encodings, from numeric conversion. It must not become a general way to forge
references, ownership or constrained wrapper values.

Equal layouts or matching fields alone do not make overlapping unions safe.
A checked rule would also need valid active-member access, initialization,
representation, aliasing and lifetime. C and C++ common-member or union-punning
rules are not implicitly imported.

The compiler may use overlapping machine accesses, wider instructions, or
register reinterpretation internally when it preserves the source contract.
It may not introduce observable tearing, invalidate an alias, touch storage
outside its permission, or overwrite an independently accessible neighbor.
Unsafe source-level reinterpretation and a proven internal lowering are
different permissions.

### Wider atomics and progress

An offered atomic operation must meet its value/order contract on the selected
profile or be rejected. If a runtime lock is permitted, advertise the fallback
and reject it in contexts requiring lock freedom or incompatible reentrancy,
such as signal/interrupt handlers.

All accesses to a lock-backed atomic must use the same synchronization protocol,
including across modules. Mixing a runtime-lock operation with a foreign native
atomic on the same bytes is not automatically interoperable. Runtime dispatch
and helper selection must preserve the declared representation and coordination,
not silently introduce different per-module locks or change an exported layout.

Ordering also does not promise scheduler fairness, bounded latency, or eventual
success of an arbitrary polling loop. Such liveness/progress guarantees need
their own primitive/runtime contract. A lock-free primitive does not make every
algorithm built from it wait-free.

Do not split a promised indivisible operation into observable parts or combine
incompatible overlapping protocols. Replacing an object starts a new lifetime
and modification order; old readers do not gain permission to access the
replacement. Initialization must complete and be published before shared use.

`compareExchange` and other read-modify-write operations do not solve object
reclamation. A pointer can be atomically removed while a reader still uses its
pointee; ABA can make a location return to the same bit pattern after
intervening changes. Safe reclamation needs an independently checked
ownership/epoch/hazard/lease protocol. Atomic pointer bits do not keep a
managed object rooted or a native allocation alive.

Managed-reference operations additionally need collector-aware loads/stores/RMWs,
exact roots across safepoints, write barriers and publication. A GC barrier
does not supply memory ordering, and acquire/release does not replace a GC
barrier. Native and interior pointers need their own reclamation contracts.
Reference indivisibility alone satisfies none of these lifetime obligations.

## Architectural and microarchitectural boundaries

A target profile maps the language contract to verified ISA/runtime sequences.
The following is implementer guidance, not a frozen instruction-selection table.
It applies only to the stated memory types and observers, not every address
space or device on a processor.

| Family | Informative profile concern | Requirement on a Terrum profile |
|---|---|---|
| x86/x64 | The commonly documented TSO model still permits store-buffering outcomes for independent ordinary operations; it is not a source-language substitute for release/acquire or SC. Streaming/non-temporal stores and write-combining have ordering details distinct from ordinary cacheable stores. | Use the Intel or AMD manuals applicable to the exact ISA/vendor profile; document the required ordering around non-temporal operations and atomic RMWs. Do not publish cache-line size or lock-free widths as universal language constants. |
| Arm64 | The architecture supports weaker ordering than x86; acquire/release instructions and barriers have specific scope and memory-type rules. | Name the ISA and feature profile; distinguish normal memory from device memory and use the architecture-defined barrier scopes. Verify mappings against the Arm architecture guide/manual. |
| RISC-V | RVWMO defines a formal weak memory model. Atomic extensions, `aq`/`rl`, alignment, and optional execution-environment properties affect the available mappings. Vector extensions have their own memory-operation details; do not infer that a vector access is one atomic event. | Require a named base/extension profile and verify supported atomics, alignment, and ordering against the selected ISA revision and platform attributes. |
| LoongArch | The ISA and optional extensions evolve independently; LSX and LASX are optional vector extensions. Barrier and atomic behavior must be taken from the selected manual/profile rather than inferred from an extension name. | Identify the exact ISA/manual revision and CPU feature set; prove atomic width, ordering, and vector-memory guarantees before exposing them. |

More specifically:

- Arm's memory-type and shareability distinctions matter independently of the
  word "barrier": an inner-shareable barrier is not a system-wide barrier.
  The readable Arm reference below explains these concepts for Armv7-A.
  An Arm64 mapping must use the corresponding Armv8-A rules; this proposal
  makes no unverified claim about multicopy-atomicity changes between Armv8
  revisions or a particular acquire instruction's strength.
- In the RISC-V A extension, `aq`/`rl` constrain the memory or I/O address
  domain selected by the atomic instruction; ordering across domains needs
  the appropriate `FENCE`. Optional Ztso strengthens RVWMO and is not a default
  assumption. The cited 20260120 RVWMO revision notes that the full vector
  memory model is not yet formalized there; V and Ztso contain additional
  instruction/element ordering rules. A backend must not extrapolate a scalar
  atomic guarantee to a vector operation.
- LoongArch Volume 1 calls its model Weak Consistency and distinguishes
  Coherent Cached, Strongly-ordered UnCached, and Weakly-ordered UnCached access.
  Coherent Cached does not imply sequential consistency. Its `DBAR 0` orders
  completion of earlier loads/stores before later accesses; among the listed
  AM instructions, `_DB` variants additionally provide documented data-barrier
  behavior. That architectural completion is not a durability guarantee.

Store buffers, speculation, forwarding and coherence transactions explain
implementation costs, not Terrum's event relations. For example, forwarding
can satisfy a local load before other cores see the prior store.

Cache-line size, coherence granularity, page size, and page permissions are
target/platform properties, not language constants. False sharing can make
independent fields contend at a coherence granularity without creating a
language-level data race. A padding/alignment facility must query or declare
its target requirement rather than bake in “64-byte cache lines” or “4 KiB
pages.” A page boundary, guard page, or faulting access does not create
atomicity; whether a misaligned/wide access faults or is decomposed is
platform-specific and must be specified by the profile.

Speculation and architectural memory ordering are related but not the same
security model. A race-free program is not thereby constant-time or protected
from cache, branch, or transient-execution side channels. Bounds, page faults,
speculation barriers, and data-oblivious algorithms require separate
contracts; this model does not claim to solve them.

## SIMD and memory operations

SIMD arithmetic on register values does not itself create shared-memory
events. A vector load/store may be one or several machine accesses, and a
vector width larger than a target's natural access width is not automatically
atomic. The language contract must classify each memory operation by its
specified access range and atomicity rather than by the chosen instruction.

Proposal:

- Ordinary vector loads and stores have no portable whole-vector non-tearing
  guarantee unless the language or target profile explicitly provides one.
  Per-lane indivisibility is not whole-vector snapshot atomicity.
- A vector memory operation that overlaps a concurrent write is governed by
  the same byte-range conflict and checked-contract rules as scalar
  operations. A compiler may not claim atomic snapshot semantics merely
  because it selected one vector instruction.
- Masked-off lanes must perform no memory access, including no architectural
  fault, only if the source operation explicitly promises that property and
  the target lowering preserves it. Otherwise the API must state its lane
  access behavior; it cannot be inferred from a mask's visual meaning.
- Tail handling must not access beyond the declared range. Inactive lanes do
  not grant bounds authority over neighboring objects.
- Packed atomic masks or vector RMW operations require a target-profile
  atomicity guarantee for the whole declared location. Independent scalar
  atomics are not a substitute for whole-vector atomicity.

SIMD alignment, lane order, and instruction availability belong to ABI and
target-feature profiles. They do not change the abstract race or publication
rules.

Gather/scatter operations describe multiple accessed ranges, not an atomic
snapshot of all lanes. An API must specify the behavior of duplicate scatter
addresses and any guaranteed lane ordering. A faulting multi-access operation
can partially complete unless its source contract explicitly promises otherwise
and the implementation can preserve that promise. Neither a vector instruction
nor a mask implies transactional rollback.

The same constraint applies to compiler-created vectorization, scalar widening,
and packed-field updates. Two source fields can be independently accessible
even when they share a cache line. Lowering must not invent a wider write that
overwrites a neighboring field or creates a conflict the source did not have.
Extra reads likewise require valid access and must not introduce observable
faults, device transactions, or invalid concurrent accesses. Padding and masked
lanes are not blanket permission to touch adjacent storage.

## Non-temporal stores and cache management

Non-temporal/streaming stores are an implementation or explicit low-level
operation, not a stronger form of ordinary release. A profile that exposes
them must define:

- the memory types and alignment/range restrictions they accept;
- whether and when the data becomes visible to coherent CPU agents;
- the fence or completion operation required before publishing a flag, handing
  the buffer to another CPU, or returning from the operation;
- how they interact with ordinary cached accesses to the same range; and
- whether the operation is unavailable on a target.

For example, Intel documents store ordering requirements for non-temporal
stores and `SFENCE`; the x86 backend must follow the precise Intel/AMD manual
and memory-type rules for its target. This is not a claim that one fence is a
universal cache flush or that a language-level release store can be omitted.
After streaming writes, a source-level publication still needs a documented
communication edge and the backend must implement both the streaming-store
completion and CPU-to-CPU ordering parts.

General cache flush/invalidate intrinsics should remain unsafe, target-specific
operations with explicit address, length, memory-type, and completion
contracts. Flushing a cache line does not by itself synchronize a racing
program, make a pointer live, or persist data to storage.

## Device memory, DMA, and persistence

### MMIO and device memory

MMIO registers are not ordinary RAM. Device accesses can have width,
side-effect, ordering, and repetition constraints that differ from CPU loads
and stores. A device API must specify permitted access sizes, whether accesses
may be combined/repeated/speculated, and which platform I/O barriers order
them relative to other device or normal-memory operations. Normal CPU atomics
and CPU-to-CPU fences do not automatically order device accesses.

The proposed boundary is an explicit unsafe/platform-checked device mapping
with typed register operations. It must not be exposed as a regular `ref<T>`
or an ordinary `volatile T` field. A device-specific barrier is not a cache
flush and does not imply DMA coherence.

### DMA

DMA involves CPU virtual addresses, CPU physical storage, and device bus
addresses; an IOMMU or host bridge may translate among them. A raw pointer is
not automatically a valid device address. A safe DMA abstraction must own the
mapping and describe direction, lifetime, completion, cache-coherency mode,
and synchronization between CPU and device.

Coherent DMA means the platform provides a coherence relationship for the
defined mapping; it does not remove descriptor ordering, ownership transfer,
or completion rules. Noncoherent DMA may require explicit map/sync/cache
maintenance. CPU barriers and device-specific DMA barriers are distinct, and
neither can replace the platform's DMA API. Buffers must account for cache-line
sharing with CPU-written data on platforms where maintenance operates at
coarser granularity.

Foreign code and devices are outside the checked CPU memory model unless
wrapped by a verified platform contract. A checked CPU loan, a readonly view,
or a CPU release store does not prove a device has stopped
using a DMA buffer.

### Persistence and crash consistency

Visibility to another CPU is not durability. A memory-ordering fence does not
promise that data has reached persistent media, and cache-line writeback does
not by itself define an application transaction. Persistent-memory or storage
APIs must specify flush, ordering, commit points, failure atomicity, and
recovery behavior for a concrete platform and medium. Abrupt process termination,
power loss, and fail-fast execution promise no cleanup; durable protocols
must tolerate abrupt restart rather than rely on destructors or `finally`.

## Compiler, runtime, module, and FFI obligations

Any eventual implementation must preserve the source contract in every
compiler path:

- Frontends and semantic verification check complete-form transfer/share
  capabilities, generic conditions, loans, object lifetimes, and atomic
  operations. Optimization cannot invent `Send`/`Sync`, remove synchronization,
  or rely on a debug-only check.
- AOT and JIT use equivalent event semantics and target-profile validation.
  Instruction selection is permitted to be stronger than required but not
  weaker; optimization levels cannot change the source model's allowed
  outcomes. Actual target executions may realize a stricter subset; portable
  code cannot rely on observing every outcome the abstract model permits.
- Persisted module contracts record the capability proof/refinement,
  generic conditions, atomic type/layout/order requirements, runtime helper
  requirements, and target-profile dependencies. Consumers reject missing or
  incompatible contracts before execution rather than trust a nominal marker.
- A runtime lock fallback, if permitted for an atomic type, has an explicit
  progress/reentrancy contract and is compatible with cancellation, cleanup,
  and signal restrictions. Collector barriers and safe-point behavior remain
  independent obligations.
- FFI declarations identify which memory is ordinary, shared, device, or DMA
  memory and who owns, synchronizes, and releases it. Calls into unverified
  native code that retain pointers or create threads cross an unsafe boundary;
  their declared contract must not silently upgrade safe capabilities.
- Unsupported target operations fail during compilation/loading or require an
  explicit unsafe target-specific path. No profile may silently degrade an
  atomic operation into a torn access.

A safe wrapper around a native implementation requires an explicitly identified
trust boundary and a defensible contract. Consumers can check its use of that
contract, not prove arbitrary foreign machine code correct. For a trusted unsafe
implementation, obligations and provenance are distinct from machine-checked
proofs; a nominal attribute cannot disguise that trust. Checking a caller
against an admitted trusted contract does not itself require unsafe callers.

Cross-language synchronization additionally needs compatible atomic object
layout, supported orders, lifetime/reclamation, and helper/progress protocols.
A matching function calling convention alone does not establish those facts.

## Recommendations and decisions

Primitive storage, alignment and wrapper eligibility belong in the
[layout and ABI companion](data-layout-and-abi.md). Single-threaded optimization
already has its rule: preserve observable source behavior. Those are not
unresolved concurrency API choices.

The remaining decisions concern explicitly concurrent access. The following
recommendations are for review, not newly accepted language rules.

### Shared-access default

**Recommend acquire loads and release stores by default for directly shared
mutable locations, with explicit relaxed overrides.** This is the shared-RAM
ordering often called volatile; choose the contract before its spelling.
It supports flag-based publication without an extra ordering annotation on
every access.

This default belongs to the checked concurrent-access path, not recursively
to every field of a shareable object. A lock-protected payload uses ordinary
accesses during the exclusive guard. An immutable published payload does not
need repeated acquires on every field. A directly shared multi-field mutable
value needs a checked protocol, not per-field volatility masquerading as an
atomic snapshot. References and API boundaries must preserve the access
contract rather than erase it to an unrestricted ordinary reference.

Relaxed accesses remain non-tearing and coherent for supported locations;
they are not thread-local accesses. For example, a statistics counter can use
an explicitly relaxed RMW when its observation publishes no unrelated payload.
A relaxed load/add/store can still lose increments.

Relaxed-by-default sharing is the alternative. It avoids ordering costs unless
requested, but a simple flag does not publish preceding data unless every
relevant access selects the stronger operation. Acquire/release is the
recommended balance because sharing is already an explicit, narrow opt-in.
Unshared code retains optimization freedom, and performance-sensitive shared
algorithms can opt into weaker ordering. This is a design tradeoff, not a
measurement claiming acquire/release is free.

### Shared-access observations and optimization

Specify portable per-location coherence and explicit publication, not inherited
x86-oriented or .NET ordinary-access behavior. Require a causally justified
write or initial value as the source of a non-tearing read. Use explicit
release/acquire communication in the initial profile, without dependency-only
publication or implicit publication relay through a relaxed RMW. An acq-rel RMW
can explicitly acquire and republish. Validate each lowering against its target
memory model.

**Recommend preserving potentially competing source accesses and their
declared ordering.** Do not invent stores, duplicate loads, coalesce distinct
shared accesses, or cache a polling load across iterations merely because
doing so preserves single-threaded behavior. Apply this to explicit relaxed
accesses too. Elimination or reordering requires proof under the concurrent
contract, not just local value equality. Proven confinement, exclusive
ownership, a held guard, or immutable publication can justify cheaper lowering.
Further transformation rules need formal justification, not a historical
architecture assumption.

### Supported sharing protocols

**Recommend checked handoff, immutable publication, scoped exclusive guards,
and direct shared scalar/reference operations as the initial composable set.**
Their proof obligations are described under
[sharing capabilities](#sharing-capabilities-are-distinct). Defer general
managed-reference APIs until collector-aware rooting, barriers and reclamation
are specified; the family recommendation is not permission to ship bare
atomic pointer operations as safe managed-reference operations.

These protocols establish permitted access and memory safety, not arbitrary
algorithmic correctness. Custom lock-free algorithms compose supported
operations. Unsupported aliasing or lifetime mechanisms require an explicit
trusted unsafe implementation boundary; a safe interface must not imply the
compiler proved that implementation.

### Stronger operations and explicit fences

**Recommend default SC interlocked RMWs with full before/after ordering of
covered memory accesses, plus explicitly weaker variants.** State both
obligations: SC among selected operations and a full fence are not synonyms.
Default strong compare-exchange has no spurious failure; its failure is an SC
read with full before/after ordering, not a release of a value it did not store.
Locks acquire on successful entry and release on exit. Expose a full fence
with explicit scope and communication rules;
initially defer weaker standalone fences until a concrete supported use
requires them.

Do not make ordered operations illegal merely because a particular caller
currently has exclusive access. Generic and low-level code may use the same
operation in either context under the same checked requirements. The concurrent
optimization rules above govern elimination of redundant ordering.

### Unsafe boundaries

**Recommend preserving guarantees whose preconditions still hold.** A valid,
aligned primitive location retains non-tearing access even if an unsafe caller
violates the sharing discipline. Supply no undeclared publication edge, but
do not turn that violation into permission for arbitrary whole-program
behavior. Dangling pointers, invalid representations, out-of-bounds accesses
and corrupted runtime state are separate violations; this is not recovery or
fault containment. Exact optimizer treatment across unsafe boundaries remains
to specify.

### Approval checklist

The substantive choices are:

| Area | Decision still needed |
|---|---|
| Shared defaults | Approve the acquire/release recommendation, or choose relaxed-by-default shared access. |
| Concurrent observations | Approve the conservative transformation rules and direct-release publication model, including no implicit relaxed-RMW relay. |
| Synchronization surface | Approve strong interlocked defaults, explicit weaker variants, a full fence, and initially no weaker standalone fences. |
| Checked and trusted protocols | Approve the initial protocol set and admission of explicitly trusted unsafe implementations; choose their proof/capability representation. |
| Unsafe semantics | Approve the bounded access-guarantee direction rather than whole-program race UB, then define surviving optimizer and representation guarantees precisely. |
| Wider-operation availability | Prefer advertised lock fallbacks where permitted, or require lock-free support and reject unsupported profiles. |

After those choices, specification work remains:

- Formalize mixed SC/non-SC and fence interactions, initialization/lifetime
  boundaries, allowed compare-exchange ordering pairs and bit/value comparison.
  Derive executable litmus cases and target mappings from those rules.
- Choose access/ordering syntax, guard forms, generic capability proofs and
  versioned imported-contract validation. Decide whether verified immutable
  types receive derived sharing capabilities.
- Define collector-aware reference operations and reclamation before enabling
  their safe concurrent APIs.
- Complete target capability tables and the layout companion's wrapper,
  unaligned-access and representation-helper rules. Any checked union subset
  needs separate approval; none follows from equal layouts.
- Specify SIMD/non-temporal APIs and the platform ownership of device, DMA
  and persistence contracts when those facilities enter implementation scope.

## Primary references consulted

Language/runtime references are comparisons, ISA references constrain target
lowering, and Linux references describe platform protocols. None automatically
defines Terrum semantics.

- [.NET runtime memory model](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md) — checked for its distinctions among atomic accesses, ordering, volatile, unmanaged memory, and runtime-specific guarantees.
- [C# atomicity of variable references](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables#96-atomicity-of-variable-references) and [volatile fields](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#1554-volatile-fields) — ordinary read/write indivisibility is separate from atomic RMW and acquire/release field access.
- [C++ order and consistency](https://eel.is/c++draft/atomics.order) and [cv-qualifiers](https://eel.is/c++draft/dcl.type.cv) — explicit atomic orders and the distinct volatile qualifier; these are C++ contracts, not definitions of all hardware atomicity.
- [Rust atomic memory model](https://doc.rust-lang.org/std/sync/atomic/index.html#memory-model-for-atomic-accesses) and [volatile reads](https://doc.rust-lang.org/std/ptr/fn.read_volatile.html) — C++20-derived access rules, their object/access-model differences, and volatile I/O's lack of inter-thread synchronization.
- [Intel manuals for Intel 64 and IA-32](https://www.intel.com/content/www/us/en/developer/articles/technical/intel-sdm.html) — official manual portal for ISA and memory-ordering/SFENCE profile work; a completed instruction-sequence audit is not claimed here.
- [AMD64 Architecture Programmer's Manual, Volume 2](https://docs.amd.com/v/u/en-US/24593_3.45_APM_Vol2_PUB) and [Volume 3](https://docs.amd.com/v/u/en-US/24594_3.38_APM_Vol3_PUB) — official memory-system and instruction references identified for profile work; backend mappings still require a section-level audit of the applicable revision.
- [Arm memory-access ordering](https://developer.arm.com/community/arm-community-blogs/b/architectures-and-processors-blog/posts/memory-access-ordering-part-3---memory-access-ordering-in-the-arm-architecture) — Normal/Device memory, DMB, and shareability-domain explanations were checked. This article discusses Armv7-A, not a proof of an Arm64 instruction mapping.
- [Armv8-A memory model guide](https://developer.arm.com/-/media/Arm%20Developer%20Community/PDF/Learn%20the%20Architecture/Armv8-A%20memory%20model%20guide.pdf?revision=58b1dd0a-3800-4218-b21a-f95a0332034c) — identified official guide for Arm64 profile work; its PDF text was not independently audited for this proposal.
- [RISC-V RVWMO](https://docs.riscv.org/reference/isa/v20260120/unpriv/rvwmo.html), [A extension atomics](https://docs.riscv.org/reference/isa/v20260120/unpriv/a-st-ext.html), [Ztso](https://docs.riscv.org/reference/isa/v20260120/unpriv/ztso-st-ext.html), and [V](https://docs.riscv.org/reference/isa/v20260120/unpriv/v-st-ext.html) — checked for memory-model primitives, address-domain ordering, optional stronger ordering, and vector-model boundaries in the named revision.
- [LoongArch Volume 1 manual](https://loongson.github.io/LoongArch-Documentation/LoongArch-Vol1-EN.html) — checked against its published source sections on [memory consistency](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/programming-model-of-basic-integer-instructions/overview-of-memory-consistency.adoc), [access types](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/programming-model-of-basic-integer-instructions/memory-access-types.adoc), [barriers](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/overview-of-basic-integer-instructions/barrier-instructions.adoc), and [atomic access instructions](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/overview-of-basic-integer-instructions/atomic-memory-access-instructions.adoc); executable mappings still need target-profile validation.
- [.NET thread-safe collections guidance](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/) — the absence of synchronization in generic collections is distinct from the runtime's memory-model guarantees.
- [Linux kernel memory barriers](https://docs.kernel.org/core-api/wrappers/memory-barriers.html) — checked for the distinction between CPU/compiler ordering, device accesses, cache coherence, and DMA; explicitly a Linux guide rather than a Terrum specification.
- [Linux DMA mapping guide](https://docs.kernel.org/core-api/dma-api-howto.html) — checked for distinct CPU/physical/bus address spaces, IOMMU mappings, mapping lifetimes, and cache-line sharing risks on DMA-incoherent systems.
- [Linux false-sharing guide](https://docs.kernel.org/kernel-hacking/false-sharing.html) — checked for cache-line contention as a performance concern distinct from language-level data races.
