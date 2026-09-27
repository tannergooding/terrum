# Memory model proposal

> **Proposal for review.** This document is a concrete design proposal, not an
> accepted language specification, implementation claim, ABI commitment, or
> promise about any current Terrum target. Examples are pseudocode, not Terrum
> syntax or library APIs. The recommendations below are intended to make the
> remaining decisions reviewable; names in square brackets are provisional.

Call sequencing, reference equivalence, output publication, and proof effects
are covered by the companion [contract review](language-contract-review.md).
[Implementation](implementation.md) remains the executable support boundary;
this document does not claim a concurrent runtime or these atomic APIs exist.

## Goals and scope

Terrum should give safe programs a portable, compiler-independent account of
communication between concurrent executions. It should preserve the existing
single-threaded access model while requiring an explicit, checked capability
before a value can be transferred between threads or concurrently shared.
Readonly access is not that capability: it does not imply immutability,
thread-safety, uniqueness, purity, or safety of references reachable through
the value.

This proposal covers ordinary cacheable memory and CPU-to-CPU communication.
It deliberately separates that model from device registers, DMA, persistent
storage, and side-channel security. Those domains have different observers,
completion rules, and platform contracts; a CPU memory fence must not be
presented as a universal operation on them.

The central recommendation is:

1. Safe ordinary memory is race-free. Ownership, checked loans, synchronized
   abstractions, and atomic operations establish that property.
2. Atomics have explicit per-operation ordering. Relaxed atomics are atomic
   and coherent per location, but do not publish unrelated memory.
3. A checked type capability is required for transfer or concurrent sharing.
   It is checked over the complete concrete type form and all authority
   exposed by its API; an annotation is not an author assertion.
4. Target profiles define which atomic representations are supported and
   their alignment, process scope, and lock-free properties. The language
   does not infer portable guarantees from pointer size or natural alignment.
5. No general `volatile` escape hatch or standalone cache-flush fence is
   proposed for ordinary shared memory. Prefer atomics and explicit
   synchronization with a real communication relation.

## Sharing capabilities are distinct

The terms below describe separate permissions. `[Send]` and `[Sync]` are
provisional labels already used in design discussion, not selected syntax.

| Capability | Proposed meaning | Does not imply |
|---|---|---|
| Transferable (`[Send]`) | A value and its live dependencies may cross an execution-thread boundary under a checked transfer contract. Transfer must account for every still-usable authority to mutable state. | Concurrent sharing, deep immutability, or thread affinity. |
| Shareable (`[Sync]`) | The type's exposed operations are safe when aliases are used concurrently under the memory model. The proof covers reachable authority and generic arguments, not just the outer object's fields. | Exclusive access, immutability, or freedom from logical races/reentrancy. |
| Thread-affine | A value may be used only on its designated thread or executor. This is a separate capability/restriction, not a synonym for `Send` or `Sync`. | Transferability or concurrent sharing. |
| Readonly | This access path cannot mutate the immediate place it denotes, as defined by the access model. | Deep readonly, immutable reachable objects, race freedom, or safe publication. |

Ordinary mutable access remains local and nonconcurrent by default; it is not
intrinsically tied to an operating-system thread. An ordinary `List<T>` is not
concurrently shareable. It can become shareable only through an explicitly
opted-in, checked complete-form contract, for example if every shared operation
is internally synchronized and all values, callbacks, iterators, and
references it exposes satisfy the corresponding contract. Merely wrapping a
list in a readonly handle or lock-shaped type does not prove this.

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

## Abstract memory events

The following event model is the proposed foundation for ordinary memory.
Names are explanatory, not source-language keywords.

- A dynamic memory event identifies an execution, a storage object and byte
  range, an operation (read, write, or read-modify-write), and whether the
  operation is ordinary or atomic. Object creation, initialization,
  destruction, and reclamation also affect the validity of accesses.
- **Sequenced-before** (`sb`) orders evaluations and memory events in one
  execution according to the source program's abstract sequencing rules.
  Reordering machine instructions is allowed only when it preserves the
  language's observable behavior.
- **Synchronizes-with** (`sw`) is created by specified communication
  operations. This proposal includes a release operation on an atomic object
  synchronizing with an acquire operation on that same object when the acquire
  reads the value written by the release. A relaxed operation does not create
  this edge. Other synchronization primitives must explicitly define their
  acquire/release edges.
- **Happens-before** (`hb`) is the transitive closure of `sb` and `sw`, plus
  any separately specified thread-start, thread-join, lock, or task-completion
  synchronization edges. A specified thread-start operation can publish
  arguments, and a specified join can publish completion effects. Queuing work
  through an unspecified mechanism, sharing pointer bits, cancelling work, or
  observing that a cache line changed does not supply such a contract.
