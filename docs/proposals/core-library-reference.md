# Terrum core-library proposal

> [!IMPORTANT]
> Proposal only: signatures and inventories are candidate designs, not
> approved APIs or claims of implementation.

This proposal describes candidate library declarations and dependency layers.
Accepted semantic direction is identified separately from proposed signatures;
exact syntax and several API contracts remain open. Examples do not establish
conformance or implementation. This is not an ABI layout specification.

The [grammar proposal](grammar-spec.md) and
[approved current semantic IL/metadata design](../il-metadata.md) are separate
documents; neither adopts the signatures here. Related accepted direction is
described in [language](../language.md), [libraries](../libraries.md),
[lifetimes](../lifetimes.md), [type families](../type-families.md),
[conversions](../conversions.md), and [memory model](../memory-model.md).

## Scope, status, and notation

**ACCEPTED DIRECTION** identifies a semantic constraint also stated in the
canonical language references; it does not approve a nearby declaration.
**PROPOSED** marks a candidate signature or API contract. **OPEN** marks a
decision not made here. Signature syntax is illustrative unless an accepted
source form is explicitly identified. No proposal-only declaration is required
for language conformance. Conformance to a library profile requires adoption
of its declarations together with their contracts and feature dependencies;
these candidate inventories establish no such conformance.

## Dependency layers

**Accepted layering direction:** distinguish the genuinely minimal
requirements for core language operation from a standard application-library/
runtime surface and from feature-specific dependencies. Illustrative
foundational forms include `ref<T>`, `ptr<T>` and `int32`; this is not an
exhaustive required-type or API list. The extensive inventories in this
reference, especially the application services proposed in §13, are not
mandatory for language conformance and do not require a universal object root,
heap, tracing GC, scheduler or OS services solely for the language.

The **Core**, **Foundation** and **Application** headings are proposed library
dependency layers, not standardized runtime-profile names. Features
that need runtime facilities retain those dependencies and their checked
contracts when used. An environment without a required facility cannot claim
that feature by silently weakening its semantics or substituting an implicit
fallback. This direction allows embedded/kernel environments in principle; it does not
require every environment to support every feature. Exact baseline/API mapping
and feature-to-runtime support remain open.

| Candidate cumulative layer | Representative surface | Required substrate |
|---|---|---|
| **Core** | Numeric primitives and protocols, `void`/options, `Span<T>`, immutable code-unit/text views, static eager algorithms, shape/view and SIMD contracts | Checked value execution, bounds/access and cleanup/failure services; no ordinary-path allocation or OS facility |
| **Foundation** | Explicitly owned buffers/vectors/maps, allocators/arenas/pools, owning native `Text`, persistent tensor-shape metadata and storage leases | Core and a caller/host-supplied allocator; no mandatory tracing collector |
| **Application** | Managed list/linked list/dictionary/set/queue, ordinary managed `Text`, tasks, streams/terminal, files/network, globalization and serialization | Foundation plus tracing and appropriate host services |
| **Platform leaves** | COM/Windows/DirectX and other native bindings | Only layers their adapters actually use; no reverse dependency on platform APIs |

## Reading declarations

- `mut` grants a writable place or receiver; `write` qualifies authority through a reference/view. They are not interchangeable. Readonly is the declaration default.
- `ref<T>` is an ordinary reference/loan. `init ref<T>` names a vacant output destination, not writable access; the accepted call-site form is `Create(init value)`. Exact grammar and output contracts remain open.
- `write Span<T>` lends write access to backing slots; `mut Span<T>` only permits descriptor rebinding. `Span<write C>` is separately writable through each retained handle. Generic substitutions preserve their complete qualification.
- `[Copy]`, `[Sized]`, `[ManagedStorable]`, `[TextCodeUnit]`, and `[SimdElement]` are checked properties/capabilities, not user assertions inferred from an interface name.
- A signature does not express all dependencies. A returned view retains its backing/root/lease and any shape metadata. Ownership, allocation, checked failure, effect and task-completion rules are stated under the relevant family.

## 1. Fundamental values, presence and outcomes

`void` is the zero-data result type, with value `()`, usable as `func<void>` or `Task<void>`; there is no public `unit` or `never` primitive. There is no universal `default(T)` and no required universal root with `Equals`, `GetHashCode`, or `ToString`.

The accepted semantic direction normalizes optional `T?` substitutions: `T?` with already-optional `T` remains one optional layer. `Option<T>` is an optional projection/alias intent for that same normalized presence type, not a nominal `Some(T)` wrapper; its exact source spelling remains open. `Option<void>` still represents absent versus present `()`.

A distinct nominal union can preserve distinctions that optional normalization cannot, including missing versus present-null by making its payload itself optional. These are illustrative type shapes, not approved declaration grammar:

```terrum
// Optional alias/projection intent; exact alias syntax is pending.
Option<T> := T?;

// Separate nominal cases preserve Missing versus Found(null).
public nominal union Lookup<T>
{
    Missing;
    Found(T? value);
}
```

`Lookup<T>.Missing` remains distinct from `Lookup<T>.Found(null)` when `T` is optional; `Option<T>` itself does not add a second presence state. Niche representations must be declared and validated; no invalid bit pattern, padding, or null encoding is available implicitly. General `init ref<T>` does **not** universally mean false leaves the destination vacant: a declaration must specify initialization/publication outcomes and the checker must verify them. For example, an API contract for `bool TryParse(TextSpan, init ref<T>)` may specify false => vacant and true => initialized; the signature alone does not convey that rule.

Expected domain alternatives should be represented by a status/union/result contract; the proposed `Result<T,E>` shape is defined once in §13. Exceptional failures propagate. `require` can lift a declared false outcome into checked failure, but no general library-wide exception hierarchy/object ABI is selected. Catch/recovery is not implied by `require`.

## 2. Explicit equality, keying and formatting witnesses

These are opt-in behavior contracts, not automatic primitive or root
conformance. Bodyless interface members require a checked witness; a body
would supply an overridable default. The sketches use the proposed grammar.
Equality intended for a map key must be paired with a hash that agrees on
equivalent values.

```terrum
public interface IEqualityComparer<T>
{
    bool Equals(ref<T> left, ref<T> right);
}
public interface IKeyComparer<T> : IEqualityComparer<T>
{
    uint64 GetHashCode(ref<T> value);
}
public interface IParsable<TSelf>
    where TSelf : IParsable<TSelf>
{
    static bool TryParse(TextSpan text, init ref<TSelf> value);
}
public interface IFormattable
{
    bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
```

`IEqualityComparer<T>` and `IKeyComparer<T>` appear in the algorithm inventory. Equality is not automatically key equality, identity, or bitwise identity. IEEE floating `==` leaves NaN unequal and signed zeros equal; proposed numeric-key semantics equate all NaNs and both zero signs and hash consistently. Decimal key hashes also normalize numerically equal cohorts. A bitwise comparer is a different policy. Formatting is explicit and typed; it does not force intermediate text allocation or imply a debug/display string API.

`int32.GetHashCode()` is a concrete primitive member, not evidence of universal root inheritance and not itself a contradiction. The actual open choice is whether it promises the same numeric-key contract/name as the float/decimal `KeyEquals`/`GetKeyHashCode` pair or is a separate utility; either way, generic map-key use still needs a compatible explicit equality/hash witness.

## 3. Text, encoding and writers

The single proposed view family is `TextSpan<TCodeUnit = void>`: bare `TextSpan` means `TextSpan<void>`, an encoding-erased *validated immutable text* view. Members are `void`, `char8`, `char16`, and `char32`; only the three code-unit members satisfy `[TextCodeUnit]` and expose contiguous code-unit access.

