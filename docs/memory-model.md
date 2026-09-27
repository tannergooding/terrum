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

## Goals and scope

Terrum should give safe programs a portable, compiler-independent account of
communication between concurrent executions without making ordinary
application code depend on hardware-level concurrency details.

> Safe code cannot concurrently access memory unless its checked contract
> explicitly permits it. Ordinary code is nonconcurrent by default; mutation
> authority does not change that. Once concurrent access is permitted, access
> atomicity, ordering, publication, and lifetime guarantees govern what other
> threads may observe.

This is a restriction on safe sharing, not a claim that an ordinary access is
intrinsically illegal or that every shared operation must use a special atomic
type. A checked sharing contract may permit ordinary non-tearing accesses when
its protocol makes them valid. Such permission does not prove that the
program's higher-level algorithm preserves its invariants.

Transfer and concurrent sharing are separate permissions. Transfer moves
usable authority under a checked handoff contract; sharing permits concurrent
aliases under a checked access protocol. Neither is implied by `mut`, `write`,
or readonly access. Readonly limits one access path; it does not imply
immutability, thread-safety, uniqueness, purity, or safety of references
reachable through the value.

This proposal covers ordinary cacheable memory and CPU-to-CPU communication.
It deliberately separates that model from device registers, DMA, persistent
storage, and side-channel security. Those domains have different observers,
completion rules, and platform contracts; a CPU memory fence must not be
presented as a universal operation on them.

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
| Ordinary access | An access without a separately requested ordering or RMW operation. A checked sharing contract may still permit it concurrently under a specified protocol. | That it tears, is automatically illegal when shared, is fresh, or can be freely merged/speculated in every context. Its baseline guarantees still need specification. |
| Volatile access | A language- or platform-specific contract that must be qualified below; the word alone does not select one. | A portable cross-language meaning or an automatic atomic RMW. |
| Readonly access | A restriction on mutation through one access path. | Immutability of reachable state or permission to share concurrently. |
| Immutable value/graph | A value or defined reachable extent that cannot be changed through any permitted alias for the promised lifetime. | Transferability, safe publication, or thread-safety of external state it references. |
| Transfer | A checked handoff of usable authority between execution contexts. | Concurrent use by the sender and receiver. |
| Sharing | A checked permission for concurrent aliases, governed by an access and lifetime protocol. | Correctness of every algorithm using those aliases. |
| Mutation authority (`mut`, `write`) | Permission to update a place or referent as allowed by its declaration and access path. | Cross-thread permission, synchronization, or ownership transfer. |

An ordinary aligned scalar load can therefore be atomic without being an
acquire load. An ordinary scalar store can be atomic without being a release
store. Even if both accesses in `counter = counter + 1` are non-tearing, the
combined update can lose a competing increment. A single machine instruction
does not by itself make that sequence an atomic RMW.

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

Transfer hands authority to another thread without granting simultaneous use;
concurrent sharing admits competing observers under a checked contract. Neither
permission follows from writable or readonly access. The contract covers the
complete type form, reachable authority and dependencies, not just an outer
annotation.

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
Exact checked wrapper eligibility remains to specify.

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

Volatile accesses may provide the selected ordering contract; interlocked
operations may combine indivisible updates with ordering. Exact API spellings,
order strengths and standalone fence rules remain open. Neither a CPU fence
nor the word `volatile` implies a cache flush, device ordering, or durability.

### Baseline guarantees to specify before choosing an API

The primitive/trivial-wrapper baseline above is agreed direction. The
[layout and ABI companion](data-layout-and-abi.md) records its storage and
interchange implications. The remaining details need explicit answers,
independent of
whether its eventual library exposes `Atomic<T>`, `Volatile.Read`, named
acquire/release operations, or another surface:

- The concrete primitive/reference representation and alignment table,
  checked trivial-wrapper eligibility, and any guarantees beyond the baseline.
  Whole-aggregate atomicity does not follow from aggregate size. Explicit
  unaligned and overlapping access have separate contracts below.