- Every atomic object has a **modification order** (`mo`): a total order of its
  atomic modifications, consistent with `sb` for modifications by one
  execution. It is per object, not one global order for all relaxed atomics.
- An atomic read has a **read-from** (`rf`) source: one modification of that
  same atomic object, or its initial value. The allowed source is constrained
  by modification order, happens-before, and atomic coherence. An atomic
  read-modify-write is one indivisible read and write event; it cannot read
  from an arbitrary older modification and then overwrite a later one.

For ordinary, race-free locations, a write that happens-before a read is
visible to it unless a later write to the same location also happens-before
that read. Where ordinary accesses are unordered and conflict, the program has
a data race as defined below and cannot use this rule to infer a value. This
proposal does not make ordinary memory globally sequentially consistent across
threads.

### Data races and safe/unsafe boundaries

Two events conflict when they access overlapping bytes of the same live
storage and at least one is a write. They form a **data race** if they are in
different concurrent executions, conflict, are not ordered by `hb`, and at
least one is an ordinary non-atomic access. Accesses that overlap an object's
initialization, destruction, or reclamation also conflict with access to that
object's storage. Atomic versus ordinary overlapping access is a race; an
atomic operation does not make a neighboring or overlapping ordinary access
safe.

The proposed safe-language rule is to reject or prevent every such race using
checked ownership, loans, synchronization contracts, or atomic locations.
The compiler must not assume that a racy ordinary load is a stable snapshot,
nor may safe code use ordinary racy access as a polling or publication
protocol.

Unsafe code, native code, and foreign devices can violate the rule. The
recommended boundary is local: conflicting locations then have no portable
value or ordering guarantee, but unrelated code does not become globally
undefined solely because one location races. Memory-safety guarantees still
apply only where independently established; a data race does not justify
dereferencing a dangling pointer, accessing a destroyed object, or bypassing
bounds and lifetime rules. This local no-guarantee policy is preferred over
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

No-out-of-thin-air rule: an atomic read's value must be justified by an initial
value or a write event whose value is computed from already justified inputs. A
compiler or runtime must not invent cyclic speculative values merely because
they make a racy program self-consistent. This rule does not promise a
particular value for a data-racy program or turn a potentially torn ordinary
read into an indivisible observation. Constraints on torn representations
belong to the explicit unsafe-race decision above.

Justification must include the control conditions that cause a write to occur,
not merely its stored bit pattern. Speculating a conditional store cannot make
the assumed read that enabled it justify itself. This is a causality condition,
not a requirement that every reads-from edge follow wall-clock observation
order; ordinary permitted weak-memory reorderings remain possible.

### Atomic coherence

For every atomic object, the model should enforce per-location coherence:

- Atomic modifications have one `mo`; two modifications ordered by `hb` must
  appear in that order in `mo`.
- A read cannot observe a modification older than a modification that happens
  before the read.
- If an atomic read happens before a later modification, the read's `rf`
  source must precede that modification in `mo`.
- If one read happens before another read of the same atomic object, the
  second cannot read from an older `mo` source. This includes `hb` across
  executions, not just two reads in one execution.
- A read-modify-write reads the modification immediately preceding its own
  write in `mo`; there is no intervening modification to that object.

Exact formal wording and how initialization, destruction, and atomic object
replacement interact with `mo` must be settled before this becomes normative.
The intent is to provide a compact, auditable coherence model rather than
inherit every corner of C++'s release-sequence, dependency-consume, and fence
rules by accident.

For the first profile, recommend direct release-to-acquire communication only:
a relaxed RMW does not implicitly relay an earlier release's publication.
An acquire-release RMW that reads a release can relay effects through explicit
acquire, sequencing, and release edges. This is a deliberate conservative
difference from importing a release-sequence rule. No dependency-only
`consume` ordering is proposed.

## Proposed atomic ordering surface

The source API is open. The recommended concepts are per-operation
`relaxed`, `acquire`, `release`, `acq_rel`, and `seq_cst` ordering on checked
atomic objects, or equivalent named operations. This is design vocabulary,
not accepted grammar.

Recommend SC defaults for convenience atomic/interlocked APIs, with weaker
ordering explicitly requested. That default would apply to atomic APIs, not
turn ordinary fields into atomic locations.

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
eventual formal test suite.

### Fences, `volatile`, and interlocked operations

A fence is not a cache flush and does not, by itself, make another execution
observe an operation. Any future source-level fence must identify the atomic
communication relation that lets it participate in synchronization; a
standalone “full fence” must not manufacture `sw` without a matching event or
promise global visibility by magic. Processor barriers selected by a backend
may implement a source-level ordering contract, but their existence does not
create one.