```terrum
public struct Span<T> : IContiguousSequence<T>
{
    public nuint Length { get; }
    // Candidate borrowed-place shape; the by-value alternative remains open.
    [MatchedAccess] public ref<T> this[nuint index] { get; }
    [MatchedAccess] public Span<T> Slice(nuint start, nuint length);
    Span<T> IContiguousSequence<T>.AsSpan(); // explicit interface projection
}
public interface IContiguousSequence<T>
{
    Span<T> AsSpan();
}
public struct TextSpan<TCodeUnit = void> : IFormattable
{
    public bool IsEmpty { get; }
    public bool EqualsOrdinal(TextSpan other);
    public bool EqualsOrdinal(rune other);
    public int32 CompareOrdinal(TextSpan other);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
public extension TextSpan<TCodeUnit> : IContiguousSequence<TCodeUnit>
    where TCodeUnit : [TextCodeUnit]
{
    public nuint Length { get; }
    public Span<TCodeUnit> AsSpan();
    public TextSpan<void> AsTextSpan();
}
public extension TextSpan<void>
{
    public bool TryProject<TRequested>(init ref<TextSpan<TRequested>> result)
        where TRequested : [TextCodeUnit];
}
public struct rune : IFormattable
{
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}

```

```terrum
public interface ITextWriter
{
    mut bool TryWrite(TextSpan text);
    mut bool TryWriteScalar(rune value);
    mut bool TryWriteFormatted<TValue>(ref<TValue> value, TextSpan format)
        where TValue : IFormattable;
}
public mut unique struct SpanTextWriter<TCodeUnit> : ITextWriter
    where TCodeUnit : [TextCodeUnit]
{
    [Captures(nameof(destination))]
    public SpanTextWriter(write Span<TCodeUnit> destination);
    public nuint Written { get; }
    public mut bool TryWrite(TextSpan text);
    public mut bool TryWriteScalar(rune value);
    public mut bool TryWriteFormatted<TValue>(ref<TValue> value, TextSpan format)
        where TValue : IFormattable;
}
public class Text : IFormattable
{
    public TextSpan AsSpan();
    [TextTarget(nameof(content))]
    public static own<val<Text>> Create<TContent>(TContent content)
        where TContent : IFormattable;
}
```

Conditional family/extension syntax above is pseudocode pending grammar/custom-family rules. Typed erasure and successful projection preserve actual encoding, validation, access, backing/root/lease and lifetime without copying/transcoding. Mismatch exposes no typed view. `AsSpan()` drops text validity as a view, never immutability or backing lifetime; there is no `Span<void>`. Ordinal comparisons operate over scalar sequences without normalization, culture or case folding. `EqualsOrdinal(rune)` requires the *whole* view to be precisely one scalar. `rune` is target-typed; `char8` admits only ASCII as a standalone unit, `char16` a nonsurrogate BMP scalar, and `char32`/`rune` any scalar. Character-literal syntax is design-only.

`SpanTextWriter` holds an exclusive loan to caller-provided storage; `Written` is accepted code-unit count. A refused fragment does not modify storage for that fragment, but prior fragments remain. `Text.Create` validates/freezes text and may transfer eligible exclusive backing; final representation and builder contract remain open.

The proposed TRRM String row has a 4-byte encoding-flags/unit-count header,
explicit code-unit terminator and padded, 4-byte-aligned data. It is not a
`Text` object laid out as `nint _length` followed by inline data: its unit
count is 24-bit, while a native `Text` length may have target-dependent width
and a different unit or scalar interpretation. A validated typed `TextSpan` could
borrow mapped row content without transcoding only while the backing artifact
remains live; an owned `Text` needs a copy/transfer or an explicitly retained
artifact lease and compatible representation. The native `Text` object layout,
including any inline-data form, remains open and is not fixed by TRRM.

## 4. Views, ownership, allocators and memory primitives

### General span and inline storage

`Span<T>` is one language-defined view type; backing/owner relationship is opaque and carried as a dependency, not as a public owner generic argument. `Length`, a borrowed indexer and slice are the useful public observations. Empty/end slices retain lease/root/lifetime. A by-value getter without a `[Copy]` restriction is not a usable contract for unique `T`: it would duplicate a value from even a readonly view. A candidate matched-access borrowed-place/ref indexer is shown in §3: read-only views yield `ref<T>`, while `write ref<T>` is available only through a write-authorized view. If property syntax cannot express that matched place, define an explicit compiler-owned place category; by-value get/copy APIs require `[Copy]`. Concrete spans deliberately do not expose `AsSpan()`; interface projection is explicit. Exact stored-view anchors and ABI are open.

```terrum
public struct InlineArray<T, const nuint N>
    where T : [Sized]
{
    [MatchedAccess] public ref<T> this[nuint index] { get; }
}
```

### Explicit native storage (representative Foundation inventory)

```terrum
public mut unique struct AllocationLease
{
    public nuint ByteLength { get; }
    public nuint Alignment { get; }
    public mut void Dispose();
}
public mut unique struct AllocatorContext
{
    public mut AllocationLease Allocate(nuint byteLength, nuint alignment);
    public mut void Dispose();
}
public mut unique struct Buffer<T>
    where T : [Sized]
{
    public nuint Capacity { get; }
    public nuint Count { get; }
    public static Buffer<T> Adopt(nuint capacity, AllocationLease storage);
    public mut void Initialize(nuint index, T value);
    public Span<T> AsSpan();
    [ViewOf(Access.Write)] public mut write Span<T> GetWriteAccess();
    public mut void Clear();
}
public mut unique struct Vector<T>
    where T : [Sized], [Copy]
{
    public nuint Count { get; }
    public nuint Capacity { get; }
    public static Vector<T> Create(AllocatorContext allocator);
    public mut void Add(T value);
    public mut void Reserve(nuint capacity);
    public Span<T> AsSpan();
    public mut void Clear();
}
```

A lease owns a specific current allocation, extent/alignment and release action; allocator context is a provider for later allocations, not the current allocation. `Adopt` validates existing storage (size product, actual alignment and necessary tracing/barrier capabilities); a release token cannot manufacture memory. `Count` is initialized elements, not capacity. Growth allocates replacement first, copies/cleans initialized prefix, publishes only after success, then releases old storage; forbidden while a view loans that allocation. Initial vector requires `[Copy]`; broader element/allocator policy is open. View/guard spellings are provisional.

### `MemoryPrimitives`

One flat static type on existing storage is accepted. Family prefixes are semantic; there are no separate `Atomic`, `Interlocked`, `Streaming` or `Cache` roots. `location` below means checked access path, not unqualified raw pointer. `T` must be an eligible representation/width for the operation and target.