- Which ordinary and ordered accesses the compiler may remove, merge,
  duplicate, widen, or speculate. A non-tearing instruction does not by
  itself require the compiler to issue every source-level read.
- The ordinary-access ordering and coherence baseline, including
  data-dependent reads and reference publication. The .NET runtime specifies
  some stronger guarantees here; they must be considered explicitly rather
  than either inherited or discarded by adopting C++ terminology.
- Which checked sharing protocols admit concurrent access, which access
  mixtures are valid, and what value/optimization guarantees survive unsafe
  races. Non-tearing hardware alone does not authorize sharing in safe Terrum.
- The additional guarantees of ordered loads/stores, atomic RMWs, locks, and
  fences, including their progress and lifetime obligations.

This baseline does not adopt all .NET runtime behavior or settle every
target representation and synchronization rule. The
explicit-order event model below is one candidate for expressing stronger
operations, not a decision to import C++ atomic storage, race UB, or API rules.

### Comparative evidence, not normative vocabulary

| Context | Atomicity and synchronization vocabulary | Meaning of `volatile` |
|---|---|---|
| C# language | The specification guarantees atomic ordinary reads/writes for listed types, including `int` and references, but not general compound updates. `Interlocked` supplies operations such as atomic increment and compare-exchange. | Volatile field reads have acquire semantics and writes have release semantics. This can support publication; it does not make `++` atomic or impose one global SC order. |
| .NET runtime | Its implementation contract separately specifies alignment and atomic accesses, including properly aligned primitive/enum accesses up to pointer size and atomic managed-reference accesses. `Volatile` and `Interlocked` APIs add their documented guarantees; `Interlocked` methods have full-fence semantics in this runtime contract. | C# field decoration causes the compiler to emit volatile accesses. The IL `volatile.` prefix supplies ordering, not an unconditional non-tearing guarantee for every width, alignment, or block operation. |
| C++ | `std::atomic` and `std::atomic_ref` are language/library concurrency contracts, not just promises that a machine load will not tear. Even relaxed operations have the specified atomic-access/coherence rules. A conflicting unsynchronized ordinary access can be a language data race despite a non-tearing hardware instruction. | The standard qualifier governs volatile evaluations and implementation-defined access details. It is not the C# acquire/release inter-thread synchronization contract. Target-specific extensions must be identified separately. |
| Rust | `Atomic*` operations follow the documented C++20-derived ordering model, translated to an access-based model. Rust's permitted access mixtures are not identical to C++ atomic-object rules. | `read_volatile`/`write_volatile` constrain observable accesses, principally for I/O. They do not provide atomic synchronization between threads. |
| ISA/hardware | Manuals distinguish indivisible accesses, atomic RMW instructions, propagation/coherence properties, and ordering instructions or attributes. Ordinary loads/stores may already be indivisible for specified widths, alignment, memory types, and observers. | A processor does not implement a universal language-level `volatile` qualifier. It executes the instructions and memory attributes selected by the implementation. |

The C# language minimum, ECMA CLI rules, and the stronger documented .NET
runtime implementation contract are not the same specification. In particular,
the C# field-type restrictions for `volatile` are not a list of every operation
offered by `System.Threading.Volatile`. For example, its supported `double`
read is atomic even on a 32-bit runtime, while an ordinary `double` read there
need not be. Conversely, a volatile IL block copy may comprise several accesses;
its ordering does not make the entire block one atomic snapshot.

Nor does C# `volatile` mean "always read the latest value" or "bypass the CPU
cache." Acquire/release ordering constrains what may be observed together when
communication occurs; it supplies neither a wall-clock freshness deadline nor
an atomic compound operation. A release write to a flag followed by an acquire
read that observes it can publish earlier payload writes. The same inference
does not follow from C++ `volatile` or Rust `read_volatile`.