The proposal is to defer a standalone source fence API until a concrete use
case requires it. Acquire/release atomics and well-defined interlocked
read-modify-write operations are sufficient for common lock, publication, and
counter protocols. If fences are later exposed, their relation to atomic
reads-from and SC order must be explicit and independently tested.

Do not use `volatile` as a safe substitute for atomics. A volatile access
alone does not make a compound update indivisible, establish mutual exclusion,
or publish unrelated ordinary memory. The proposal is to have no general
`volatile` keyword for shared RAM. Device-register access, if supported, should
use a distinct platform-checked device API with access width and ordering
contracts, not ordinary volatile fields.

Interlocked operations are atomic read-modify-write operations with an
explicitly documented ordering strength. A source API may offer a default
strong operation for ease of use, but it must not hide whether it is SC or
only acquire/release. Backend intrinsics and runtime locks must preserve that
same contract.

## Atomic types, alignment, and progress guarantees

Atomicity is a language contract, not a guess from natural alignment. Each
target profile must publish a machine-readable capability table covering:

- supported atomic value types and widths;
- required address alignment and storage representation;
- whether each operation is lock-free, wait-free, or may use a runtime lock;
- process and shared-memory scope, if any;
- availability on each CPU/OS feature profile and behavior when the required
  feature is absent;
- managed-reference/root-barrier support, if reference atomics are offered.

The language must not promise “all aligned pointer-sized accesses are atomic”
or “all values up to pointer size are atomic” without a profile guarantee.
Ordinary memory accesses are not atomic merely because a particular processor
usually emits one instruction for them.

Recommended behavior is that a declared atomic type either has correct atomic
semantics on the selected profile, possibly through a runtime lock, or is
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
split one atomic access into ordinary parts. Overlapping atomic objects with
different widths or mixed atomic/non-atomic access are not a way to compose a
larger atomic. A supported wide atomic must be represented as one indivisible
operation by contract or not be available.

Distinct atomic objects must not overlap unless an explicit profile defines a
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
interior pointers need separate lifetime and reclamation contracts. The default
proposal is not to make arbitrary managed references atomic until this contract
is established.

## Architectural and microarchitectural boundaries

The language model must be architecture-neutral. A target profile maps its
operations to verified ISA/runtime sequences and states which profiles it
supports. The following is guidance for that work, not a frozen instruction
selection table.

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

- Ordinary vector loads and stores are non-atomic unless an explicitly
  specified atomic-vector operation says otherwise.
- A vector memory operation that overlaps a concurrent write is governed by
  the same byte-range race rule as scalar operations. A compiler may not claim
  atomic snapshot semantics merely because it selected one vector instruction.
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
| Racy ordinary accesses | Reject in safe code; unsafe races have local no-portable-guarantee semantics without poisoning unrelated code. | Whether the local rule is specified as undefined behavior for that location or as a more tightly constrained set of outcomes. |
| Atomic orders | SC defaults for convenience atomic APIs; explicit relaxed/acquire/release/acq_rel alternatives, direct release/acquire edges, and no implicit release-sequence relay. | Exact API, compare-exchange order pairs, and formal interaction of SC with non-SC writes. |
| Fences and volatile | No general `volatile`; defer source-level standalone fences; provide checked atomics/interlocked operations first. | Add fences only with explicit `rf`/`sw` relation; use separate device access contracts instead of volatile. |
| Atomic widths and progress | Publish exact target profiles; correct atomic semantics may use a lock, with lock-free status separately stated. | Restrict the language to universally lock-free operations or reject types unsupported by any selected profile. |
| Managed-reference atomics | Defer arbitrary reference atomics until roots, barriers, RMW, and reclamation are specified. | Add only a runtime-owned rooted atomic-reference abstraction. |
| SIMD/non-temporal operations | Non-atomic vector memory by default; explicit target profile for streaming operations and completion. | Add named vector-atomic or non-temporal APIs only for demonstrated library needs. |
| Device/DMA/persistence | Separate unsafe or platform-checked APIs and ordering domains. | Which standard library layer owns those APIs and which operating systems/boards are supported. |
| Foreign contracts | Unverified native/device claims remain unsafe and cannot create checked capabilities. | Define versioned FFI attestations and their trust/compatibility policy. |

## Primary references consulted

These references inform the proposal but do not select Terrum semantics. The
.NET document is a useful runtime-specific comparison, not a specification
Terrum should silently inherit. Linux barrier/DMA guidance is operational
platform guidance, not a portable language memory model.

- [.NET runtime memory model](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md) — checked for its distinctions among atomic accesses, ordering, volatile, unmanaged memory, and runtime-specific guarantees.
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