```terrum
public enum MemoryOrder : int32
{
    Relaxed, Acquire, Release, AcquireRelease, Sequential
}

// Proposed richer result alongside the success-only CAS form.
public struct CompareExchangeObservation<T>
    where T : [Copy], [Sized]
{
    public bool Exchanged { get; }
    public T Previous { get; }
}

public static class MemoryPrimitives
{
    public static T AtomicRead<T>(ref<T> location, MemoryOrder order = MemoryOrder.Acquire);
    public static void AtomicWrite<T>(write ref<T> location, T value,
        MemoryOrder order = MemoryOrder.Release);
    public static T UnalignedRead<T>(Span<uint8> source, nuint byteOffset)
        where T : [Copy], [Sized];
    public static void UnalignedWrite<T>(write Span<uint8> destination, nuint byteOffset, T value)
        where T : [Copy], [Sized];

    public static void AtomicAdd<T>(write ref<T> location, T amount, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicAddGetPrevious<T>(write ref<T> location, T amount, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicAddGetUpdated<T>(write ref<T> location, T amount, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicSubtract<T>(write ref<T> location, T amount, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicSubtractGetPrevious<T>(write ref<T> location, T amount, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicSubtractGetUpdated<T>(write ref<T> location, T amount, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicIncrement<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicIncrementGetPrevious<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicIncrementGetUpdated<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicDecrement<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicDecrementGetPrevious<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicDecrementGetUpdated<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicAnd<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicAndGetPrevious<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicAndGetUpdated<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicOr<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicOrGetPrevious<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicOrGetUpdated<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicXor<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicXorGetPrevious<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicXorGetUpdated<T>(write ref<T> location, T mask, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicNot<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicNotGetPrevious<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicNotGetUpdated<T>(write ref<T> location, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicMin<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicMinGetPrevious<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicMinGetUpdated<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicMax<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicMaxGetPrevious<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicMaxGetUpdated<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static void AtomicExchange<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static T AtomicExchangeGetPrevious<T>(write ref<T> location, T value, MemoryOrder order = MemoryOrder.Sequential);
    public static bool AtomicCompareExchange<T>(write ref<T> location, T expected, T replacement,
        MemoryOrder successOrder = MemoryOrder.Sequential, MemoryOrder failureOrder = MemoryOrder.Sequential);
    public static CompareExchangeObservation<T> AtomicCompareExchangeObserve<T>(write ref<T> location,
        T expected, T replacement, MemoryOrder successOrder = MemoryOrder.Sequential,
        MemoryOrder failureOrder = MemoryOrder.Sequential)
        where T : [Copy], [Sized];
    public static void Fence(MemoryOrder order = MemoryOrder.Sequential);
    public static void CompilerFence(/* exact scope/mask open */);
    public static T NonTemporalRead<T>(ref<T> location);
    public static void NonTemporalWrite<T>(write ref<T> location, T value);
    public static void NonTemporalCopy(write Span<uint8> destination, Span<uint8> source);
    public static void NonTemporalCompleteWrites();
    public static void CacheWriteBack(Span<uint8> range);
    public static void CacheInvalidate(Span<uint8> range);
    public static void CacheWriteBackInvalidate(Span<uint8> range);
}
```

These **PROPOSED** signatures express accepted result and ordering direction;
parameter grammar, overload mapping, target range contracts, and some CAS
declarations remain open. Add, subtract, increment, decrement, AND, OR, XOR,
NOT, min and max each have `void`, `GetPrevious`, and `GetUpdated` forms;
exchange has `void` and `GetPrevious` forms. The success-only
`AtomicCompareExchange` shape is distinct from the proposed
`AtomicCompareExchangeObserve`, which returns a named
`CompareExchangeObservation<T> { Exchanged, Previous }`; distinct names avoid
return-type-only overloads. Its `T` must have a defined bitwise-comparable
representation and be eligible for a copied observation; no user `==` or
equality witness participates. Defaults are load-acquire, store-release,
strong CAS, and sequential consistency with full before/after ordering for
all RMWs/fences. One compile-time-known `MemoryOrder` selects other valid
orders; non-constant values and invalid operation/order pairs are rejected.
Atomic defaults are lock-free; lock fallback must be explicit and globally
coordinated. Checked RMW overflow leaves storage unchanged; lexical
`unchecked` selects the separately declared wrapping variant without changing
atomicity, ordering, bounds, lifetime or access permissions. Explicit
unaligned value access is not an atomic snapshot. `UnalignedRead`/
`UnalignedWrite` take a `uint8` span and byte offset, check that the complete
target-sized range is in bounds without offset overflow, and only materialize
representations valid for `T`; they never fabricate an unaligned `ref<T>`.
Non-temporal and cache operations require explicit target support and valid
backing, range, memory type, alignment, alias/concurrency and completion
conditions. A span supplies bounds/lifetime authority, not proof that hardware
effects are safe. Cache maintenance may affect whole cache lines, and
invalidation may discard dirty data; the target contract must define
line-granularity ownership and concurrency preconditions. Unsupported ranges
must be rejected or handled by an explicit supported path. These operations
do not imply publication or persistence by name. Managed-reference operations
need collector roots/barriers independently of memory order.

`GetPrevious` returns the value observed by that RMW; `GetUpdated` returns the value produced by that same RMW, not a subsequent read that might observe another writer. `[Copy]` and `[Sized]` alone do not establish raw-byte representation eligibility. The proposed ordinary byte-span unaligned overloads exclude managed-reference-containing types; arbitrary bytes cannot manufacture a managed handle or bypass collector/provenance requirements. Any collector-aware typed-memory operation needs its own explicit contract.

## 5. Collections, iteration and eager algorithms

Initial managed collections from `collections.api.terrum` (design-only):

```terrum
[CollectionBuilder(typeof(ListBuilder<>))]
public mut class List<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public nuint Count { get; }
    public bool TryGet(nuint index, init ref<T> value);
    public mut void Add(T value);
    public T this[nuint index] { get; mut set; }
    public mut void Clear();
    public mut void RemoveAt(nuint index);
    [ViewOf(Access.Write)] public mut ListWriteAccess<T> GetWriteAccess();
    [ViewOf(Access.Write, To = nameof(access))]
    public mut bool TryGetWriteAccess(init ref<ListWriteAccess<T>> access);
}
public mut unique struct ListBuilder<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public ListBuilder(nuint capacityHint);
    public mut bool TryAdd(T value);
    public mut bool TryBuild(init ref<write List<T>> value);
}
public mut unique struct ListWriteAccess<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    [ViewOf(Access.Write)] public write Span<T> Span { get; }
}
public mut class LinkedListNode<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public T Value { get; mut set; }
    [MatchedAccess] public LinkedListNode<T>? Previous { get; }
    [MatchedAccess] public LinkedListNode<T>? Next { get; }
}
[CollectionBuilder(typeof(LinkedListBuilder<>))]
public mut class LinkedList<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public nuint Count { get; }
    [MatchedAccess] public LinkedListNode<T>? First { get; }
    [MatchedAccess] public LinkedListNode<T>? Last { get; }
    public mut write LinkedListNode<T> AddLast(T value);
    public mut void AddLast(write LinkedListNode<T> node);
    public mut void AddAfter(write LinkedListNode<T> anchor, write LinkedListNode<T> node);
    public mut void Remove(write LinkedListNode<T> node);
    public mut void Clear();
    public LinkedListCursor<T> GetEnumerator();
}
public mut unique struct LinkedListBuilder<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public LinkedListBuilder(nuint capacityHint);
    public mut bool TryAdd(T value);
    public mut bool TryBuild(init ref<write LinkedList<T>> value);
}
public mut unique struct LinkedListCursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool MoveNext();
    public T Current { get; }
}
```

The by-value `List<T>` indexer is valid under its explicit `[Copy]` constraint; that does not generalize to non-Copy `Span<T>`. Private buffer/access fields are intentionally omitted; public ABI/layout is not implied. A write guard is an exclusive nonescaping loan, not a cross-thread mutex. Builders construct privately in source order and publish only on success; abandonment/failure cleans partial state. Linked nodes have stable logical identity, not stable machine addresses; one owner maximum; removal/clear detach externally held nodes. Structural mutation invalidates traversal; payload replacement need not. Listed cursor copies `T`; lending, consuming and snapshot iteration are separate protocols. Initial `[ManagedStorable], [Copy], [Sized]` restrictions are not a permanent ban on reference/affine values in every collection.

The algorithms inventory is eager and nonmaterializing; no allocation, boxing, deferral or inferred SIMD from a closed callback:

```terrum
public interface IMapper<TSource, TResult>
{
    TResult Map(ref<TSource> value);
}
public interface IPredicate<T>
{
    bool Test(ref<T> value);
}
public static class SpanAlgorithms
{
    public static bool TryIndexOf<T, TComparer>(Span<T> source, ref<T> value,
        write ref<TComparer> comparer, init ref<nuint> index)
        where TComparer : IEqualityComparer<T>;
    public static void Select<TSource, TResult, TMapper>(Span<TSource> source,
        write Span<TResult> destination, write ref<TMapper> mapper)
        where TMapper : IMapper<TSource, TResult>;
    public static nuint Where<T, TPredicate>(Span<T> source,
        write Span<T> destination, write ref<TPredicate> predicate)
        where T : [Copy]
        where TPredicate : IPredicate<T>;
}
```