Use qualified terms such as **C# volatile**, **C++ volatile**, **Rust volatile
I/O**, **atomic load/store**, and **atomic RMW** when comparing contracts.
The language-specific contracts are comparative evidence, not selected Terrum
semantics; atomic load/store and atomic RMW retain the definitions above.
In the candidate formalism below, **explicit atomic access** denotes an
explicitly requested atomic operation; it does not mean that all other
accesses tear. A dedicated atomic storage type is one possible API, not part
of the definition of atomicity.

## Sharing capabilities are distinct

The terms below describe separate permissions. `[Send]` and `[Sync]` are
provisional labels already used in design discussion, not selected syntax.

| Capability | Proposed meaning | Does not imply |
|---|---|---|
| Transferable (`[Send]`) | A value and its live dependencies may cross an execution-thread boundary under a checked transfer contract. Transfer must account for every still-usable authority to mutable state. | Concurrent sharing, deep immutability, or thread affinity. |
| Shareable (`[Sync]`) | The type's exposed operations are safe when aliases are used concurrently under the memory model. The proof covers reachable authority and generic arguments, not just the outer object's fields. | Exclusive access, immutability, or freedom from logical races/reentrancy. |
| Thread-affine | A value may be used only on its designated thread or executor. This is a separate capability/restriction, not a synonym for `Send` or `Sync`. | Transferability or concurrent sharing. |
| Readonly | This access path cannot mutate the immediate place it denotes, as defined by the access model. | Deep readonly, immutable reachable objects, race freedom, or safe publication. |

Safe code has no cross-thread access permission by default, but ordinary
mutable access is not intrinsically tied to an operating-system thread.
Ordinary `List<T>` is not concurrently shareable by default. It can become
shareable only through an explicitly opted-in, checked complete-form contract,
for example if every shared operation is internally synchronized and all
values, callbacks, iterators, and references it exposes satisfy the
corresponding contract. A checked contract may permit ordinary accesses within
that protocol; the accesses do not thereby gain RMW or ordering guarantees
beyond the protocol. Merely wrapping a list in a readonly handle or
lock-shaped type does not prove this.

The .NET comparison needs precision: `List<T>` does not supply synchronization
for concurrent mutation, but that is not a blanket C++-style claim that any
managed race makes the whole program undefined. The .NET runtime separately
constrains memory access and optimizations, including preservation of
single-thread consistency. Terrum's proposed difference is checked rejection
of unauthorized sharing, rather than relying on callers to obey a collection's
concurrency documentation. Its remaining unsafe-race policy still needs an
explicit decision below.

An opt-in must be a checked capability, not a magic trust attribute:

- For source-defined types, the compiler verifies the implementation and its
  reachable fields, captured values, returned aliases, callbacks, and
  synchronization protocol. The checker should credit only specified
  synchronization primitives and protocol forms; an unsupported custom
  algorithm cannot opt in through a declaration alone.
- For generic types, the proof is conditional on the complete arguments. A
  `ConcurrentBox<T>` cannot be shareable for arbitrary `T` if it exposes
  unsynchronized access to `T`.
- For opaque imported types, the provider must publish a versioned checked
  contract that consumers can validate. A native or otherwise unverifiable
  claim crosses an explicitly unsafe boundary and remains the caller's
  responsibility.
- Wrapper methods, guards, and projections cannot erase authority carried by
  their contents. A wrapper cannot safely grant a raw reference that its
  argument type or lifetime contract does not allow.

Transfer is not sharing. Sending a mutable class handle while the source still
has an independently usable alias to the same mutable object is not a transfer
of exclusive authority. A retained but provably unusable alias during a
temporary handoff is different; its access restrictions and return of authority
must be checked. Conversely, a type may be safely transferred but not
concurrently shared. If all access is restricted to one designated thread,
that is thread-affinity rather than general transferability.

Readonly is shallow under the existing access direction. For example,
`List<write C>` contains writable object handles even if the list's slots are
readonly. Concurrently sharing the list must account for those object
capabilities; marking the list handle or its slots readonly does not make the
objects safe. Immutable bytes likewise do not make an entire object graph
shareable when another reachable field carries writable authority.

