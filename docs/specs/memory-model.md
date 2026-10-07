# Memory model

> **Accepted design; concurrent APIs are not implemented.** This document records the selected
> memory and concurrency policy, not a complete formal model, frozen API
> grammar, or claim that Terrum currently supports concurrent access.
> Examples are pseudocode, not implemented Terrum syntax or library APIs.

Accepted call and lifetime rules live in [language](language.md),
[lifetimes](lifetimes.md) and [language contracts](language-contracts.md);
source forms and their open boundaries are in the
[grammar](grammar-spec.md), and candidate proof rules are in the
[static-proof proposal](../proposals/static-proof-and-constant-evaluation.md).
[Data layout and ABI contracts](data-layout-and-abi.md) separates required
primitive guarantees from physical layout, interchange and procedure calls;
its remaining [profile and implementation work](data-layout-and-abi.md#profile-specification-and-implementation-work)
does not reopen the accepted memory guarantees.
[Implementation](../implementation.md) remains the executable support boundary;
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
| Atomic read-modify-write (RMW) | The read and conditional or unconditional update form one indivisible operation relative to covered competing accesses. Exchange, compare-exchange, and atomic addition are examples. | A particular memory order, lock freedom, or atomicity of surrounding code. |
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
| Thread-confined | Access is restricted to one execution thread at a time, with no concurrent competing access. | Permanent attachment to that thread; a checked handoff may transfer authority. |
| Thread-affine | Use is restricted to a designated thread or executor. | Merely being unshared, or permission to transfer to a different thread. |
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

The access rules below state the accepted contract. The event relations make
that direction precise enough to guide specification and implementation, but
the formal treatment of mixed sequentially consistent operations and fences
remains incomplete. The [library surface](#library-access-surface) records the
selected API direction; [specification and implementation obligations](#specification-and-implementation-obligations)
list the work needed to realize it.

## From single-threaded code to shared memory

### 1. Nonconcurrent access is the default

Without an explicit cross-thread contract, memory is used within one execution
thread, not concurrently accessible to other threads. Readonly defaults
encourage state that does not change after initialization, but a readonly path
alone cannot prove that another permitted alias cannot change the value.
Even genuinely immutable state still needs an appropriate transfer or sharing
contract to cross the thread boundary.

This is why default safe code has no competing cross-thread accesses: there is
no permitted other thread accessing its storage. It is not because every
ordinary read and write secretly uses synchronization.

### 2. Mutation preserves single-threaded consistency

`mut` and `write` permit the corresponding updates, not concurrent access.
A scalar update or aggregate copy may use one instruction, several stores,
or no memory instruction at all when a value can stay in a register. With no
permitted concurrent observer, tearing between those machine operations is
not a source-level concern.

Ordinary unshared data may be optimized and vectorized freely within the
source contract, including its bounds, aliasing, fault, arithmetic and effect
rules. More generally, the observable source contract is invariant under
optimization; it does not require a fixed count or shape of loads, stores or
instructions. Evaluation order, aliases, calls and callbacks, checked
failures, cleanup, and any explicitly permitted partial completion remain
observable as specified.

For example, an explicitly requested non-temporal store operation may leave
ordering or completion obligations to its caller. Its contract must specify
the fence or completion operation required before dependent accesses or
publication. If the compiler instead selects non-temporal stores to implement
an ordinary assignment or copy, those obligations remain the implementation's
responsibility; instruction selection cannot weaken the source contract.
See [non-temporal stores and cache management](#non-temporal-stores-and-cache-management).

### 3. Explicit sharing introduces observable access atomicity

An explicit checked sharing contract admits competing observers. It covers the
complete qualified form, reachable authority and dependencies, not just an
outer annotation. Ordinary operations may be used concurrently when that
checked protocol permits them; "ordinary" means no separately requested
ordering or RMW operation, not thread-local or inherently illegal access.

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
publication relations. Publication uses explicit release/acquire
communication; a relaxed RMW does not implicitly relay a prior release.
An acquire-release RMW may acquire and republish through its own read and write
effects. Atomic RMWs make one update indivisible; locks or equivalent checked
protocols can protect compound invariants. Their roles are complementary, not
interchangeable. Dependency-only consume publication is not part of the
selected model.

A writer can initialize a large payload with many ordinary stores, then
publish it through an appropriate release/acquire protocol. A reader that
observes that publication can use the complete payload without an atomic
whole-payload copy, provided its lifetime is valid and conflicting mutation
is excluded. A lock can instead protect a larger mutable payload: its
indivisibility to other participants comes from excluding their access during
the update, not from the payload's machine representation.

For potentially competing accesses, optimization preserves the source
contract's atomicity, coherence and ordering, and does not introduce new
conflicts, races, effects or observable faults. This is an invariant on
observable behavior, not a requirement to preserve each source access or emit
one corresponding instruction. An unused load may be removed when its value is
irrelevant and its required effects are preserved or proved irrelevant; some
cases may require retaining only an ordering effect. Neither every load
elision nor every coalescing is categorically forbidden or universally valid.
The source operation, access contract and observable effects decide.

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

The selected capability names are `[ThreadTransferable]` and
`[ThreadShareable]`. They are checked properties of a complete qualified form,
not synchronization operations or unchecked trust attributes. Capability
declaration syntax remains to specify. The capability characterizes the
complete qualified form, not whether it is currently exposed concurrently,
whether mutation is permitted, or which synchronization operation is used.

| Capability | Meaning | Does not imply |
|---|---|---|
| `[ThreadTransferable]` | A value and its live dependencies may cross a thread boundary under a checked handoff. Transfer accounts for every still-usable authority to mutable state. | Concurrent sharing, deep immutability, or thread affinity. |
| `[ThreadShareable]` | The exposed operations are safe under the declared concurrent-access protocol. The proof covers reachable authority and generic arguments, not just outer fields. | Mutation permission, current concurrent exposure, or correctness of a multi-operation algorithm. |

Authoritative declarations and producer metadata determine consumer-visible
capabilities, including any deliberately specified defaults. A compiler may
derive implementation proofs automatically, but private layout or a closed
generic specialization does not silently change a published capability.
Generic bodies use their declared constraints and conditional rules; machine
code optimization does not change source binding. A checked handle or access
path establishes current authority, and the consumer selects a permitted
operation. Eligibility for sharing does not mean a location is currently
exposed concurrently, nor does it make every access volatile. Confined or
exclusively guarded mutable storage uses ordinary operations. A bare `x++` through a potentially competing
shared path is rejected unless its protocol supplies exclusive access; the
caller instead selects an atomic update or acquires a guard. The operator
never changes into an RMW because of a storage annotation. Both `x++` and
`y++` still require mutation permission under readonly-by-default rules.

Layout/ABI stability, capability-contract stability, and a trivial wrapper's
carrier-derived atomicity are distinct properties. A capability does not
promise a fixed representation, and a stable or trivial representation does
not by itself establish transferability or shareability.

The initial composable protocols are checked handoff, immutable publication,
scoped exclusion guards and explicit shared scalar access. Managed-reference
operations additionally require the collector contracts described below.

Reader/writer cardinality belongs to the access protocol, not separate
transfer/share capability variants:

| Arrangement | Access obligations |
|---|---|
| Multiple readers, no concurrent writer | Ordinary reads can suffice after publication, with valid lifetimes and exclusion of mutation. Readonly aliases alone do not prove those conditions. |
| Single reader / single writer | Concurrent conflicts still require handoff, ordered accesses or another checked protocol. |
| Multiple readers / single writer | A single writer removes writer/writer competition, not reader/writer conflicts or torn multi-field snapshots. |
| Multiple readers / multiple writers | The protocol also coordinates writers; compound updates require RMWs or exclusion. |

A reader/writer lock permits multiple readers **or** one exclusive writer.
A queue permitting overlapping producer/consumer activity has a different
protocol; cardinality alone does not establish its safety.

Ordinary `List<T>` is not concurrently shareable by default. An opted-in
synchronized implementation must account for every shared operation and
exposed value, callback, iterator and reference. Its internal ordinary accesses
receive the protocol's guarantees, not an implicit RMW contract. Merely adding
a readonly handle or a lock-shaped wrapper is insufficient.

The checker has these obligations:

- Derive proofs for checked source implementations, reachable authority and
  synchronization protocols. Admit supported primitives and protocol forms;
  do not infer safety from an unsupported custom algorithm or private layout.
- For generic types, the proof is conditional on the complete arguments. A
  `ConcurrentBox<T>` cannot be shareable for arbitrary `T` if it exposes
  unsynchronized access to `T`.
- For opaque imported types, the provider publishes a versioned contract that
  is authoritative for consumers and identifies its checked proof or
  explicitly trusted implementation boundary. Consumers validate their use
  of that contract; a native implementation is not thereby machine-checked.
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

## Access-event model and formalization status

The selected access-based model covers locations under a specified width and
compatible non-tearing accesses, whether ordinary or explicitly ordered by
their contract. It does not require dedicated atomic objects or define
incompatible overlapping-width accesses. The following event vocabulary
records the intended rules; the formal treatment of mixed SC and fence
interactions remains work.

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
  operations. A release operation on a location
  synchronizes with an acquire operation on that same location when the acquire
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
  protocol. Ordinary non-atomic accesses are not thereby assigned one
  universal read-from rule independent of their checked protocol.

Where the protocol excludes competing writes, a read observes the last write
to that location in the established happens-before order. With permitted
competing writes, happens-before alone does not select one value; the location's
coherence and access rules constrain the allowed observations. The model
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

Unsafe code may be used for interop and handwritten synchronization. It moves
the relevant proof responsibility to the developer; it does not itself make
an invalid protocol safe or make unsafe code unchecked in the arithmetic or
failure-policy sense. An abstraction may expose caller obligations, or a
trusted implementation may contain them behind an explicitly declared safe
contract. Exact declaration syntax and metadata remain to specify. Optional
diagnostics may report misuse, but do not repair it or change the source
contract. Native code and foreign devices additionally require their own
contracts. None of these boundaries imports C++'s rule that an ordinary data
race makes the whole program undefined.

For operations whose checked preconditions hold, a non-tearing shared read's
value must be justified by an initial value or a permitted write event whose
value is computed from justified inputs. Where tearing is permitted, each
observed portion must have a justified source under the applicable access
rules; the combined value need not have been written by one whole-value
store. The exact formal no-out-of-thin-air and causality rules remain part of
the specification work. An access-contract violation does not license the
compiler to invent arbitrary whole-program behavior, treat source code as
unreachable, or add an unrequested trap. Ordinary optimizations, including
elimination of an irrelevant load, remain governed by the source contract and
observable effects. Misuse is not repaired. Inlining and constant propagation
do not change the source rules, and unsafe code does not convert ordinary
accesses into explicit atomic or ordered operations.

Justification must include the control conditions that cause a write to occur,
not merely its stored bit pattern. Speculating a conditional store cannot make
the assumed read that enabled it justify itself. This is a causality
requirement, not a requirement that every reads-from edge follow wall-clock
observation order; ordinary permitted weak-memory reorderings remain possible.

### Atomic coherence

For locations covered by the coherence rules:

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

The selected model uses direct release-to-acquire communication: a relaxed
RMW does not implicitly relay an earlier release's publication. An
acquire-release RMW that reads a release can acquire and republish through its
explicit acquire, sequencing and release effects. Dependency-only `consume`
ordering is not provided. These rules do not import C++ release-sequence or
fence semantics.

## Explicit-order semantics

The table states the selected ordering surface. API names do not replace
these event rules or require dedicated atomic storage.

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
whether spurious failure is possible. The selected default is non-spurious
strong CAS, with SC and full ordering on failure as well as success. Failure
ordering and spurious failure are distinct properties; explicit weak variants
and the complete valid order-pair table remain to specify.

CAS compares bits, not user-defined equality. An eligible value needs a
defined bitwise comparison representation: `+0` and `-0`, and distinct NaN
encodings, compare differently when their bits differ. Unspecified padding
disqualifies an aggregate unless a defined comparison representation removes
that ambiguity. Managed-reference CAS remains gated on collector support.

### Publication example

Illustrative pseudocode:

```text
payload = 42
MemoryPrimitives.AtomicWrite(ready, true)

if MemoryPrimitives.AtomicRead(ready):
    use(payload)
```

Assume the checked sharing contract permits the reader and writer to access
this object and its lifetime spans both operations.
The payload is published only if the acquire reads from the release (or a
precisely specified release RMW). If the acquire reads an older `false`, or a
relaxed store, the required `sw` edge does not exist. If `payload` can also be
mutated concurrently, its separate access protocol must still be valid.

### Relaxed store-buffering example

Illustrative pseudocode, initial `x = y = 0`, all operations relaxed:

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

Illustrative pseudocode, initial `x = y = 0`, stores release and loads acquire:

```text
Thread A       Thread B       Thread C                       Thread D
x = 1 release  y = 1 release  r0 = x acquire; r1 = y acquire  r2 = y acquire; r3 = x acquire
```

If `r0 == 1, r1 == 0, r2 == 1, r3 == 0`, each reader has acquired one
release, but the independent per-object communications do not order the two
reader executions with each other. The selected release/acquire direction
permits this result absent another ordering edge. An all-SC version must
forbid it because the single SC order cannot place both readers' first
observed store before their second observed initial value. In that version,
both stores and all four loads are SC. This is the selected portable
release/acquire direction, not a claim that every target realizes every
permitted outcome. Stronger target behavior does not invalidate the contract.

### Fences, `volatile`, and interlocked operations

A full fence orders covered accesses on both sides; that alone does not
define SC participation or an `sw` edge. A formal fence model must cover its
interaction with ordinary accesses, ordered accesses, RMWs and communication.
The .NET full-fence contract and C++ `atomic_thread_fence` are not interchangeable
definitions. The [strong RMW contract](#ordering-and-fences) requires both SC
and full-fence obligations, not one inferred from the other.

Acquire/release shared-RAM access can publish payload through the communication
relation above. Terrum uses explicit library operations, not a `volatile`
keyword or a declaration that silently changes every field access. Device
accesses obey their width, side-effect and ordering contracts. Neither "volatile is
not atomic" nor "volatile cannot publish" is a useful unqualified rule.

The .NET `Volatile` API supplies non-tearing accesses for its supported types,
including certain 64-bit accesses on 32-bit processors; that is stronger than
ordering alone. Terrum likewise specifies atomicity separately and requires
every `AtomicRead`/`AtomicWrite` to satisfy its supported-width contract.
These guarantees constrain observable behavior, not a fixed instruction count:
an unused load may be eliminated when its required synchronization effects
remain or are proved irrelevant. In some cases the implementation may retain
an ordering effect without retaining the value-producing load; this is not a
universal transformation permission. Faults and other specified effects remain
observable. An access need not bypass processor caches or transact with DRAM.

For example, the .NET runtime memory-model discussion permits elimination of
an unused read, including a volatile read, while noting cases where a read's
fault is observable. Terrum applies its own source contracts to determine
which effects a particular operation carries and which residual effects an
optimization must preserve.

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

`MemoryPrimitives.UnalignedRead` and `UnalignedWrite` are explicit value
accesses with a target-defined supported range and representation. They do not
promise an atomic snapshot and do not grant permission to forge references or
construct invalid representations. Some hardware supports non-tearing
unaligned accesses within a cache line but not across a line boundary; other hardware faults or requires
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

Initializing every field does not initialize padding bytes, so a live complete
value does not make every byte of its object representation readable. Raw
object-byte blits and unmediated raw foreign transfers are unsafe operations.
A `Blittable` target-profile promise fixes a representation but does not make
padding initialized or grant arbitrary raw access. Safe binary serializers
must account for the initialization and representation of every emitted byte
under their wire format; they may use a verified or explicitly trusted raw copy
when its contract covers those bytes, and need not encode field by field. These
representation rules are independent of access atomicity and memory ordering.

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
profile or be rejected. Default atomic operations are lock-free. A runtime-lock
fallback requires an explicit opt-in API or overload; advertise its blocking
contract and reject it in contexts requiring lock freedom or incompatible
reentrancy, such as signal/interrupt handlers.

All aliases and participants using a lock-backed atomic must use the same
coordination protocol, including across modules and native code. Mixing a
runtime-lock operation with a foreign native atomic on the same bytes is not
automatically interoperable. Runtime dispatch and helper selection must
preserve the declared representation and coordination, not silently introduce
different per-module locks or change an exported layout.

Ordering also does not promise scheduler fairness, bounded latency, or eventual
success of an arbitrary polling loop. Such liveness/progress guarantees need
their own primitive/runtime contract. A lock-free primitive does not make every
algorithm built from it wait-free. A target-exclusive retry loop needs an
actual forward-progress proof; the availability of an LR/SC or CAS instruction
alone is not that proof.

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
  An Arm64 mapping must use the corresponding Armv8-A rules; this document
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

SIMD operations are permitted on confined data, under an exclusive guard, or
on immutable data after publication. They need not provide whole-vector
atomicity when the source contract promises no snapshot; each location's
required access, ordering and lifetime guarantees still apply. A vector memory
operation that overlaps a concurrent write is governed by the same byte-range
conflict and checked-contract rules as scalar operations. A compiler may not
claim atomic snapshot semantics merely because it selected one vector
instruction.

Masked-lane access is determined by the source contract: if it promises no
access or fault for masked-off lanes, the lowering must preserve that promise.
Otherwise the API must state lane behavior; it cannot be inferred from a mask's
visual meaning. Tail handling must not access beyond the declared range.
Inactive lanes do not grant bounds authority over neighboring objects. Packed
atomic masks or vector RMW operations require a target-profile atomicity
guarantee for the whole declared location. Independent scalar atomics are not
a substitute for whole-vector atomicity.

SIMD alignment, lane order, and instruction availability belong to ABI and
target-feature profiles. They do not change the abstract race or publication
rules.

Gather/scatter operations describe multiple accessed ranges, not an atomic
snapshot of all lanes. An API must specify the behavior of duplicate scatter
addresses and any guaranteed lane ordering. A faulting multi-access operation
can partially complete unless its source contract explicitly promises otherwise
and the implementation can preserve that promise. Neither a vector instruction
nor a mask implies transactional rollback.

Intel's guarantee for specified aligned 128-bit moves on processors enumerating
AVX does not establish atomicity for arbitrary vector operations or
representations. Each target profile must verify its exact vendor, instruction,
width, alignment and memory-type guarantees rather than infer them from ISA
availability alone.

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

The boundary is an explicit unsafe/platform-checked device mapping with typed
register operations. It must not be exposed as a regular `ref<T>`
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
  operations. Optimization cannot invent thread-transfer/share capabilities,
  remove required synchronization effects, or rely on a debug-only check.
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

## Library access surface

The selected flat static API type is **`MemoryPrimitives`**. It groups low-level
operations on existing storage and controls their ordering/cache behavior,
without becoming an allocation, ownership or collection abstraction. The name
fits the operation-oriented library vocabulary, including `TensorPrimitives`,
while access guards and storage leases retain their distinct roles.

Method names identify the access family, operation and result behavior.
There are no separate top-level `Atomic`, `Volatile`, `Interlocked`,
`Streaming` or `Cache` API families. These are design sketches; `location`
stands for a checked access path, not an unqualified raw pointer or a finalized
reference-argument spelling.

### Access and atomic operation families

| Family | Operations | Result forms |
|---|---|---|
| Atomic access | `AtomicRead`, `AtomicWrite` | Read returns a value; write returns `void`. |
| Unaligned access | `UnalignedRead`, `UnalignedWrite` | Read returns a value; write returns `void`. Neither promises an atomic snapshot. |
| Arithmetic | `AtomicAdd`, `AtomicSubtract`, `AtomicIncrement`, `AtomicDecrement` | `void`, previous value, updated value. |
| Bitwise | `AtomicAnd`, `AtomicOr`, `AtomicXor`, `AtomicNot` | `void`, previous value, updated value. |
| Bounds/reduction | `AtomicMin`, `AtomicMax` | `void`, previous value, updated value; signedness follows the type. |
| Replacement | `AtomicExchange` | No result or previous value; returning the supplied replacement is unnecessary. |
| Conditional replacement | `AtomicCompareExchange` | Success-only or success with the observed previous value; exact result signatures remain open. Comparison is bitwise over an eligible defined representation, not user-defined equality. |

```text
MemoryPrimitives.AtomicAdd(location, amount)
MemoryPrimitives.AtomicAddGetPrevious(location, amount)
MemoryPrimitives.AtomicAddGetUpdated(location, amount)
```

All three are RMWs with the same arithmetic and ordering contract. "Updated"
means the value produced by this RMW, not a promise that no subsequent writer
has changed the location. Result-free operations can avoid unnecessary result
work, but do not promise a cheaper instruction sequence on every target.

Provide complete semantic families: add/subtract, increment/decrement,
AND/OR/XOR, and min/max must not depend on which individual instructions a
backend happens to expose. Correct instruction sequences or permitted runtime
helpers may implement missing native operations. Bit-test/set/reset/complement
helpers corresponding to BTS/BTR/BTC are deferred, not ruled out. Atomic
multiply/divide, shifts, floating arithmetic and arbitrary callback updates
are not part of the initial operation set.

### Arithmetic policy

Default arithmetic remains checked. A policy scope can select an explicitly
declared wrapping variant of a participating arithmetic operation:

The marker and declaration forms below are schematic; exact syntax and
metadata remain to specify.

```text
MemoryPrimitives.AtomicAdd(location, amount)
require policy<Arithmetic.Wrap>(MemoryPrimitives.AtomicAdd(location, amount))
```

The Core domain and case names in this example are illustrative. The
unannotated checked operation and a wrapping variant share a name and
parameter/result shape. Binding selects the variant and its effects; it does
not dynamically propagate arithmetic policy into arbitrary callees.
Nonparticipating calls retain their declared behavior. A plain
`policy<Arithmetic.Wrap>` scope is a preference and can fall back to the
unannotated checked operation; the `require` form above diagnoses a missing
variant. Exact variant declaration syntax and metadata remain open.

Only overflow policy changes, not atomicity, ordering, bounds, lifetime or
access permissions. Checked overflow leaves the location unchanged. Return
shape cannot select a different overflow policy. Implementing subtraction
with addition is valid, but must preserve subtraction's result and failure:
subtracting `int32.MinValue` from `-1` is representable even though separately
negating `int32.MinValue` is not. A write followed by overflow detection is not
an implementation of a checked, no-update-on-failure RMW.
Existing named wrapping variants such as `AddWrapping` remain valid library
designs pending compiler support; they do not silently replace checked
behavior.

The [conversion policy](conversions.md#conversion-policies) governs lexical
selection more generally; this does not claim an implemented policy-variant
declaration feature or a separate `unchecked` source form.

### Ordering and fences

One **`MemoryOrder`** enum covers `Relaxed`, `Acquire`, `Release`,
`AcquireRelease` and `Sequential`. Orders must be compile-time-known and valid
for the operation. Invalid combinations are compiler errors in every build,
with corresponding persisted-contract validation, not optional analyzer advice.

| Operation | Default | Explicit valid orders |
|---|---|---|
| `AtomicRead` | Acquire | Relaxed, Acquire, Sequential |
| `AtomicWrite` | Release | Relaxed, Release, Sequential |
| RMW | Sequential plus full before/after ordering | Relaxed, Acquire, Release, AcquireRelease, Sequential |
| `Fence` | Sequential plus full before/after ordering | Acquire, Release, AcquireRelease, Sequential |

```text
MemoryPrimitives.AtomicRead(location, MemoryOrder.Relaxed)
MemoryPrimitives.AtomicWrite(location, value, MemoryOrder.Release)
MemoryPrimitives.AtomicAddGetPrevious(location, amount, MemoryOrder.Relaxed)
MemoryPrimitives.Fence(MemoryOrder.Acquire)
MemoryPrimitives.CompilerFence(...)
```

These defaults belong to the selected operations, not a storage declaration.
A lock-protected payload uses ordinary accesses under its guard; published
immutable fields do not require repeated acquires. Explicit relaxed accesses
remain coherent and non-tearing where supported, not thread-local accesses.
A deliberate atomic read, local addition and atomic write can still lose an
increment; the sequence is not one RMW.
Ordered operations also remain legal under exclusive access; sharing is not a
prerequisite for requesting ordering, and the request does not grant sharing.

Each RMW has one order for the complete indivisible event, rather than separate
read/write orders. Compare-exchange instead has success and failure orders
because these are different outcomes. The default is non-spurious strong CAS,
with an SC read and full before/after ordering on failure. Failure does not
release a value that was not stored; release-only or acquire-release failure
orders are invalid. Explicit weak variants and the complete legal order-pair
table need specification.

SC participation and full ordering of covered accesses are separate
obligations, both required by strong RMWs and the default fence. This does not
make every other operation in the program SC. Acquire-release ordering is not
a full fence: it does not alone order an earlier store against a later load.
`Fence(Relaxed)` is rejected rather than silently doing nothing.

The fence contract must express read/read, read/write, write/read and
write/write coverage across the fence; exact signature and mask spelling
remain to specify.
`CompilerFence` constrains compiler transformations only; it is not a CPU
synchronization operation. ISA-specific speculation or device barriers belong
to their target contracts, not an inferred meaning of the word "fence".

Omitting an RMW result must not weaken ordering. For example, Arm LSE
[`LDADD`](https://www.scs.stanford.edu/~zyedidia/arm64/ldadd.html)
does not retain acquire behavior when its destination is the zero register;
the no-return alias cannot blindly replace an acquire/strong source operation.
All result variants must preserve the same declared order.

### Non-temporal access and cache maintenance

```text
MemoryPrimitives.NonTemporalRead(...)
MemoryPrimitives.NonTemporalWrite(...)
MemoryPrimitives.NonTemporalCopy(...)
MemoryPrimitives.NonTemporalCompleteWrites()

MemoryPrimitives.CacheWriteBack(range)
MemoryPrimitives.CacheInvalidate(range)
MemoryPrimitives.CacheWriteBackInvalidate(range)
```

Non-temporal policy is independent of `MemoryOrder`, not a level weaker than
relaxed atomicity. These operations carry their own target-dependent range,
alignment, memory-type, access-indivisibility and completion contracts.
Reads and writes may have different target support. An explicit streaming
copy is not an atomic snapshot or transaction.

```text
MemoryPrimitives.NonTemporalCopy(destination, source)
MemoryPrimitives.NonTemporalCompleteWrites()
MemoryPrimitives.AtomicWrite(ready, true)
```

Completion satisfies the streaming-write obligations; the flag communicates
publication to a matching acquire. Hidden non-temporal lowering of an ordinary
operation must supply any required completion itself. See the
[non-temporal contract](#non-temporal-stores-and-cache-management).

Cache operations distinguish issuing maintenance from completing it and specify
the required completion operation. They may require privileges or exclusive
control of affected cache lines, including neighboring bytes affected by range
rounding. There is no universal flush promising CPU publication, DMA coherence
and persistence together; those observer domains remain separate.

### Support and progress

Capability queries distinguish **supported**, **lock-free**, and, only where
justified, **wait-free**. They identify operation, type/representation, order,
alignment, memory domain and target features. An atomic contract does not imply
one instruction or bounded completion. `MemoryPrimitives` uses lock-free
implementations where the selected target supports them. An explicit
lock-backed overload or fallback may provide support where lock-free operation
is unavailable, but it must be advertised and may not be silently substituted.
Lock freedom and justified wait freedom remain distinct guarantees.

Double-width compare-exchange is one operation on a supported contiguous
representation, not CAS over two unrelated locations. Eligibility, alignment,
feature checks and cross-module coordination are explicit. A supported wide CAS
does not establish whole-aggregate atomicity for ordinary loads/stores.
Unsupported operation and comparison mismatch must remain distinct outcomes.

The full surface is design direction, not an early-runtime implementation
requirement. Waiting/waking and processor spin hints need separate progress
contracts when introduced; notification alone neither stores nor publishes data.

## Specification and implementation obligations

The policy choices above are accepted. The work below makes them precise and
implements them; it is not a list of open policy alternatives.
This acceptance does not settle the separate
[layout/ABI profile work](data-layout-and-abi.md#profile-specification-and-implementation-work)
or [source call contracts and remaining choices](grammar-spec.md).

| Area | Remaining work |
|---|---|
| Formal observations | Complete the formal rules for mixed SC and non-SC operations, fences and their communication edges, initialization/lifetime boundaries, and causality. Preserve explicit release/acquire publication, no relaxed-RMW release-sequence relay, and no dependency-only consume. Derive litmus tests from the completed rules. |
| Atomic API contract | Specify exact signatures and valid order combinations, including strong/weak compare-exchange outcomes. Preserve bitwise CAS semantics and identify eligible defined comparison representations; unspecified padding is ineligible unless a defined representation resolves it. |
| Source declarations and metadata | Specify syntax and serialized contracts for thread capabilities, trusted unsafe implementations, guards, low-level access paths and checked/unchecked declared variants. Consumer-visible capabilities come from authoritative declarations and producer metadata, including only deliberately specified defaults; they are not inferred from incidental private layout or a closed specialization. |
| Target profiles and progress | Publish per-target support, width, alignment, representation, memory-domain and progress tables. Use lock-free implementations where supported; expose any lock-backed fallback explicitly and coordinate it across all aliases, modules and native participants. Lock-free support does not imply wait freedom. Audit exact vendor/ISA guarantees before relying on them; vector instruction availability alone does not establish atomicity. |
| References and reclamation | Define collector-aware managed-reference loads, stores and RMWs, exact roots, write barriers, publication and safe reclamation before exposing managed-reference atomic operations. These guarantees remain independent of CPU ordering. |
| Representation, SIMD and platform APIs | Complete wrapper eligibility, unaligned value-access, bit-representation and aliasing rules; specify masked-lane/range behavior and any whole-vector atomic operations separately. Define target contracts for non-temporal access, cache maintenance, device memory, DMA and persistence without conflating their observer domains or completion rules. |
| Progress APIs | Give waiting/waking and processor spin hints their own progress and lifetime contracts; notification alone does not store or publish data. |

## Primary references consulted

Language/runtime references are comparisons, ISA references constrain target
lowering, and Linux references describe platform protocols. None automatically
defines Terrum semantics.

- [.NET runtime memory model](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md) — checked for its distinctions among atomic accesses, ordering, volatile, unmanaged memory, and runtime-specific guarantees.
- [.NET `Volatile` API](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0#remarks) -- ordered accesses, conditional publication and supported 64-bit non-tearing accesses on 32-bit processors.
- [C# atomicity of variable references](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables#96-atomicity-of-variable-references) and [volatile fields](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#1554-volatile-fields) — ordinary read/write indivisibility is separate from atomic RMW and acquire/release field access.
- [C++ order and consistency](https://eel.is/c++draft/atomics.order) and [cv-qualifiers](https://eel.is/c++draft/dcl.type.cv) — explicit atomic orders and the distinct volatile qualifier; these are C++ contracts, not definitions of all hardware atomicity.
- [Rust atomic memory model](https://doc.rust-lang.org/std/sync/atomic/index.html#memory-model-for-atomic-accesses) and [volatile reads](https://doc.rust-lang.org/std/ptr/fn.read_volatile.html) — C++20-derived access rules, their object/access-model differences, and volatile I/O's lack of inter-thread synchronization.
- [Intel manuals for Intel 64 and IA-32](https://www.intel.com/content/www/us/en/developer/articles/technical/intel-sdm.html) — official manual portal for ISA and memory-ordering/SFENCE profile work; a completed instruction-sequence audit is not claimed here.
- [AMD64 Architecture Programmer's Manual, Volume 2](https://docs.amd.com/v/u/en-US/24593_3.45_APM_Vol2_PUB) and [Volume 3](https://docs.amd.com/v/u/en-US/24594_3.38_APM_Vol3_PUB) — official memory-system and instruction references identified for profile work; backend mappings still require a section-level audit of the applicable revision.
- [Arm memory-access ordering](https://developer.arm.com/community/arm-community-blogs/b/architectures-and-processors-blog/posts/memory-access-ordering-part-3---memory-access-ordering-in-the-arm-architecture) — Normal/Device memory, DMB, and shareability-domain explanations were checked. This article discusses Armv7-A, not a proof of an Arm64 instruction mapping.
- [Armv8-A memory model guide](https://developer.arm.com/-/media/Arm%20Developer%20Community/PDF/Learn%20the%20Architecture/Armv8-A%20memory%20model%20guide.pdf?revision=58b1dd0a-3800-4218-b21a-f95a0332034c) — identified official guide for Arm64 profile work; its PDF text was not independently audited for this document.
- [RISC-V RVWMO](https://docs.riscv.org/reference/isa/v20260120/unpriv/rvwmo.html), [A extension atomics](https://docs.riscv.org/reference/isa/v20260120/unpriv/a-st-ext.html), [Ztso](https://docs.riscv.org/reference/isa/v20260120/unpriv/ztso-st-ext.html), and [V](https://docs.riscv.org/reference/isa/v20260120/unpriv/v-st-ext.html) — checked for memory-model primitives, address-domain ordering, optional stronger ordering, and vector-model boundaries in the named revision.
- [RISC-V Zacas](https://docs.riscv.org/reference/isa/v20260120/unpriv/zacas.html) -- double-width CAS, alignment, compare outcomes and ordering requirements, including 128-bit `AMOCAS.Q` on RV64.
- [LoongArch Volume 1 manual](https://loongson.github.io/LoongArch-Documentation/LoongArch-Vol1-EN.html) — checked against its published source sections on [memory consistency](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/programming-model-of-basic-integer-instructions/overview-of-memory-consistency.adoc), [access types](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/programming-model-of-basic-integer-instructions/memory-access-types.adoc), [barriers](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/overview-of-basic-integer-instructions/barrier-instructions.adoc), and [atomic access instructions](https://raw.githubusercontent.com/loongson/LoongArch-Documentation/main/docs/LoongArch-Vol1-EN/basic-integer-instructions/overview-of-basic-integer-instructions/atomic-memory-access-instructions.adoc); executable mappings still need target-profile validation.
- [.NET thread-safe collections guidance](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/) — the absence of synchronization in generic collections is distinct from the runtime's memory-model guarantees.
- [Linux kernel memory barriers](https://docs.kernel.org/core-api/wrappers/memory-barriers.html) — checked for the distinction between CPU/compiler ordering, device accesses, cache coherence, and DMA; explicitly a Linux guide rather than a Terrum specification.
- [Linux DMA mapping guide](https://docs.kernel.org/core-api/dma-api-howto.html) — checked for distinct CPU/physical/bus address spaces, IOMMU mappings, mapping lifetimes, and cache-line sharing risks on DMA-incoherent systems.
- [Linux false-sharing guide](https://docs.kernel.org/kernel-hacking/false-sharing.html) — checked for cache-line contention as a performance concern distinct from language-level data races.