`TryIndexOf` false means no index was published. `Select` preflights destination size and rejects any overlap before callback. `Where` reserves upper-bound capacity, calls predicate once per element, and permits disjoint storage or exact same-start compaction; displaced overlap is rejected before callbacks. Stateful callbacks are mutable borrowed values. Callback failure may leave accepted prefix; no rollback or predicate replay. General lazy/GroupBy/Join/Sort surfaces are not inventoried.

### Collection `params` binding (accepted direction; signatures illustrative)

```terrum
public static void Collect<T>(params List<T> values);

List<int32> existing = ...;
Collect(existing); // normal form: T=int32; pass the collection directly
List<List<int32>> nested = [existing];
Collect(nested); // normal form: T=List<int32>; explicit outer collection

void Log<T>(T item);
void Log<T>(params List<T> items);
Log(existing); // List<T> overload: T=int32.
```

`params` modifies the collection parameter's call form, not its type. A
matching collection binds directly in normal form; expanded form gathers
individual arguments into new slots under ordinary copy/move rules. Resolve
overloads by ordinary match quality and specificity first; a fixed-arity,
non-expanded candidate wins an otherwise equal match over expanded `params`,
not regardless of conversion quality. Diagnose unresolved ambiguity rather
than choosing arbitrarily. For the `Log` pair, both candidates infer one `T`
and match by identity after inference (`T = List<int32>` versus `T = int32`);
the declared `List<T>` parameter shape is more specific than bare `T`, so that
overload wins. This is not a ranking by inference-variable count or solver
effort and does not change if `params` is removed. A bare collection argument
does not silently enumerate; future enumeration requires explicit spread
syntax. The accepted ranking direction compares applicable candidates by per-argument
dominance: identity is better than permitted qualification weakening, and
crossed better/worse arguments are ambiguous rather than summed into a score.
Declared specificity and the accepted by-value/`implicit ref<T>`/`params`
priorities apply; return type, declaration order and literal numeric fit do
not break ties, and conversion chains are not searched. Exact grammar,
diagnostics, generic collection-expression inference and serialized binding
remain specification work.

## 6. Numerics and conversions

The numerics source is representative, not a complete primitive matrix:

```terrum
public interface IAdditionOperators<TSelf, TOther, TResult>
    where TSelf : IAdditionOperators<TSelf, TOther, TResult>
{
    static TResult operator +(TSelf left, TOther right);
}
public interface IMultiplyOperators<TSelf, TOther, TResult>
    where TSelf : IMultiplyOperators<TSelf, TOther, TResult>
{
    static TResult operator *(TSelf left, TOther right);
}
public interface IAdditiveIdentity<TSelf, TResult>
    where TSelf : IAdditiveIdentity<TSelf, TResult>
{
    static TResult Zero { get; }
}
public interface IScalarNumber<TSelf>
    : IAdditionOperators<TSelf, TSelf, TSelf>,
      IMultiplyOperators<TSelf, TSelf, TSelf>,
      IAdditiveIdentity<TSelf, TSelf>
    where TSelf : IScalarNumber<TSelf>
{
}
public interface IScalarIeee754<TSelf> : IScalarNumber<TSelf>
    where TSelf : IScalarIeee754<TSelf>
{
    static bool IsNaN(TSelf value);
    static bool IsFinite(TSelf value);
    static TSelf FusedMultiplyAdd(TSelf left, TSelf right, TSelf addend);
}
public interface ICheckedConversion<TDestination, TSource>
    where TDestination : ICheckedConversion<TDestination, TSource>
{
    static TDestination ConvertChecked(TSource value);
}
public interface ISaturatingConversion<TDestination, TSource>
    where TDestination : ISaturatingConversion<TDestination, TSource>
{
    static TDestination ConvertSaturating(TSource value);
}
public interface ITruncatingConversion<TDestination, TSource>
    where TDestination : ITruncatingConversion<TDestination, TSource>
{
    static TDestination ConvertTruncating(TSource value);
}
public struct int32 : IParsable<int32>, IFormattable, IScalarNumber<int32>
{
    public static int32 Zero { get; }
    public static int32 MinValue { get; }
    public static int32 MaxValue { get; }
    public static bool TryParse(TextSpan text, init ref<int32> value);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
    public static bool TryAdd(int32 left, int32 right, init ref<int32> result);
    public static bool TrySubtract(int32 left, int32 right, init ref<int32> result);
    public static bool TryMultiply(int32 left, int32 right, init ref<int32> result);
    public static int32 FromBits(uint32 bits);
}
public struct float32 : IParsable<float32>, IFormattable, IScalarIeee754<float32>
{
    public static float32 Zero { get; }
    public static bool IsNaN(float32 value);
    public static bool IsFinite(float32 value);
    public static float32 FusedMultiplyAdd(float32 left, float32 right, float32 addend);
    public static uint32 ToBits(float32 value);
    public bool KeyEquals(float32 other);
    public int32 GetKeyHashCode();
    public static bool TryParse(TextSpan text, init ref<float32> value);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
public struct decimal32 : IParsable<decimal32>, IFormattable, IScalarIeee754<decimal32>
{
    public static decimal32 Zero { get; }
    public static bool IsNaN(decimal32 value);
    public static bool IsFinite(decimal32 value);
    public static decimal32 FusedMultiplyAdd(decimal32 left, decimal32 right, decimal32 addend);
    public bool KeyEquals(decimal32 other);
    public int32 GetKeyHashCode();
    public static bool TryParse(TextSpan text, init ref<decimal32> value);
}
```

`IScalarNumber` and `IScalarIeee754` are optional composites; universal number protocol is not required. `TOther`/`TResult` may differ from `TSelf`. Vector arithmetic is not scalar classification. Named integer associated types such as `Wider`/`Signed`/`Unsigned` resolve through the selected interface witness; explicit associated equality can bind another type parameter, with no reverse inference from a projection. Exact member, equality, witness-selection and conversion-witness syntax remains specification work.