`write` grants authority already present in the accessed place or reference;
it does not create that authority, override nominal restrictions, or grant
cross-thread synchronization. Multiple concurrent writers need an explicitly
checked synchronized protocol or suitable atomic locations. A write guard
that arbitrates aliases is only a synchronization mechanism if its complete
acquire/release, reentrancy, and lifetime behavior is specified and verified;
it is not automatically a mutex by virtue of its name.

A verified synchronized wrapper can nevertheless be shareable without making
its contained list independently shareable. It must exclusively encapsulate
the relevant authority, serialize all access, and prevent references or
callbacks from escaping the protected interval. Its generic requirements
depend on what crosses that boundary: moving a value out and concurrently
sharing a borrowed value are different contracts. Requiring every private
field to be independently shareable would incorrectly exclude this pattern;
merely annotating the wrapper would incorrectly admit unchecked aliases.

## Candidate event formalism

This access-based event vocabulary is a candidate formalism, not an accepted
ordinary-memory baseline. It describes what each access and checked sharing
protocol guarantees, without requiring the C++ partition between ordinary and
atomic objects or importing its unsafe-race consequences. A particular API
may designate atomic accesses or define permitted ordinary access under a
protocol. The coherence rules below describe a location with one specified
access width and compatible non-tearing accesses. Which ordinary and explicitly
ordered accesses share those rules, especially for overlapping widths, still
needs specification. A dedicated atomic storage type is not a prerequisite.

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
unordered non-tearing accesses and define their observations; the precise
supported protocols remain to specify. "Data race" in a comparison with
another language retains that language's definition, not an implied Terrum
undefined-behavior rule.
Accesses overlapping initialization, destruction, or reclamation must also
obey the object's lifetime contract. Read/read overlap alone is not a conflict.

The proposed safe-language rule is to reject or prevent conflicts that fall
outside a checked concurrent-access contract. A non-tearing guarantee alone
does not establish such a contract, order independent locations, or make an
update indivisible. Safe code may use ordinary accesses within an explicitly
permitted synchronization or sharing protocol; it may not assume that an
uncontracted access is a polling, publication, or compound-update mechanism.

Unsafe code, native code, and foreign devices can violate checked access
contracts. The recommended boundary is local: specify which value and ordering
guarantees remain at a violating location, without making unrelated code
globally undefined solely because one location races. This proposal does not
import C++'s rule that an ordinary data race makes the program undefined.
In particular, do not silently withdraw an ordinary-access non-tearing
guarantee merely by calling the access a data race. Memory-safety guarantees
still apply only where independently established; a data race does not justify
dereferencing a dangling pointer, accessing a destroyed object, or bypassing
bounds and lifetime rules. This bounded-local-effects direction is preferred over
whole-program undefined behavior, but its exact diagnostic and optimization
consequences remain a language-design decision.

This is not a promise of fault containment. A raced value can influence later
control flow, indexes, and other writes; its consequences can propagate beyond
the original location. If unsafe code corrupts a pointer, lifetime, or runtime
metadata, this policy does not restore the invariant or make continued
execution safe. Before adopting the recommendation, specify allowed torn
representations, remaining type-safety guarantees, and which optimizer
assumptions survive exposure to unsafe code. A claim of local race semantics
cannot coexist with an optimizer silently treating every unsafe race as
permission for arbitrary whole-program behavior.

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

Exact formal wording and how initialization, destruction, and storage
replacement interact with `mo` must be settled before this becomes normative.
The intent is to provide a compact, auditable coherence model rather than
inherit every corner of C++'s release-sequence, dependency-consume, and fence
rules by accident.

Within the explicit-order candidate, one option is direct release-to-acquire
communication only:
a relaxed RMW does not implicitly relay an earlier release's publication.
An acquire-release RMW that reads a release can relay effects through explicit
acquire, sequencing, and release edges. This is a deliberate conservative
difference from importing a release-sequence rule. No dependency-only
`consume` ordering is proposed.

## Candidate explicit-order surface

The source API is open. One option is per-operation
`relaxed`, `acquire`, `release`, `acq_rel`, and `seq_cst` ordering on checked
atomic objects, or equivalent named operations. Another is a .NET-style surface
with an ordinary-access baseline, ordered loads/stores, and strongly ordered
interlocked operations. The table and litmus outcomes here describe the
explicit-order candidate, not a selection between those designs.

Convenience interlocked operations should have a strong, clearly documented
default. Whether that is a formally specified SC order or a particular
full-fence contract must be decided, not inferred from their names. It does not
determine the baseline indivisibility of ordinary fields.

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
below. The detailed
interaction between SC operations and non-SC modifications must be formally
specified rather than summarized as “everything is sequentially consistent.”
In particular, relaxed atomics remain relaxed even in a program that uses SC
operations elsewhere.

A failed compare-exchange is a read, not a release of a value it did not store.
Its failure ordering must therefore be a supported load ordering, never release
or acquire-release. The API must specify both success and failure ordering and
whether spurious failure is possible. Recommend that only explicitly weak
operations permit spurious failure; successful weak operations still provide
their full atomicity/order contract. Exact legal ordering pairs and value/bit
comparison rules belong to the atomic profile, not to arbitrary overload code.

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
are SC. The exact all-SC statement and pseudocode should be included in any
eventual formal test suite. This permissive acquire/release result is a choice
of the candidate model, not a conclusion that C#/.NET or every target mapping
admits it. The selected baseline's propagation, ordering, and multicopy-atomicity
guarantees must be reconciled with it before adoption.

### Fences, `volatile`, and interlocked operations

A fence is not a cache flush and does not force an immediate read by another
execution. Its source contract must nevertheless describe real ordering
effects, not dismiss them because no C++ atomic object is named. A .NET-style
full fence and a C++ `atomic_thread_fence` obtain their meaning from their
respective models. A Terrum fence would need a specified scope and interaction
with ordinary accesses, ordered accesses, RMWs, and communication. If the
explicit-order candidate is chosen, its `rf`/`sw`/SC rules must include fences.

C#-style acquire/release volatile accesses are a legitimate shared-RAM design
option. They can publish ordinary payload through the specified communication
pattern, but do not make a compound update indivisible or establish mutual
exclusion. C++/Rust volatile access does not supply that inter-thread ordering.
Consequently, "volatile is not atomic" and "volatile cannot publish" are not
adequate unqualified rules. Choosing a Terrum spelling must follow the contract,
not select it accidentally. Whether to expose a keyword, explicit ordered
access APIs, or standalone fences remains open.

Interlocked exchange, compare-exchange, and arithmetic updates provide atomic
RMW behavior with an additional ordering contract. An interlocked API family
may also contain atomic reads or barrier operations; not every member is
necessarily an RMW. The .NET full-fence contract is a useful comparison, not an
automatic proof of the SC rules in the candidate above. Backend intrinsics
and runtime helpers must preserve whichever contract Terrum actually selects.

Device-register access should still use a distinct platform-checked API with
access width, side-effect, and ordering contracts. A shared-RAM volatile
contract does not automatically provide MMIO semantics.

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

The agreed baseline covers naturally aligned primitive loads/stores no wider
than the machine pointer, eligible trivial wrappers of those primitives, and
managed references. It applies to ordinary coherent memory and the specified
CPU observers. An `int32` storage location therefore has atomic ordinary
loads/stores on the intended 32-/64-bit target profiles. Alignment and suitable
lowering are implementation obligations. Do not describe ordinary accesses as
non-atomic merely because they are not operations on a dedicated atomic type.
This does not promise whole-aggregate atomicity or automatic interoperability
with foreign bytewise writers.

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
references, ownership or constrained wrapper values. API names remain open.