`int32.TryAdd/Subtract/Multiply` are checked expected failures; false publishes no output. `int32.FromBits` and `float32.ToBits` reinterpret bits, not numerically convert. Integer arithmetic is checked by default. Bare numeric literals have no default width; target context selects their type. The canonical [conversion policy](../conversions.md#conversion-policies) is: checked integer conversion preserves or throws, unchecked integer conversion wraps to destination-width bits, and saturation is explicit; floating-to-floating conversion follows IEEE-754 without language failure; float-to-int truncates then checked-throws or unchecked/explicitly-saturates, never modulo-narrows, with NaN converting to zero. Floating APIs are target-specific; exact source/destination witness lookup and user-defined declarations remain open.

## 7. Fixed SIMD, scalable vectors and tensors

SIMD values are fixed/scalable ABI values; tensors are logical views over memory. These are different representations. Fixed-vector registration is explicit and checked; width/mask/name resemblance is not membership. Family grammar and associated-mask binding are open.

```terrum
// Declaration-only specialization of simd.api.terrum's local fixed-family contract.
public struct simd128<T> : IAdditionOperators<simd128<T>, simd128<T>, simd128<T>>
    where T : [SimdElement], [Copy], [Sized]
{
    public static nuint LaneCount { get; }
    public static simd128<T> Splat(T value);
    public static simd128<T> Load(Span<T> source);
    public static simd128<T> LoadMasked(Span<T> source, simd128mask<T> active, T inactive);
    public static void Store(simd128<T> value, write Span<T> destination);
    public static void StoreMasked(simd128<T> value, simd128mask<T> active,
        write Span<T> destination);
    public static simd128mask<T> CompareEqual(simd128<T> left, simd128<T> right);
    public static simd128mask<T> CompareLessThan(simd128<T> left, simd128<T> right);
    public static simd128<T> Select(simd128mask<T> condition,
        simd128<T> whenTrue, simd128<T> whenFalse);
}
public struct simd128mask<T>
    where T : [SimdElement], [Copy], [Sized]
{
    public static simd128mask<T> First(nuint activeLanes);
    public static bool Any(simd128mask<T> value);
    public static bool All(simd128mask<T> value);
}

public struct TensorShape
{
    public nuint Rank { get; }
    public nuint ElementCount { get; }
    public Span<nuint> Lengths { get; }
    public Span<nint> Strides { get; }
}
public struct TensorSpan<T>
{
    public TensorShape Shape { get; }
    public nuint ElementCount { get; }
    [MatchedAccess] public ref<T> this[Span<nuint> indexes] { get; }
    [MatchedAccess] public TensorSpan<T> Slice(Span<nuint> origin, Span<nuint> lengths);
    [MatchedAccess] public TensorSpan<T> Transpose(Span<nuint> permutation);
}
public static class TensorViews
{
    public static TensorSpan<T> View<T>(Span<T> storage, TensorShape shape, nuint origin);
    public static write TensorSpan<T> View<T>(write Span<T> storage, TensorShape shape, nuint origin);
    public static bool TryAsDenseSpan<T>(TensorSpan<T> source, init ref<Span<T>> result);
    public static void CopyTo<T>(TensorSpan<T> source, write TensorSpan<T> destination)
        where T : [Copy];
}
public static class TensorPrimitives
{
    public static void Add<T>(Span<T> left, Span<T> right, write Span<T> destination)
        where T : IAdditionOperators<T, T, T>, [Copy];
    public static T Sum<T>(Span<T> source)
        where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, [Copy];
    public static T Dot<T>(Span<T> left, Span<T> right)
        where T : IAdditionOperators<T, T, T>, IMultiplyOperators<T, T, T>,
                 IAdditiveIdentity<T, T>, [Copy];
    public static void Add<T>(TensorSpan<T> left, TensorSpan<T> right,
        write TensorSpan<T> destination)
        where T : IAdditionOperators<T, T, T>, [Copy];
}
```

`simd.api.terrum` contains concrete type heads for `simd128<T>`/mask and a local pseudo-family contract for their static members; the declarations above specialize that member set for 128 bits. They remain design-only, not compiler bindings or runtime exports. Other fixed widths appear only in the family registration sketch. `simd<T>` is a separate scalable, initially sizeless type, not a host-selected fixed width. Masked-off lane access, tails, lane order, aliasing, FP contraction and reductions need explicit contracts. Tensor lengths are `nuint`, strides signed `nint`; rank-zero is scalar count one, any zero dimension yields zero without multiplying the remaining dimensions or computing `(length - 1)` addresses. Validate signed offset/address bounds for nonempty shapes. Shape metadata has its own lifetime dependency. Writable views require injective mappings; broadcast/zero-stride views cannot write when logical indexes alias. Copy preflights shape/aliasing, allows disjoint or identical per-index mappings only, and allocates no hidden temporary. Empty sum/dot return additive identity/zero. Exact default layout, scalable target ABI, family syntax and reproducible versus relaxed reduction APIs remain open.

## 8. Callables, tasks, errors, time, random and application services

`func<Args..., R>` and `fnptr<Args..., R>` are fixed-signature family notation, with result last—not source-level arbitrary type packs. `func` is the lifetime-aware single-cast callable family; `fnptr` is a raw closed-signature code pointer without retained environment. Bound `func` retains declared target/capture dependencies and cannot extend borrowed lifetime; `fnptr` target code must outlive use. There is no implicit closure boxing, multicast delegate or automatic genericization. Foreign ABI/callback details and exact grammar remain open.

```terrum
// Fixed-arity family notation only; exact callable grammar remains illustrative.
func<Arg0, Arg1, TResult>
fnptr<Arg0, Arg1, TResult>

// Accepted type-family direction; Task members are not specified here.
Task<TResult = void>
Task // expands to Task<void>, not general type erasure
```

Async methods return task-like completion, including `Task<void>` for no
result; `async void` is not allowed. The accepted
[async execution direction](../language.md#storage-concurrency-entry-and-boundaries)
is eager inline start through actual suspension, inline continuation for
completed awaits, and no automatic caller-executor capture or mandatory
worker hop for incomplete awaits. Declared affinity and checked
transfer/share/lifetime rules remain in force. A started operation retains
borrowed inputs through completion/cleanup; every operation needs a
responsible structured owner, explicit transfer, or supervisor; discarding it
neither cancels it nor discharges errors/captures. Cancellation is a request,
not completion. Exact task, executor, affinity and awaiter APIs remain open.

`IRandom` is referenced as a nominal protocol for mutable generic use through `write ref<TRandom>`, without defensive copies/boxing; storing concrete `TRandom` additionally requires `[Sized]`. The design sample defines `mut uint32 NextUInt32()` and `ISeedableRandom.Reseed(uint32 seed)` only; no RNG quality, entropy or cryptographic guarantee is specified.

Entry shapes: `Main` returning `void`/`int32` or `MainAsync` returning `Task`/`Task<int32>`, with no arguments or `Span<TextSpan> args` (excluding program name). Host arguments and roots last through async completion/structured cleanup.

Terminal/TextWriter are the existing scoped application I/O surface:

```terrum
public enum TerminalOutput : int32 { Standard, Error }
public static class Terminal
{
    [TextTarget(nameof(content))]
    public static void Write<TContent>(TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    [TextTarget(nameof(content))]
    public static void WriteLine<TContent>(TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    [TextTarget(nameof(content))]
    public static Task WriteLineAsync<TContent>(TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    public static write TextWriter Output { get; }
    public static write TextWriter Error { get; }
}
public mut class TextWriter : ITextWriter
{
    public mut bool TryWrite(TextSpan text);
    public mut bool TryWriteScalar(rune value);
    public mut bool TryWriteFormatted<TValue>(ref<TValue> value, TextSpan format)
        where TValue : IFormattable;
    [TextTarget(nameof(content))]
    public mut void Write<TContent>(TContent content)
        where TContent : IFormattable;
    [TextTarget(nameof(content))]
    public mut void WriteLine<TContent>(TContent content)
        where TContent : IFormattable;
    [TextTarget(nameof(content))]
    public mut Task WriteLineAsync<TContent>(TContent content)
        where TContent : IFormattable;
}
```

Standard output is default; `Error` selects stderr. Writes can expose partial progress; async formatting occurs once into operation-owned buffering then drains to completion. Dropping returned task cannot cancel I/O or release captures. `TryWrite` refusal differs from format-contract failure.

## 9. Accepted direction and unresolved choices

The following separates semantic constraints from source and API decisions
that remain open:

1. **Presence/result:** normalized optional flattening is the accepted semantic direction; settle only the source spelling/alias projection for `Option<T>`. A nominal `Missing | Found(T?)` union remains distinct when present-null must differ from missing. `Result<T,E>` and error payload design remain proposed; declaration-specific vacancy/publication contracts must be explicit.
2. **Equality/hash semantics:** clarify how concrete `int32.GetHashCode()` relates to numeric-key hashing; it does not imply a universal root contract. Settle reusable primitive key-comparer declarations.
3. **`init ref<T>` contracts:** semantics and `Create(init value)` call-site spelling are accepted direction; conditional outcome metadata and optional outputs remain open. These proposed signatures define no executable ABI.
4. **Memory signatures:** checked location/range grammar; legal CAS success/failure order pairs and bitwise-eligible representations; fence coverage/compiler-fence scope; non-temporal/cache safety and target support. Preserve compile-time-known ordering, accepted defaults, lock-free default and checked/wrapping selection.
5. **Collections/elements:** allocator/failure contract, non-Copy/borrowed/affine storage, guard alias/reentrancy enforcement, write-access naming, builder abandonment/error contract, lending iteration.
6. **Numerics:** integer associated-relation declarations and witness lookup; primitive operator/conversion matrix; floating/decimal policy gaps; align key hashing with comparer.
7. **SIMD/tensors:** fixed family/mask syntax, member registration/ABI and scalable restrictions; tensor shape storage/anchor, checked stride math, alias contract and reproducible reduction API.
8. **Tasks/errors/services:** Task members/awaiter/completion ownership representation; exact scheduling/affinity APIs; cancellation; error/exception object/failure ABI; RNG guarantees beyond the sample methods; time and filesystem/network abstractions.
9. **Notation/metadata:** generic defaults, family/extension/constraint syntax, fixed-signature `func`/`fnptr` family notation, handler/output attributes, public metadata and ABI. Source spellings remain illustrative.
10. **Collection `params`:** modifier changes call form, not parameter type; bind a matching collection directly in normal form and use expanded form to construct new slots. Apply the accepted per-argument dominance and declared-specificity rules; fixed arity beats expanded form only on an otherwise equal match, and crossed argument matches are ambiguous. Specify exact grammar/diagnostics and generic collection-expression inference; any future enumeration needs explicit spread.
11. **Adapters/extension interfaces:** ordinary implementations remain declared by their type; package imports do not add retroactive global conformances. Explicit adapters may add extension-interface behavior without changing the original witness set. Keep ABI-transparent nominal wrappers distinct from adapters; equal payload bits do not prove validity or total projection. Specify adapter intent/forwarding, dispatch metadata and any one-way projections only with validity, representation, access, ownership, lifetime and container guarantees. No blanket generic covariance or physical multiple-vtable layout is selected.
12. **Families and operation protocols:** families explicitly opt in and declare checked projections/constraints; no universal `Type<T>`-to-`Type<void>` erasure or inferred covariance follows. Preserve declared family relationships such as `TextSpan<void>`. No universal root is introduced merely for equality/hash/formatting; use operation-specific witnesses/interfaces and explicit adapters. Specify eligible adapter projections and readonly views with representation, validity, access, backing/lifetime and cost checks; writable storage remains invariant.

## 10. Open signature and contract alignments

The application-library proposal includes `Result<T,E>`/`Error`,
`Dictionary`/`HashSet`, task completion and ownership, clocks/time,
cancellation, file/stream and network endpoints, and application entry. RNG
quality/state guarantees beyond the scoped sample, console input, and general
service registration are outside this proposal. No signature freezes private
layout or establishes a public managed ABI.

The following distinctions and signature questions remain open:
- `int32.GetHashCode()` does not imply a universal root contract. Review only whether the primitive method denotes the same key-hash semantics as `float32`/`decimal32` or is a separate concrete helper; generic map-key use remains governed by explicit comparer witnesses.
- `TensorShape.Lengths`/`Strides` are `Span` properties but docs require immutable independently live metadata. Their exact readonly/view qualification and ownership source are not expressed by these signatures.
- `TensorViews.View` must preserve both data and shape dependencies, but no final multi-source lifetime/anchor syntax exists; shown signature alone is insufficient proof.
- `IParsable.TryParse(... init ref<T>)` uses the accepted `init` direction; its output contract remains open.
- The [memory model](../memory-model.md) defines accepted operation families, result forms and ordering; the signatures above propose source forms while exact grammar, target range contracts and some CAS declarations remain open.
- `Task<TResult = void>` is a type-family direction; type-default grammar and Task members are not finalized. `Task` does not erase an owning `Task<T>` result. Optional normalization is semantic; its type-substitution rules and exact source grammar remain open.

## 11. Scoped proposed signatures: random and `uint32`

This scoped proposal (not a production RNG API) declares only these public
members:

```terrum
public interface IRandom
{
    mut uint32 NextUInt32();
}
public interface ISeedableRandom : IRandom
{
    mut void Reseed(uint32 seed);
}
public static uint32 Draw<TRandom>(write ref<TRandom> random)
    where TRandom : IRandom;
public struct uint32 : IFormattable
{
    public static uint32 AddWrapping(uint32 left, uint32 right);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
```

The sample also has `CounterRandom(uint32 seed)` and `DifferentRandom<TRandom>(TRandom inner, uint32 mask)` with `TRandom : IRandom, [Sized]`; their backing state/algorithm is not reproduced. `NextUInt32` provides no implied entropy, statistical quality, cryptographic security, or OS randomness guarantee.

## 12. Proposed treatment of consequential open shapes

The following are candidate treatments, not accepted declarations. Each
remains subject to the accepted access, lifetime, and failure semantics.

| Open shape | Illustrative declaration | Candidate rationale and tradeoff |
|---|---|---|
| Presence versus expected failure | `T?` for absence; `Result<T, E>` as `Success(T)` / `Failure(E)` only if an API genuinely exposes a closed domain-failure set | Treat `T?` as the canonical presence spelling; do not add a second `Option<T>` runtime representation. Add nominal `Result<T,E>` only after `E`, initialization, and failure cleanup are specified. It makes domain errors composable, at the cost of a union/witness and more explicit handling. Keep thrown exceptions separate. |
| Conditional output | `bool TryParse(TextSpan, init ref<T> value);` | Preserve false => vacant and success => fully initialized; grammar and guarded outcome metadata must be specified together. `require` is a caller-side lift, not an exception convention. This avoids default fabrication but makes output-flow checking a language/metadata obligation. |
| CAS observation | `CompareExchangeObservation<T> AtomicCompareExchangeObserve<T>(...)`, with `bool Exchanged` and `T Previous`; distinct from success-only `bool AtomicCompareExchange<T>(...)` | Use a named observation record only when callers need both success and the previous bits. Restrict it to copied values with eligible defined representations; do not infer success through user `==`. ABI, validity and order-pair rules remain open. |
| Task responsibility | `Task<TResult = void>` remains a completion result; responsibility belongs to a structured owner/supervisor contract | Do not make possession or dropping of a task silently imply join, cancellation, or cleanup. A separate structured owner is more explicit and permits transfer/supervision, but requires a defined scope/ownership API. The accepted [async execution direction](../language.md#storage-concurrency-entry-and-boundaries) is independent of concurrency and parallelism; exact responsibility APIs remain separate library work. |
| General conversion matrix | `T(value)` for checked; separately declared checked/saturating/truncating source-destination witnesses | Accepted: checked integer-to-integer preserves or throws, unchecked wraps destination-width bits, and saturation is explicit; floating-to-floating conversion follows IEEE-754 with nearest/ties-even default and no language failure for inexactness, infinity, underflow or NaN; float-to-int truncates then checked-throws or unchecked/explicitly-saturates, never modulo-narrows, with NaN converting to zero. Keep floating support target-only; specify witness lookup and APIs separately. |
| Collection `params` | `Collect<T>(params List<T> values)` | `params` changes call form, not parameter type: bind a matching collection directly in normal form, or use expanded form to construct new slots. Apply ordinary match quality/specificity; fixed arity beats expanded `params` only on an otherwise equal match. Diagnose unresolved ambiguity; exact severity remains open. Explicit collection construction expresses nesting, and future enumeration requires spread. |
| Time and randomness | Proposed signatures in §13 | Do not copy `DateTime`/`Random` APIs by analogy. §13 proposes distinct UTC and monotonic clock contracts, checked nanosecond duration arithmetic, and leaves RNG quality/entropy/cryptographic guarantees unspecified. This gains testable time APIs without pretending resolution equals precision, at the cost of separate timestamp domains and explicit clock injection. |

## 13. Proposed standard application-library/runtime surface

This section sketches one possible standard application-library/runtime
surface, not the minimal language baseline or a named target profile. It
offers candidate declaration shapes; none is approved by its spelling, and
the inventory is not mandatory for language conformance. Feature-specific
runtime dependencies remain explicit when used; a missing facility does not
weaken semantics or trigger an implicit fallback. The task lifecycle proposed
below does not change the accepted async function-call model. The accepted
[async execution direction](../language.md#storage-concurrency-entry-and-boundaries)
does not select concurrent/parallel execution. Fields below are semantic
public state, not frozen private layout or wire ABI.

### Expected failures and error values

Use a nominal `Result<T,E>` for expected domain alternatives and retain exceptions for exceptional language/runtime failures. Do not make every error an exception object or invent a universal object root. This is a *candidate* family syntax; payload ownership follows `T`/`E` (a result containing an owner is not `[Copy]`).

```terrum
public struct ErrorCode
{
    public uint32 Domain { get; }
    public uint32 Value { get; }
}

public struct Error : IFormattable
{
    public ErrorCode Code { get; }
    public Text? Message { get; }
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}

public nominal union Result<T, E>
{
    Success(T value);
    Failure(E error);
}
```

`Domain`/`Value` identify a documented condition; numeric assignments/registry are open and should not be guessed from OS error constants. `Message` is optional owned immutable text, not a mandatory allocation or an implicit `ToString`; callers can handle codes without formatting. A failed Result is data, not an exception. `require` and thrown exceptions remain separate. Keep `T?`/`Option<T>` for absence, not error status.

### Keyed collections (proposed baseline)

```terrum
public mut class Dictionary<TKey, TValue, TComparer>
    where TKey : [ManagedStorable], [Copy], [Sized]
    where TValue : [ManagedStorable], [Copy], [Sized]
    where TComparer : IKeyComparer<TKey>, [ManagedStorable], [Copy], [Sized]
{
    public nuint Count { get; }
    public TComparer Comparer { get; }
    public Dictionary(TComparer comparer, nuint capacity);
    public bool TryGet(TKey key, init ref<TValue> value);
    public mut Result<bool, Error> TryAdd(TKey key, TValue value);
    public mut Result<void, Error> Set(TKey key, TValue value);
    public mut bool Remove(TKey key);
    public mut void Clear();
}

public mut class HashSet<T, TComparer>
    where T : [ManagedStorable], [Copy], [Sized]
    where TComparer : IKeyComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public nuint Count { get; }
    public TComparer Comparer { get; }
    public HashSet(TComparer comparer, nuint capacity);
    public bool Contains(T value);
    public mut Result<bool, Error> TryAdd(T value);
    public mut bool Remove(T value);
    public mut void Clear();
}
```

These proposed constructors require both comparer and capacity; no `DefaultComparer` or hidden comparer lookup is provided, and missing values are never synthesized with `default(TValue)`. `TryGet` returns false for a missing key and leaves its output vacant; true publishes a copied value. `TryAdd` returns `Success(false)` for a duplicate without changing collection contents or count, `Success(true)` only after insertion, and `Failure(Error)` on allocation failure with no partial publication. `Set` inserts or updates and leaves the prior mapping unchanged if insertion allocation fails. `Remove` returns false when absent; `Clear` empties the collection. Construction validates capacity and is all-or-nothing; capacity/allocation failure must be a checked failure before an instance is published. The precise constructor-failure transport remains open because no constructor-failure ABI or library-wide exception hierarchy is accepted. Keys must retain equality/hash stability while stored; comparer behavior must also remain stable, and comparer equality must imply identical hashes. These collections add no automatic concurrent safety; callers need an explicit external synchronization contract. Initial key, value, element and stored-comparer constraints intentionally retain `[ManagedStorable]`, `[Copy]`, and `[Sized]`; broader affine/non-Copy storage is out of scope.

### Tasks, await, cancellation and completion responsibility

```terrum
public enum TaskStatus : int32
{
    Created,
    Running,
    Succeeded,
    Faulted,
    Canceled
}

public nominal union TaskOutcome<T>
{
    Completed(T value);
    Faulted(Error error);
    Canceled;
}

public mut unique class Task<T = void>
{
    public TaskStatus Status { get; }
    public bool IsCompleted { get; }
    public bool HasOutcome { get; }
    public TaskOutcome<T> GetOutcomeCopy() where T : [Copy];
    public mut bool Start();
    public mut bool RequestCancellation();
    public mut bool TryTakeOutcome(init ref<TaskOutcome<T>> outcome);
    public mut TaskAwaiter<T> GetAwaiter();
}

// Bare Task is the declared Task<void> family member, not a derived type.
public struct TaskAwaiter<T>
{
    public bool IsCompleted { get; }
    public ref<TaskOutcome<T>> GetResult();
    public void OnCompleted(func<void> continuation);
}

public struct CancellationToken
{
    public bool IsCancellationRequested { get; }
}

public mut unique class CancellationSource
{
    public CancellationToken Token { get; }
    public mut void Cancel();
}

public mut unique struct TaskScope
{
    public TaskScope();
    public mut void Start<T>(move Task<T> operation);
    public mut Task<void> JoinAsync();
    public mut void RequestCancellation();
}
```

The concrete alias/inheritance syntax is illustrative; prefer the one `Task<T = void>` family if the grammar can express it, so `Task` is exactly `Task<void>`. `Task<T>` is a unique operation/completion owner, not a freely copied `[Copy]` value. `TaskAwaiter<T>.GetResult()` and `Task<T>.GetOutcomeCopy()` provide two explicitly different paths: the first returns a checked readonly borrow tied to the task lifetime, the second is available only when every payload in the copied outcome (`T` and `Error`) is `[Copy]`. Borrowed access cannot overlap an exclusive take; reborrowing a payload that retains write capability still obeys the ordinary access/share checker. `TryTakeOutcome` transfers the outcome once, including any unique payload; `HasOutcome` is false until completion and after that transfer. Completion observation remains repeatable after a take, but result access does not: `GetResult()` or `GetOutcomeCopy()` when no outcome remains must fail through a checked `require`/invalid-state path (the precise failure transport remains open), never fabricate or return an uninitialized value. Repeated awaits may observe the same completion, but they never duplicate an owning result; repeatable by-value access requires `[Copy]`, while repeatable borrowed access requires a valid checked borrow/share contract. For `Task<void>`, completion carries no value. `Completed(Result<T,E>.Failure(...))` is an expected domain failure; `Faulted(Error)` is a typed terminal diagnostic; `Canceled` is a cooperative cancellation outcome. This deliberately does not promise a catchable universal exception object or preserve runtime exception identity/stack; that would need its own contract, and `Error` is not a root type.

The accepted execution model starts an async function inline when called and continues until actual suspension. Completed awaits continue inline; incomplete ordinary awaits do not capture the caller's executor or require a worker hop. Affinity-bound functions/callbacks retain their own declared executor through suspension, cleanup and unwinding; independent helpers do not inherit affinity merely from their caller. `Run` schedules an initial invocation and adopts its full async completion without binding every helper continuation or choosing the caller's continuation. An explicit awaitable adapter such as `ResumeOn(executor)` selects the awaiting continuation without rescheduling the producer; an explicit `Yield` may request a scheduling boundary. Executor choice does not bypass transfer, sharing, lifetime, affinity or cleanup checks.

The `TaskScope`, `Start`, capture, task-handle disposal and outcome APIs shown here remain a separate library proposal, not an adopted lifecycle contract. Their cold-task behavior in particular does not describe or override the accepted async function-call model. Repeated observation, responsibility ownership, cancellation and retained dependencies still require coherent exact API contracts. Any cross-thread scheduling requires every captured value to satisfy explicit transfer/share and lifetime contracts; arbitrary thread-confined captures cannot be sent to a worker. `Task<T>` is not automatically `[ThreadShareable]`, and no ambient scheduler can waive these checks. Dropping a handle is never cancellation. `CancellationSource` uniquely controls a shared request; its token is a non-owning view and may not be default-fabricated. Requesting cancellation is an idempotent, monotonic, cooperative signal; it does not forcibly stop an operation or end borrowed-buffer, receiver, or owner dependencies before the operation stops and cleans up. Cross-thread request/observation guarantees remain a memory/runtime contract to specify.

`GetAwaiter` and result observation signatures are **PROPOSED**; exact `await`
integration, task-state synchronization, error aggregation in `JoinAsync`,
executor selection and task result borrowing rules remain open. Captured
borrows cannot outlive their declared backing lifetime. The accepted
no-implicit-caller-executor-capture direction and declared affinity rules do
not settle how this draft's task/scope API realizes scheduling; a general
executor contract and callback effects remain open.

### Time values and clock capabilities

```terrum
public struct Duration
{
    public int64 Nanoseconds { get; }
    public static Duration Zero { get; }
    public static bool TryAdd(Duration left, Duration right, init ref<Duration> result);
    public static bool TrySubtract(Duration left, Duration right, init ref<Duration> result);
}

public struct Instant
{
    public int64 UnixNanoseconds { get; }
    public static bool TryAdd(Instant instant, Duration delta, init ref<Instant> result);
    public static bool TrySubtract(Instant later, Instant earlier, init ref<Duration> result);
}

public struct MonotonicTimestamp;

public interface IUtcClock
{
    Instant GetUtcNow();
}
public interface IMonotonicClock
{
    MonotonicTimestamp GetTimestamp();
    bool TryGetElapsed(MonotonicTimestamp earlier, MonotonicTimestamp later,
        init ref<Duration> elapsed);
}
public struct SystemClock : IUtcClock, IMonotonicClock
{
    public static SystemClock Create();
    public Instant GetUtcNow();
    public MonotonicTimestamp GetTimestamp();
    public bool TryGetElapsed(MonotonicTimestamp earlier, MonotonicTimestamp later,
        init ref<Duration> elapsed);
}
```

`Duration` is signed nanoseconds for arithmetic/interop, not a promise of nanosecond clock precision; checked range failure returns false and leaves output vacant. `Instant` uses Unix epoch only for UTC timestamps. Monotonic timestamps are opaque, have no public epoch/counter, and compare only through the same clock instance/domain; `TryGetElapsed` rejects foreign or reversed/overflowing pairs. Wall clocks can move backward and are not substitutes for elapsed-time measurement. Inject clock interfaces into code needing determinism; no ambient global clock, timezone database, calendar, timer queue, or sleep API is implied. The chosen nanosecond range is approximately ±292 years, trading a simple scalar representation for a bounded epoch; range/precision can be revisited before adoption.

### Streams, file capability and network endpoints

```terrum
public enum SeekOrigin : int32
{
    Begin,
    Current,
    End
}
public enum FileDisposition : int32
{
    OpenExisting,
    CreateNew,
    CreateOrReplace,
    OpenOrCreate,
    Append
}

public interface IByteStream
{
    bool CanRead { get; }
    bool CanWrite { get; }
    mut Result<nuint, Error> Read(write Span<uint8> destination);
    mut Result<nuint, Error> Write(Span<uint8> source);
    mut Result<void, Error> Flush();
    mut Result<void, Error> Close();
    [NoThrow, Cleanup] mut void Dispose();
    [Captures(nameof(destination))]
    mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures(nameof(source))]
    mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
}

public mut unique struct FileHandle : IByteStream
{
    public bool IsOpen { get; }
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public Result<uint64, Error> GetLength();
    public mut Result<uint64, Error> Seek(int64 offset, SeekOrigin origin);
    public mut Result<nuint, Error> Read(write Span<uint8> destination);
    public mut Result<nuint, Error> Write(Span<uint8> source);
    public mut Result<void, Error> Flush();
    public mut Result<void, Error> Close();
    [NoThrow, Cleanup] public mut void Dispose();
    [Captures(nameof(destination))]
    public mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures(nameof(source))]
    public mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
}

public class FileSystem
{
    public Result<FileHandle, Error> OpenRead(TextSpan relativePath);
    public Result<FileHandle, Error> OpenWrite(TextSpan relativePath,
        FileDisposition disposition);
    public Result<bool, Error> FileExists(TextSpan relativePath);
    public Result<void, Error> CreateDirectory(TextSpan relativePath);
    public Result<void, Error> DeleteFile(TextSpan relativePath);
}

public struct TcpEndpoint
{
    public Text Host { get; }
    public uint16 Port { get; }
}
public mut unique struct TcpConnection : IByteStream
{
    public bool IsOpen { get; }
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public mut Result<nuint, Error> Read(write Span<uint8> destination);
    public mut Result<nuint, Error> Write(Span<uint8> source);
    public mut Result<void, Error> Flush();
    [Captures(nameof(destination))]
    public mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures(nameof(source))]
    public mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
    public mut Result<void, Error> Close();
    [NoThrow, Cleanup] public mut void Dispose();
}
public mut unique struct TcpListener
{
    public static Result<TcpListener, Error> Bind(TcpEndpoint endpoint);
    public bool IsOpen { get; }
    public mut Task<Result<TcpConnection, Error>> AcceptAsync(CancellationToken cancellation);
    public mut Result<void, Error> Close();
    [NoThrow, Cleanup] public mut void Dispose();
}
public class NetworkClient
{
    public Task<Result<TcpConnection, Error>> ConnectAsync(TcpEndpoint endpoint,
        CancellationToken cancellation);
}
```

`FileSystem` is an explicit host-supplied capability rooted to an authorized filesystem scope; `relativePath` cannot escape that root. There is no ambient current directory, unrestricted global `File`, arbitrary path-based service locator, or implicit path encoding conversion in this minimum. Path normalization, symbolic-link policy and platform error mapping must be specified by the host profile before adoption. `TcpEndpoint` is a small immutable value over a shared immutable `Text` handle, not a unique resource owner; `ConnectAsync` takes this value rather than retaining a temporary text span, with backing lifetime still governed by the operation's declared dependencies. DNS/network errors are explicit `Result` failures.

Reads return bytes read; `0` means EOF only when the destination is nonempty. An empty read returns zero without probing EOF. Writes may be partial and the caller advances by the returned count; the proposed contract requires a successful write of nonempty input to report positive progress, not a zero-count success that invites a retry loop. Buffer-size/alias checks occur before access. Async stream calls capture their buffer and exclusive stream access until completion, including after cancellation is requested; their declared operation lifetime must cover those loans through completion and cleanup. One in-flight mutating operation per stream is the proposed default. `Close` invalidates the Terrum wrapper authority even when the target reports an error; the underlying OS resource may then be indeterminate. Its `Result` must carry the reported failure, and the target/runtime contract must define what release is known. Never blindly retry a close against a possibly reused numeric descriptor or claim target release on every error. The shown no-throw `Dispose` signature is provisional: it cannot silently convert a target close failure into success, but its explicit error-reporting/deferred-cleanup policy is unresolved. This reference makes no per-target guarantee of OS release on every failure; callers needing an observable result use `Close`, while implicit cleanup behavior still requires a runtime contract. Unique handles cannot be copied; `move` transfers release responsibility. Result unions carrying handles are likewise non-`Copy`. Network listener/client tasks follow the same cancellation and task ownership rules; no zero-copy, TLS, datagram, proxy, or ambient DNS configuration surface is implied.

### Application entry

The existing scoped entry direction remains minimal; no `Environment` root or service locator is needed:

```terrum
public static int32 Main(Span<TextSpan> args);
public static Task<int32> MainAsync(Span<TextSpan> args);
```

`args` excludes the program name and stays rooted through asynchronous completion and structured cleanup. `MainAsync` must be responsible for its task to completion; host shutdown does not silently cancel or abandon it. A no-argument `void`/`int32` `Main` and `Task<void>` `MainAsync` remain valid variants from the canonical language contract.