Equal layouts or overlapping unions are not automatically safe. A narrowly
checked union rule, potentially for matching fields, remains a proposal: it
must define active-member access, initialization, representation validity,
aliasing and lifetime. C and C++ common-member or union-punning rules are not
implicitly imported. Matching field declarations alone do not grant permission.

The compiler may use overlapping machine accesses, wider instructions, or
register reinterpretation internally when it preserves the source contract.
It may not introduce observable tearing, invalidate an alias, touch storage
outside its permission, or overwrite an independently accessible neighbor.
Unsafe source-level reinterpretation and a proven internal lowering are
different permissions.

### Wider atomics and progress

For the dedicated-atomic-type option, a declared type must have correct atomic
semantics on the selected profile, possibly through a runtime lock, or be
rejected for that target. A lock-backed implementation must clearly advertise
that it is not lock-free and must not be used in signal/interrupt handlers,
lock-free algorithms, or other contexts where reentrancy or progress matters.
Lock freedom is a separate performance/progress property, never a prerequisite
for the abstract atomic value/order guarantee.

All accesses to a lock-backed atomic must use the same synchronization protocol,
including across modules. Mixing a runtime-lock operation with a foreign native
atomic on the same bytes is not automatically interoperable. Runtime dispatch
and helper selection must preserve the declared representation and coordination,
not silently introduce different per-module locks or change an exported layout.

Ordering also does not promise scheduler fairness, bounded latency, or eventual
success of an arbitrary polling loop. Such liveness/progress guarantees need
their own primitive/runtime contract. A lock-free primitive does not make every
algorithm built from it wait-free.

Misaligned atomic access should be rejected unless the target profile
explicitly guarantees the exact operation. The compiler must not silently
split an access promised to be indivisible into separately observable parts.
Combining smaller accesses, even individually non-tearing ones, does not
create a larger atomic snapshot or update. A supported wide atomic must be
represented as one indivisible
operation by contract or not be available.

In the dedicated-object candidate, distinct atomic objects must not overlap
unless an explicit profile defines a
single compatible access protocol. Two atomic operations on incompatible
overlapping representations are invalid even though neither is ordinary.
Replacing an object creates a new lifetime and modification order; reusing an
address is not permission for readers from the old lifetime to keep accessing it.
Initialization must be completed and published before concurrent atomic use.

`compareExchange` and other read-modify-write operations do not solve object
reclamation. A pointer can be atomically removed while a reader still uses its
pointee; ABA can make a location return to the same bit pattern after
intervening changes. Safe reclamation needs an independently checked
ownership/epoch/hazard/lease protocol. Atomic pointer bits do not keep a
managed object rooted or a native allocation alive.

If atomic references to managed objects are proposed, they need collector-aware
loads/stores/RMWs, exact roots across safepoints, write barriers, and publication
semantics. A collector write barrier is not a memory-ordering edge, and an
acquire/release edge does not replace a GC barrier. Raw native pointers and
interior pointers need separate lifetime and reclamation contracts. The proposal
is to defer general concurrent managed-reference APIs until this
contract is established. Ordinary managed-reference loads/stores are included
in the agreed non-tearing baseline; that does not by itself provide GC-safe
rooting across safepoints, ordered publication, atomic RMW, or reclamation.

## Architectural and microarchitectural boundaries

The language model must be architecture-neutral. A target profile maps its
operations to verified ISA/runtime sequences and states which profiles it
supports. The following is guidance for that work, not a frozen instruction
selection table. Ordinary application code relies on the checked language
contract; it should not need to reason about cache protocols or instruction
sequences. These details matter to implementers validating a profile and to
programs that explicitly use low-level concurrent or platform-specific
operations. Claims below apply only to the stated memory type and observer
domain, not to every device or address space on the same processor.

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

Store buffers, load speculation, store forwarding, cache-coherence
transactions, and writeback queues describe implementation behavior. A
processor may forward a prior store to a later local load without that store
having reached another core. Cache writeback is not publication. These
microarchitectural mechanisms explain costs and why ordering instructions may
be required; they do not define Terrum's `hb`, `mo`, or `rf` relations.

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
contract, not prove arbitrary foreign machine code correct. If trusted unsafe
implementations may provide capabilities, their obligations and provenance must
be distinguished from machine-checked implementation proofs; a nominal attribute
cannot disguise that trust. Exact admission of such providers remains an
approval decision, not a claim that all callers must themselves be unsafe.

Cross-language synchronization additionally needs compatible atomic object
layout, supported orders, lifetime/reclamation, and helper/progress protocols.
A matching function calling convention alone does not establish those facts.

## Decisions for approval

| Question | Recommendation in this proposal | Alternatives / remaining decision |
|---|---|---|
| Type opt-in spelling and proof form | Keep `[Send]`/`[Sync]` as provisional vocabulary; require checked complete-form evidence and conditional generic proofs. | Different names or sealed capability interfaces; whether imported proofs use compiler certificates or trusted unsafe declarations. |
| Default sharing | Ordinary mutable values and `List<T>` are not concurrently shareable. Readonly alone is insufficient. | Permit selected immutable-by-construction types through a separately verified deep-immutability capability. |
| Ordinary-access baseline (agreed direction) | Naturally aligned primitive loads/stores up to machine pointer size, their trivial wrappers, and managed references are non-tearing; small multi-field aggregates do not inherit whole-value atomicity. | Concrete target representations, checked wrapper eligibility, compiler transformations, coherence and publication beyond access indivisibility. |
| Unaligned and overlapping access | Explicit target-defined unaligned operations; overlapping typed storage requires unsafe code or a specific checked abstraction. Value-level bit helpers do not introduce aliased views. | Supported unaligned atomic widths/boundaries, representation-helper APIs and any narrowly checked union cases. |
| Conflicts outside a checked contract | Prevent them in safe code; specify unsafe-access outcomes without poisoning unrelated code or silently withdrawing baseline guarantees. | Which non-tearing, value, type-safety, and optimization guarantees remain, and how consequences can propagate from raced values. |
| Stronger access model and API | Keep .NET-style ordered access/interlocked and explicit per-operation-order designs distinguishable; the event model above is a candidate, not the baseline. | Strong-default contract, atomic-object versus access-based rules, compare-exchange ordering, release sequences, and reconciliation of litmus outcomes with target guarantees. |
| Fences and volatile | Consider C#-style acquire/release accesses for shared RAM separately from C++/Rust volatile I/O semantics; specify any full-fence contract explicitly. | Keyword versus APIs, standalone fences, and formal relation to ordinary and explicitly ordered accesses; device access remains a separate contract. |
| Atomic widths and progress | Publish exact target profiles; correct atomic semantics may use a lock, with lock-free status separately stated. | Restrict the language to universally lock-free operations or reject types unsupported by any selected profile. |
| Managed-reference atomics | Ordinary reference accesses retain the agreed non-tearing baseline; defer general concurrent reference APIs until roots, barriers, RMW, and reclamation are specified. | Add only a runtime-owned rooted atomic-reference abstraction. |
| SIMD/non-temporal operations | No implicit whole-vector indivisibility; explicit target profile for streaming operations and completion. | Portable or target-specific non-tearing widths and named vector-atomic/non-temporal APIs. |
| Device/DMA/persistence | Separate unsafe or platform-checked APIs and ordering domains. | Which standard library layer owns those APIs and which operating systems/boards are supported. |
| Foreign contracts | Unverified native/device claims remain unsafe and cannot create checked capabilities. | Define versioned FFI attestations and their trust/compatibility policy. |

## Primary references consulted

These references inform the proposal but do not select Terrum semantics. The
.NET document is one useful runtime comparison, not the source of Terrum's
baseline or a specification to inherit silently. C++ and Rust references
explain distinct explicit-atomic and volatile contracts; they do not select
Terrum's ordinary-access or unsafe-race model. Linux barrier/DMA guidance is
operational platform guidance, not a portable language memory model.

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
