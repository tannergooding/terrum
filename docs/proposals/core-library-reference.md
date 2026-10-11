# Terrum core-library proposal

> [!IMPORTANT]
> Proposal only: signatures and inventories are candidate designs, not
> approved APIs or claims of implementation.

This proposal inventories candidate APIs and dependency layers, not an
approved or executable library. Accepted semantic direction, proposed
signatures and open choices are marked separately below; examples do not
establish conformance. This is not an ABI layout specification.

Canonical language and library contracts live in [language](../specs/language.md),
[libraries](../specs/libraries.md), [language contracts](../specs/language-contracts.md),
[lifetimes](../specs/lifetimes.md), [type families](../specs/type-families.md),
[conversions](../specs/conversions.md) and [memory model](../specs/memory-model.md).
The [adopted inventory](../specs/libraries/adopted-inventory.md) identifies
exact selected roles, incomplete families and unselected candidates; the
[design scenarios](../specs/libraries/design-scenarios.md) exercise their
actual contracts. Selection of a protocol does not adopt each candidate
provider or member in this proposal.
The design-only [`library/*.api.terrum`](../../library/) sketches are inventory
inputs, not reference assemblies.

## Scope, status, and notation

**ACCEPTED DIRECTION** identifies a semantic constraint also stated in the
canonical language references; it does not approve a nearby declaration.
**PROPOSED** marks a candidate signature or API contract. **OPEN** marks a
decision not made here. Signature syntax is illustrative unless an accepted
source form is explicitly identified. No proposal-only declaration is required
for language conformance. Conformance to a library profile requires adoption
of its declarations together with their contracts and feature dependencies;
these candidate inventories establish no such conformance.

This is **reference-signature notation**: a terminating `;` on a concrete
member denotes its proposed public signature, not permission to compile a
bodyless concrete method, accessor, constructor, or property in runnable
Terrum. The implementing library must provide legal bodies or a separately
specified intrinsic/foreign implementation. Interface requirements have their
own bodyless semantics. Tables using `Type.Member(arguments) -> Result` are
likewise inventories, not Terrum source productions. In particular, a table
marked **OPEN** cannot acquire source syntax or metadata simply by copying
its entry into a declaration.

The public namespace map follows the concept namespaces already present in
the sketches; dependency layers do **not** prefix public names:

| Namespace | Candidate types and operations |
|---|---|
| `Terrum` | primitives, `Span`, text, formatting, memory, tasks/application entry prototypes |
| `Terrum.Collections` | owned storage, list, linked list, keyed and sequential collections |
| `Terrum.Collections.Algorithms` | equality/key comparers, callbacks, eager span operations |
| `Terrum.Numerics` | fixed/scalable vector values and masks |
| `Terrum.Numerics.Tensors` | shape/view and tensor operations |

New application capability namespaces below are **proposals**, not evidence
of exports in `library/*.api.terrum`. They can be split into packages without
renaming established concept types. Neither .NET inheritance nor CLR primitive
categories, array/object roots, `IDisposable`, finalizers, or an implicit
numeric-promotion lattice are imported by this reference.

The comparison target is *inventory breadth and reviewability*, not
compatibility: [.NET reference assemblies](https://learn.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies)
expose a public API without implementations, while
[ECMA-335 Partition IV](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)
and its [TR/84 library listing](https://www.ecma-international.org/publications-and-standards/technical-reports/ecma-tr-84/)
organize CLI library profiles. This document instead inventories Terrum's
checked value, view, ownership, operation, and capability boundaries. It
does not import CLI type identity, standardized profile names, interfaces
or implementation layout.

## Dependency layers

The accepted `language` floor, optional library tiers, feature dependencies,
primitive authority and prohibition on silent fallback are specified in
[library layering](../specs/libraries.md#dependency-layers-and-package-boundaries)
and [language direction](../specs/language.md#accepted-direction). The
Core/Foundation/Application/Platform layers below remain proposed groupings,
not standardized profiles or mandatory language APIs. The selected primitive
inventory, source/target mapping, artifact authority, baseline/API mapping
and feature-to-runtime support remain explicit profile/build work.

| Baseline / candidate cumulative layer | Representative surface | Required substrate |
|---|---|---|
| **`language` baseline** | Selected compiler/runtime-owned primitive bindings, representation/reference forms and language-required checking services; not the entire signature inventory below | Explicit selected language/target facilities; ordinary packages cannot redefine intrinsic identity or operations |
| **Core** | Numeric primitives and protocols, `void`/options, `Span<T>`, immutable code-unit/text views, static eager algorithms, shape/view and SIMD contracts | Checked value execution, bounds/access and cleanup/failure services; no ordinary-path allocation or OS facility |
| **Foundation** | Explicitly owned buffers/vectors/maps, allocators/arenas/pools, owning native `Text`, persistent tensor-shape metadata and storage leases | Core and a caller/host-supplied allocator; no mandatory tracing collector |
| **Application** | Managed list/linked list/dictionary/set/queue, ordinary managed `Text`, tasks, streams/terminal, files/network, globalization and serialization | Foundation plus tracing and appropriate host services |
| **Platform leaves** | COM/Windows/DirectX and other native bindings | Only layers their adapters actually use; no reverse dependency on platform APIs |

## Reading declarations

Access, representation, and complete generic qualification follow
[language types and operations](../specs/language.md#types-and-operations),
[library view contracts](../specs/libraries.md#views-shapes-and-tensor-primitives),
and [lifetimes](../specs/lifetimes.md). Approved interface, associated-type,
default, `params`, and extension-provider forms are in the
[source grammar](../specs/grammar-spec.md); output vacancy and conditional
publication are in [language contracts](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate).
Those accepted forms do not settle candidate witnesses, family registration,
or per-API contract metadata. Here, a concrete-member `;` remains
reference-signature notation, not an implementation or an additional source
form; each inventory retains its own unresolved dependencies and decisions.

## Selected Core value-layout queries

**SELECTED CORE CONTRACT:** the
[registered-query chapter](../specs/language/registered-queries.md) adopts
these ordinary members on Core's `Terrum.RuntimeHelpers` identity.
This does not approve the rest of this proposal or select a wire intrinsic ID:

| Member | Declared subject and result |
|---|---|
| `SizeOf<T>() -> nuint` | `where T : [Sized]`; complete normalized value/handle byte extent |
| `AlignmentOf<T>() -> nuint` | The same constraint; value representation's alignment in bytes |

The exact intrinsic contracts are pure and required-constant-eligible,
without constructing T, allocating a reflection object or initializing
mutable static state. They are not object-payload queries.
Runtime layout evidence does not imply a definition-time number; retained
observations require published fixed target promises or explicit complete
layout-coupling/invalidation records. The result's scalar codec does not
freeze its queried type. Existing IsPureEval/IsConstEval properties keep their
[evaluation-mode contracts](../specs/language/published-predicates-and-effects.md#evaluation-modes).
Neither `sizeof`/`alignof` syntax nor general source query registration is added.

## Type and protocol reference index

This index locates the concrete public type/protocol shapes below, including
sampled primitive declarations and explicitly unapproved alternatives. Names
omit generic arguments for readability except where two different sequence
shapes need distinguishing; the linked declaration supplies the formals,
constraints and member signatures. Being indexed does not approve a type,
its namespace, family registration, ABI or implementation.

Generative width families, scalable SIMD, nominal sums such as `Result<T,E>`
and matrix-only service candidates remain in their respective inventories;
they are not complete declarations merely because their names appear in a
table. The proposed `Option<T>` alias is separately described in
[fundamental values](#1-fundamental-values-presence-and-outcomes).

| Signature group | Concrete types/protocols | Reference |
|---|---|---|
| Equality, keying, parse and format | `IEqualityComparer`, `IKeyComparer`, `IParsable`, `IFormattable` | [Witnesses](#2-explicit-equality-keying-and-formatting-witnesses) |
| Explicit primitive comparison | `Int32KeyComparer`, `Float32IeeeEqualityComparer`, `Float32NumericKeyComparer` | [Primitive comparers](#proposed-explicit-primitive-comparers) |
| Views and immutable text | `Span`, `IContiguousSequence`, `TextSpan`, `rune`, `Text` | [Text and views](#3-text-encoding-and-writers) |
| Text output | `ITextWriter`, `SpanTextWriter` | [Writer signatures](#3-text-encoding-and-writers) |
| Text construction and search | `TextBuilder`, `TextSearch` | [Construction/search](#proposed-text-construction-search-and-incremental-transcoding) |
| Incremental text conversion | `MalformedTextPolicy`, `TextConversionStatus`, `TextConversionProgress`, `ITextDecoder`, `ITextEncoder`, `Utf8Decoder`, `Utf8Encoder`, `Utf16Decoder`, `Utf16Encoder` | [Transcoding](#proposed-text-construction-search-and-incremental-transcoding) |
| Inline storage | `InlineArray` | [Inline storage](#general-span-and-inline-storage) |
| Allocation and native owners | `AllocationLease`, `AllocatorContext`, `Buffer`, `Vector` | [Native storage](#explicit-native-storage-representative-foundation-inventory) |
| Native double-ended storage | `Deque`, `DequeSegments`, `DequeWriteAccess` | [Deque signatures](#proposed-owned-double-ended-storage) |
| Borrowed arena and rentals | `ByteArena`, `MemoryPool`, `MemoryOwner` | [Arena/pool](#proposed-borrowed-arena-and-typed-rental-storage) |
| Memory operations | `MemoryOrder`, `CompareExchangeObservation`, `MemoryPrimitives` | [Memory primitives](#memoryprimitives) |
| Selected value-layout queries | `RuntimeHelpers` | [Core value-layout queries](#selected-core-value-layout-queries) |
| Managed lists | `List`, `ListCursor`, `ListBuilder`, `ListWriteAccess`, `LinkedListNode`, `LinkedList`, `LinkedListBuilder`, `LinkedListCursor` | [Collections](#5-collections-iteration-and-eager-algorithms) |
| Eager callbacks and algorithms | `IMapper`, `IPredicate`, `IZipper`, `IVisitor`, `SpanAlgorithms` | [Eager signatures](#5-collections-iteration-and-eager-algorithms) |
| Selected synchronous/async value iteration and proposed producer examples | `ICursor`, `ISequence<T, TCursor>`, `Int32RangeCursor`, `Int32RangeSequence`, `IAsyncCursor`, `IAsyncSequence`, `Int32AsyncCursor`, `Int32AsyncSequence` | [Sequence/cursor contracts](#sequence-and-cursor-protocol-contracts) |
| Unapproved associated/lending alternatives | `ISequence<T>`, `ILendingCursor`, `IAsyncLendingCursor` | [Iteration alternatives](#sequence-and-cursor-protocol-contracts) |
| Ordering and folding | `IOrderComparer`, `IFolder`, `OrderedSpanAlgorithms` | [Ordered signatures](#proposed-ordered-and-sequential-collection-declarations) |
| Ordered/sequential storage | `SortedSet`, `SortedSetCursor`, `Queue`, `QueueCursor`, `Stack`, `StackCursor` | [Collection signatures](#proposed-ordered-and-sequential-collection-declarations) |
| Independent numeric operations | `ISubtractionOperators`, `IDivisionOperators`, `IRemainderOperators`, `IUnaryPlusOperators`, `IUnaryNegationOperators`, `IEqualityOperators`, `IComparisonOperators`, `IBitwiseOperators`, `IShiftOperators`, `IMultiplicativeIdentity`, `IIntegerRelations`, `IFloatingClassification` | [Operation protocols](#independently-selectable-operation-protocols) |
| Sampled numeric witnesses | `IAdditionOperators`, `IMultiplyOperators`, `IAdditiveIdentity`, `IScalarNumber`, `IScalarIeee754` | [Sampled declarations](#existing-sampled-declarations) |
| Selected conversion pairs | `ICheckedConversion`, `ISaturatingConversion`, `ITruncatingConversion` | [Conversion witnesses](#existing-sampled-declarations) |
| Sampled primitives | `int32`, `float32`, `decimal32`; `uint32`, `int8` | [Initial samples](#existing-sampled-declarations), [width-family samples](#candidate-primitive-member-families) |
| Scalar services | `BitOperations` | [Scalar services](#numeric-conversion-and-scalar-operation-signatures) |
| Built-in numeric operations | Per-type selection members and generic-interface access | [Numeric operation signatures](#numeric-conversion-and-scalar-operation-signatures), [combined numeric-selection proposal](numeric-selection-members.md) |
| Fixed SIMD | `simd128`, `simd128mask`, `Simd128Conversions`, `Simd128Reductions` | [Fixed SIMD](#fixed-width-simd) |
| Tensor metadata and views | `TensorShape`, `TensorSpan`, `TensorViews` | [Tensor views](#tensor-shapes-and-views) |
| Tensor kernels | `TensorPrimitives` | [Tensor operations](#tensor-operations) |
| Random-source witnesses | `IRandom`, `ISeedableRandom` | [Scoped random samples](#8-callables-tasks-errors-time-random-and-application-services) |
| Terminal output | `TerminalOutput`, `Terminal`, `TextWriter` | [Terminal samples](#8-callables-tasks-errors-time-random-and-application-services) |
| Expected-error payloads | `ErrorCode`, `Error` | [Error values](#expected-failures-and-error-values) |
| Keyed storage and traversal | `Dictionary`, `HashSet`, `DictionaryEntry`, `DictionaryCursor`, `HashSetCursor` | [Keyed collections](#keyed-collections-proposed-baseline) |
| Task ownership and cancellation | `TaskStatus`, `Task`, `TaskAwaiter`, `CancellationToken`, `CancellationSource`, `TaskScope` | [Task signatures](#tasks-await-cancellation-and-completion-responsibility) |
| Time and clock capabilities | `Duration`, `Instant`, `MonotonicTimestamp`, `IUtcClock`, `IMonotonicClock`, `SystemClock` | [Time signatures](#time-values-and-clock-capabilities) |
| Stream and file access | `SeekOrigin`, `FileDisposition`, `IByteStream`, `ISeekableByteStream`, `FileHandle`, `FileSystem` | [Streams/files](#streams-file-capability-and-network-endpoints) |
| TCP capabilities | `TcpEndpoint`, `TcpConnection`, `TcpListener`, `NetworkClient` | [Network endpoints](#streams-file-capability-and-network-endpoints) |
| Borrowed memory stream | `MemoryByteStream` | [Memory stream](#proposed-borrowed-byte-stream-storage) |
| Progress-aware I/O | `ByteTransferStatus`, `ByteTransferResult`, `IProgressByteStream`, `ByteStreamAlgorithms` | [Progress operations](#proposed-progress-aware-stream-operations) |
| File metadata and traversal | `FileKind`, `FileRenamePolicy`, `FileMetadata`, `DirectoryEntry`, `DirectoryCursor` | [Filesystem metadata](#proposed-filesystem-metadata-and-directory-traversal) |
| Addresses and datagrams | `IpAddress`, `IpEndpoint`, `DatagramRead`, `DatagramSocket` | [Datagrams](#proposed-address-values-and-datagram-operations) |
| UTF-8 stream adapters | `Utf8TextReader`, `Utf8TextWriter` | [Stream text adapters](#proposed-utf-8-stream-text-adapters) |
| Binary values and fixed codecs | `BinaryPrimitives`, `ByteOrder`, `BinaryCodecStatus`, `IFixedBinaryCodec`, `UInt32BinaryCodec`, `Int64BinaryCodec` | [Binary codecs](#proposed-binary-values-and-fixed-codecs) |
| Culture and civil formatting | `Locale`, `ICultureDataProvider`, `CultureData`, `NumberFormatter`, `LocalDateTime`, `DateTimeFormatter` | [Globalization](#proposed-globalization-and-civil-time-providers) |
| Zone data and civil mappings | `ITimeZoneDataProvider`, `TimeZoneDatabase`, `TimeZone`, `LocalTimeResolution`, `LocalTimeMapping` | [Time zones](#proposed-globalization-and-civil-time-providers) |

## 1. Fundamental values, presence and outcomes

The canonical [language contract](../specs/language.md#primitives-strict-arithmetic-and-text)
defines `void`, optional normalization, and the absence of a universal root;
[alias rules](../specs/type-families.md#nominal-witnesses-and-associated-relationships)
define the transparent alias form. Exporting `Option<T>` remains a library
decision. The proposed `Lookup<T>` uses an optional payload to distinguish
missing from present-null; its type shape remains unapproved. The union and
case syntax is an [approved target form](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
not evidence of current compiler support.

```terrum
// Approved alias declaration form; this particular exported alias is PROPOSED.
public alias Option<T> = T?;
```

| Proposed nominal sum | Case inventory | Distinction |
|---|---|---|
| `Lookup<T>` | `Missing`; `Found(T? value)` | Missing versus a present optional payload, including null |

Presence, vacancy, initialization, and checked failure are separate under
[language contracts](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate);
no niche encoding follows from the candidate shapes. `Result<T,E>` and its
error payload remain proposed in §13, without selecting a library-wide
exception hierarchy or object ABI.

### Primitive and presence inventory

The following is an inventory of **candidate** value surfaces, not a claim that
all have the same bit layout, supported operators or storage category. An
operation listed for a family is subject to its per-type semantic constraints;
it is not an implicit conversion or promoted mixed-width expression.

| Family / values | Proposed representative public members | Boundary |
|---|---|---|
| `bool` | `True`, `False`; `operator !`, `operator &`, `operator \|`, `operator ^` | Logical and bitwise evaluation forms retain distinct expression semantics; no integer coercion |
| signed integer widths (including sampled `int32`, and `nint`) | `MinValue`, `MaxValue`, `Zero`; same-width arithmetic, comparison, bitwise, shifts, `TryAdd/Subtract/Multiply`, `TryParse`, `TryFormat` | Exact supported width set is OPEN; native width is target-dependent; overflow follows checked policy |
| unsigned integer widths (including sampled `uint32`, `uint8`, and `nuint`) | `MinValue`, `MaxValue`, `Zero`; same-width operations and checked/unchecked/explicit saturating conversions | Exact supported width set is OPEN; `uint32.AddWrapping` is a concrete sample |
| binary floating widths (including sampled `float32`) | `Zero`, `IsNaN`, `IsFinite`, `FusedMultiplyAdd`, `TryParse`, `TryFormat`, matching-width bit reinterpretation | Exact supported width set is OPEN; numeric comparison differs from key equivalence |
| decimal floating widths (including sampled `decimal32`) | `Zero`, classification, arithmetic, parse/format, numeric-key policy | Exact supported width set, cohorts, rounding and transcendentals need separate profiles |
| `char8`, `char16`, `char32`, `rune` | scalar validity, ordinal comparison, formatter where selected | Code units are not automatically complete Unicode scalars |
| `void`, `T?`, candidate `Option<T>` alias | `()`; presence test and checked payload access for option | No universal `default(T)`; optional normalization is not nested two-state wrappers |
| proposed `Lookup<T>`, `Result<T,E>` | case test and checked payload access | Case spelling and ownership/registration are OPEN; see §13 for result |

Primitive widths in this table are **candidate coverage**, not a compiler
required-type list. `int32` and `uint32` concrete sketches are in §6 and §8;
source spelling/availability for additional widths must be checked against
the target's approved primitive inventory before adoption. Unary operators,
literal typing, conversion witnesses and checked policy belong to the
[grammar](../specs/grammar-spec.md) and [conversions](../specs/conversions.md), not to a
fictional shared primitive base class.

## 2. Explicit equality, keying and formatting witnesses

The declarations below are opt-in witnesses, not approved primitive/root
conformance. IFormattable's exact logical requirement is specified with the
[standard text operations](../specs/language/construction-handlers.md#standard-text-operations);
the equality/key/parser declarations here remain candidates.
The accepted separation of value equality,
identity, key equality/hash, and formatting is specified in
[scalar mathematics and keys](../specs/libraries.md#scalar-mathematics-keys-and-conversion)
and [interface contracts](../specs/language.md#interfaces-access-and-generic-properties).

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

`IEqualityComparer<T>` and `IKeyComparer<T>` appear in the algorithm inventory.
Their exact primitive declarations and conformance remain proposed; they add
no default witness or string/root method.

`int32.GetHashCode()` is a concrete primitive member, not evidence of universal root inheritance and not itself a contradiction. The actual open choice is whether it promises the same numeric-key contract/name as the float/decimal `KeyEquals`/`GetKeyHashCode` pair or is a separate utility; either way, generic map-key use still needs a compatible explicit equality/hash witness.

### Proposed explicit primitive comparers

These concrete candidates in `Terrum.Collections.Algorithms` make the
selected equality/key policy usable without a default comparer, universal
root method or automatically inferred primitive conformance. The integer
candidate also supplies the separately declared ordering witness from §5.

```terrum
public struct Int32KeyComparer : IKeyComparer<int32>, IOrderComparer<int32>
{
    public Int32KeyComparer();
    public bool Equals(ref<int32> left, ref<int32> right);
    public uint64 GetHashCode(ref<int32> value);
    public int32 Compare(ref<int32> left, ref<int32> right);
}
public struct Float32IeeeEqualityComparer : IEqualityComparer<float32>
{
    public Float32IeeeEqualityComparer();
    public bool Equals(ref<float32> left, ref<float32> right);
}
public struct Float32NumericKeyComparer : IKeyComparer<float32>
{
    public Float32NumericKeyComparer();
    public bool Equals(ref<float32> left, ref<float32> right);
    public uint64 GetHashCode(ref<float32> value);
}
```

All three are **PROPOSED**, stateless explicit values. `Int32KeyComparer`
uses integer value equality/order; `Compare` returns negative/zero/positive
without overflowing subtraction, and hashing must accept every `int32`
without a checked numeric conversion that rejects negative values.
`Float32IeeeEqualityComparer` and `Float32NumericKeyComparer` apply the
distinct accepted operator-equality and numeric-key contracts in
[libraries](../specs/libraries.md#scalar-mathematics-keys-and-conversion);
choosing either comparer remains explicit and does not select a default
witness for another API.

The hash algorithm, collision-quality requirements and stability across
library/profile versions remain **OPEN**; no persistent wire identifier or
cryptographic property follows from `GetHashCode`. A bitwise float comparer,
floating total order, other widths and decimal cohort policies require their
own explicit candidates, not a fallback selected by these names. No hidden
process salt, ambient culture, allocation or NoThrow promise is introduced.

## 3. Text, encoding and writers

The canonical [view contract](../specs/libraries.md#views-shapes-and-tensor-primitives)
defines `Span<T>` access and the `TextSpan<TCodeUnit = void>` family,
including validated immutable text, encoding erasure, and backing
dependencies. The declarations below are candidate library signatures;
conditional members and the selected indexer form remain open.

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
public struct rune : IFormattable
{
    public uint32 Value { get; }
    public bool IsAscii { get; }
    public nuint Utf8CodeUnitCount { get; }
    public nuint Utf16CodeUnitCount { get; }
    public static bool IsValid(uint32 value);
    public static bool TryCreate(uint32 value, init ref<rune> result);
    public static bool TryDecodeSingleUtf8(Span<char8> source, init ref<rune> result);
    public static bool TryDecodeSingleUtf16(Span<char16> source, init ref<rune> result);
    public static bool TryDecodeSingleUtf32(Span<char32> source, init ref<rune> result);
    public bool TryEncodeUtf8(write Span<char8> destination, init ref<nuint> written);
    public bool TryEncodeUtf16(write Span<char16> destination, init ref<nuint> written);
    public bool TryEncodeUtf32(write Span<char32> destination, init ref<nuint> written);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}

```

```terrum
public interface ITextWriter
{
    mut bool TryWrite(TextSpan text);
    mut bool TryWriteScalar(rune value);
    mut bool TryWriteFormatted<TValue>(ref<TValue> value, int32 alignment, TextSpan format)
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
    public mut bool TryWriteFormatted<TValue>(ref<TValue> value, int32 alignment, TextSpan format)
        where TValue : IFormattable;
}
public class Text : IFormattable
{
    public TextSpan AsSpan();
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
    public static own<val<Text>> Create<TContent>([TextTarget] TContent content)
        where TContent : IFormattable;
}
```

Text/collection source evaluation follows the adopted
[eager construction contract](../specs/language/construction-handlers.md#eager-capture-and-acquisition):
capture operands before the affected builder acquisition or draining.
The parameter-local TextTarget annotations follow the adopted
[no-argument schema](../specs/language/construction-handlers.md#texttarget-parameter-schema)
over typed eager IFormattable content. Core writer/value logical signatures
follow the [standard operations](../specs/language/construction-handlers.md#standard-text-operations),
including int32 Unicode-scalar alignment. Concrete consumer/builder APIs and
physical capture representations remain candidates; no deferred hole
execution, ITextContent protocol or construction-evaluation policy is added.

| **OPEN** conditional family contract | Proposed member/relationship |
|---|---|
| `TextSpan<char8/char16/char32>` | `Length: nuint`; `AsSpan() -> Span<TCodeUnit>`; `AsTextSpan() -> TextSpan<void>`; implements `IContiguousSequence<TCodeUnit>` |
| `TextSpan<void>` | `TryProject<TRequested>(init ref<TextSpan<TRequested>> result) -> bool`, with `TRequested : [TextCodeUnit]`; false leaves output vacant |
| all `TextSpan` members | `IsEmpty`, scalar-ordinal comparisons and `TryFormat`; none promises writable text storage |

Conditional family membership/conformance remains **OPEN**; ordinary
extension lookup cannot establish a witness. The canonical text-view rules
cover erasure, checked projection, and ordinal comparison; see also
[character intent](../specs/conversions.md#character-intent). The concrete
`rune` declarations and their publication/encoding members below remain
proposed API.

Scalar validity and character intent for `rune` are defined in
[language](../specs/language.md#primitives-strict-arithmetic-and-text) and
[conversion policies](../specs/conversions.md#character-intent). These
`rune` members remain **PROPOSED** scalar services, not approval of
additional privileged Core members or an encoding default. The UTF counts
describe encoded code units, not display columns, grapheme clusters or a
native object layout.

Each `TryDecodeSingle` requires the entire supplied raw code-unit span to
encode exactly one scalar. Empty, incomplete, malformed or multi-scalar input
returns false with no published result; UTF-8 rejects overlong spellings,
surrogate encodings and out-of-range values, and UTF-16 rejects unpaired
surrogates. These are exact single-value decoders, not incremental readers;
the stateful conversion protocols below retain their distinct progress and
error reporting. Encoding writes one complete scalar with no added terminator
or byte-order mark. Insufficient capacity leaves destination and output
unchanged/vacant respectively; success reports the code-unit count and leaves
the remaining destination unchanged. Destinations contain initialized code
units before access. No replacement policy, normalization, casing or implicit
text validation is selected by these names.

Writer refusal and text publication follow the accepted
[typed-writer and text contract](../specs/libraries.md#collections-algorithms-and-application-services).
The concrete `Text.Create` representation/transfer and builder contracts
remain open.

### Text/view/writer signature families

| Type / proposed layer | Additional signature inventory | Source/ownership boundary |
|---|---|---|
| `Span<T>` / Core | `Length`; `Slice(start, length) -> Span<T>`; matched-access borrowed index; `IContiguousSequence<T>.AsSpan()` | Candidate indexer form; access/backing follow [view contracts](../specs/libraries.md#views-shapes-and-tensor-primitives) |
| `InlineArray<T,N>` / Core | `Length: nuint` (constant `N`); matched-access index; `AsSpan() -> Span<T>` | Candidate fixed inline shape; enclosing lifetime follows [lifetimes](../specs/lifetimes.md#three-independent-dependencies) |
| `TextSpan<TCodeUnit>` / Core | `IsEmpty`; `EqualsOrdinal(TextSpan/rune)`; `CompareOrdinal(TextSpan)`; `TryFormat(writer, format)` | Candidate family members; accepted erasure/projection rules are in [libraries](../specs/libraries.md#views-shapes-and-tensor-primitives) |
| `rune` / Core | `Value`, `IsAscii`, UTF code-unit counts, `IsValid`, `TryCreate`, exact `TryDecodeSingleUtf8/16/32`, `TryEncodeUtf8/16/32`, `TryFormat` | Proposed service signatures; scalar and literal rules are in [conversions](../specs/conversions.md#character-intent) |
| `Text` / Foundation or Application | `AsSpan() -> TextSpan`; `TryFormat(writer, format)`; `Create<TContent>(content) -> own<val<Text>>` | Copy/transfer/lease and native layout remain open |
| `TextBuilder<TCodeUnit>` / Foundation, **OPEN** registration | Constructors, `Length`, `Capacity`, `Reserve`, `Append`, `AppendScalar`, `TryAppend`, `TryAppendScalar`, `TryBuild`, `Clear` | Explicit code-unit capacity, whole-fragment allocation refusal; registration, backing and failure cleanup need checked contracts |
| `TextSearch` / Core | `ScalarCount`, `TryGetScalar`, `TrySliceScalars`, `StartsWithOrdinal`, `EndsWithOrdinal`, `TryIndexOfOrdinal`, `TryLastIndexOfOrdinal` | Scalar positions; borrowed slices retain encoding/backing; no culture, normalization or sentinel |
| `TextFormatting` / Core | `TryWrite<TWriter,TContent>(write ref<TWriter>, TContent) -> bool` | Follows the accepted [typed-writer contract](../specs/libraries.md#scalar-mathematics-keys-and-conversion) |
| `ITextWriter`, `SpanTextWriter<TCodeUnit>` / Core | `TryWrite`, `TryWriteScalar`, `TryWriteFormatted`; `Written` for bounded writer | Proposed surface over accepted [writer behavior](../specs/libraries.md#collections-algorithms-and-application-services) |
| `ITextDecoder<TCodeUnit>`, `ITextEncoder<TCodeUnit>` / Foundation | `Convert`, `Finish`, `Reset` with `TextConversionProgress`; concrete UTF-8 witnesses below | Proposed incremental codec protocol; explicit EOF and progress decisions remain as specified below |
| `Utf8TextReader<TStream>` / Application, **OPEN** | `ReadScalar(init ref<rune>) -> Result<bool,Error>`; `ReadLine(init ref<own<val<Text>>>) -> Result<bool,Error>` | Borrows an explicit byte stream; EOF differs from empty line |

The proposed explicit encoding protocols below retain incremental state and
select malformed-input policy on decoder construction. No encoding is
defaulted from a `uint8` span, a terminal handle, or a file path. These
protocols do not change validated `TextSpan`'s encoding-erasure rules.

### Proposed text construction, search and incremental transcoding

The following concrete signatures in candidate `Terrum` expand the text
inventory; they are reference-only declarations under the notation above. The proposed
`TextBuilder<TCodeUnit>` is not automatically the registered text-literal
handler; its registration and `Text.Create` forwarding remain **OPEN**.

Its candidate instance TryBuild below is an explicit reusable API, not the
adopted [consuming source completion](../specs/language/construction-handlers.md#consuming-builder-completion)
requirement. A checked nominal adapter must own the supplied builder value,
publish the complete result and clean residual state without assuming reset.

```terrum
public mut unique class TextBuilder<TCodeUnit>
    where TCodeUnit : [TextCodeUnit]
{
    public TextBuilder();
    public TextBuilder(nuint capacityHint);
    public nuint Length { get; }
    public nuint Capacity { get; }
    public mut Result<void, Error> Reserve(nuint minimumCapacity);
    public mut void Append(TextSpan source);
    public mut void AppendScalar(rune value);
    public mut bool TryAppend(TextSpan source);
    public mut bool TryAppendScalar(rune value);
    public mut bool TryBuild(init ref<own<val<Text>>> result);
    public mut void Clear();
}

public static class TextSearch
{
    public static nuint ScalarCount(TextSpan source);
    public static bool TryGetScalar(TextSpan source, nuint scalarIndex,
        init ref<rune> value);
    public static bool TrySliceScalars(TextSpan source, nuint scalarStart,
        nuint scalarLength, init ref<TextSpan> result);
    public static bool StartsWithOrdinal(TextSpan source, TextSpan prefix);
    public static bool EndsWithOrdinal(TextSpan source, TextSpan suffix);
    public static bool TryIndexOfOrdinal(TextSpan source, TextSpan needle,
        init ref<nuint> scalarIndex);
    public static bool TryLastIndexOfOrdinal(TextSpan source, TextSpan needle,
        init ref<nuint> scalarIndex);
}

public enum MalformedTextPolicy : int32
{
    Reject,
    Replace
}
public enum TextConversionStatus : int32
{
    NeedInput,
    NeedOutput,
    InvalidData,
    Complete
}
public struct TextConversionProgress
{
    public nuint Consumed { get; }
    public nuint Written { get; }
    public TextConversionStatus Status { get; }
}
public interface ITextDecoder<TCodeUnit>
    where TCodeUnit : [TextCodeUnit]
{
    mut TextConversionProgress Convert(Span<TCodeUnit> source,
        write Span<rune> destination);
    mut TextConversionProgress Finish(write Span<rune> destination);
    mut void Reset();
}
public interface ITextEncoder<TCodeUnit>
    where TCodeUnit : [TextCodeUnit]
{
    mut TextConversionProgress Convert(Span<rune> source,
        write Span<TCodeUnit> destination);
    mut TextConversionProgress Finish(write Span<TCodeUnit> destination);
    mut void Reset();
}
public mut unique struct Utf8Decoder : ITextDecoder<char8>
{
    public Utf8Decoder(MalformedTextPolicy policy);
    public mut TextConversionProgress Convert(Span<char8> source,
        write Span<rune> destination);
    public mut TextConversionProgress Finish(write Span<rune> destination);
    public mut void Reset();
}
public mut unique struct Utf8Encoder : ITextEncoder<char8>
{
    public Utf8Encoder();
    public mut TextConversionProgress Convert(Span<rune> source,
        write Span<char8> destination);
    public mut TextConversionProgress Finish(write Span<char8> destination);
    public mut void Reset();
}
public mut unique struct Utf16Decoder : ITextDecoder<char16>
{
    public Utf16Decoder(MalformedTextPolicy policy);
    public mut TextConversionProgress Convert(Span<char16> source,
        write Span<rune> destination);
    public mut TextConversionProgress Finish(write Span<rune> destination);
    public mut void Reset();
}
public mut unique struct Utf16Encoder : ITextEncoder<char16>
{
    public Utf16Encoder();
    public mut TextConversionProgress Convert(Span<rune> source,
        write Span<char16> destination);
    public mut TextConversionProgress Finish(write Span<char16> destination);
    public mut void Reset();
}
```

The builder copies or transcodes each fragment into its chosen code-unit
encoding while the fragment is borrowed; it retains no source view. A failed
`TryBuild` leaves `result` vacant and the builder responsible for its storage;
successful publication transfers exactly one owned `Text`. `TextSearch`
returns scalar offsets, not byte offsets; an empty needle matches position
zero (or the source's scalar length for the last match). Ordinal search does
not normalize or fold. Its `TryIndexOfOrdinal`/`TryLastIndexOfOrdinal` false
outcomes leave `scalarIndex` vacant.

The existing ordinal-search signatures use the same
[Unicode scalar-sequence identity across encodings](../specs/type-families.md#text-views),
not byte identity or UTF-16 code-unit ordering. Source and needle may have
different validated encodings; matching does not require allocating a
transcoded copy. A match consumes the complete needle at a source scalar
boundary. It cannot begin inside a UTF-8 sequence or surrogate pair, or
finish with only part of a scalar. The original views retain their encoding
and ordinary access/backing dependencies throughout the call.

| Proposed operation | Complete match and boundary contract |
|---|---|
| `StartsWithOrdinal` | True exactly when the prefix's scalar sequence matches the beginning of the source; an empty prefix succeeds, including on empty source. |
| `EndsWithOrdinal` | True exactly when the suffix's scalar sequence matches the end of the source; an empty suffix succeeds, including on empty source. |
| `TryIndexOfOrdinal` | Publish the lowest source scalar index at which the entire needle matches. An empty needle succeeds at zero. |
| `TryLastIndexOfOrdinal` | Publish the highest such source scalar index, including overlapping matches. An empty needle succeeds at the source's scalar count. |

A nonempty needle longer than the source in scalars has no match. A false
search leaves the initialization output vacant; it does not publish zero,
the source length or an unknown-index sentinel. Zero and the end boundary
can both be successful outputs, with the end available for an empty needle.
The published index is an independent numeric value, not a borrowed view:
it neither keeps the source alive nor grants later access to its storage.
Conflicting loans, invalid lifetime/backing or other checked failures are
not converted to a no-match result. No culture, case folding, normalization,
grapheme segmentation or NUL termination is implied.

For example, let the source scalar sequence be
`U+0041 U+1F600 U+0041 U+1F600` and the needle be `U+1F600`.
Both searches publish scalar indices `1` and `3`, respectively, regardless
of either view's encoding. These positions differ from raw storage offsets:

| Source encoding | First/last scalar index | First/last encoded code-unit offset |
|---|---|---|
| UTF-8 | `1`, `3` | `1`, `6` |
| UTF-16 | `1`, `3` | `1`, `4` |
| UTF-32 | `1`, `3` | `1`, `3` |

In `aaa`, the needle `aa` matches at zero and one; last-search returns one,
not zero from skipping overlapping candidates. An encoded `U+0000` is an
ordinary scalar and may occur in either input. Canonically equivalent
`U+00E9` and `U+0065 U+0301` do not match ordinally. These examples clarify
the **proposed library contracts**, not new compiler support or an exact
search/probe algorithm, complexity bound or hidden scalar-index cache.

`ScalarCount`, `TryGetScalar` and `TrySliceScalars` are **PROPOSED** additions
for inspecting and borrowing validated text by Unicode scalar position.
`ScalarCount` counts the entire view's scalars; empty text returns zero.
It does not count UTF code units, bytes, grapheme clusters or display columns.
The count fits `nuint` because each scalar occupies at least one code unit
in the underlying bounded view.

`TryGetScalar` uses a zero-based scalar index. True publishes exactly one
valid `rune`; an index at or beyond the scalar count returns false and leaves
`value` vacant. An encoded NUL is a valid scalar, not EOF or a missing-value
sentinel. Access does not normalize, fold, replace or copy the text's backing.

`TrySliceScalars` checks that the complete requested scalar range lies within
the source and that both encoded endpoints are scalar boundaries. True
publishes a borrowed validated `TextSpan` in the source's actual encoding,
retaining its access, backing/root/lease and lifetime; no allocation,
transcoding, mutation or owner transfer occurs. An empty slice at any valid
boundary, including the end, succeeds and retains those dependencies.
An out-of-range start or length returns false with no published view.
Bounds must be checked without overflowing a start-plus-length sum, and a
successful slice never divides a UTF-8 sequence or UTF-16 surrogate pair.

These operations consume already validated text, not arbitrary raw code-unit
spans. False is only the specified range failure, not a substitute for a
conflicting loan, invalid source lifetime or other checked failure. Scalar
indexing/slicing can require scanning the encoded prefix; no constant-time
index, cached scalar map, hidden allocation or materialization is promised.
The returned slice has the ordinary validated encoding-erased view type;
these signatures add no conditional family member or typed projection.
Exact source-to-output dependency and conditional-publication metadata remain
**OPEN**; the initialization output alone is not a proof of the retained loan.

The builder constructor, capacity/reservation and allocation-refusal
additions are **PROPOSED**. `Length` counts initialized code units in the
builder's declared encoding, not Unicode scalars or bytes; `Capacity`
counts usable code-unit storage and is at least `Length`. The empty
constructor starts with no text. Capacity construction and `Reserve`
request at least the given code-unit capacity without creating readable
text in unused storage. Reservation never reduces capacity or changes
existing text; an already sufficient capacity succeeds without allocation.
Expected allocation failure returns `Failure(Error)` and preserves text,
length and capacity. Constructor-failure transport remains **OPEN**, as
for the other managed collection constructors.

`TryAppend` and `TryAppendScalar` return false only on expected allocation
refusal, preserving all prior text, length and capacity. Success accepts the
entire borrowed fragment or complete scalar in the chosen encoding, without
retaining the source view. Required encoded length and capacity arithmetic
must be checked before publication; invalid arithmetic, conflicting loans
and other checked failures are not allocation-refusal results. An empty
fragment succeeds without allocation or content changes. A scalar needing
multiple code units is never partly appended on a false result.
`Clear` releases the initialized text while retaining capacity; unused
storage is not a sequence of default code units to expose as text.

This adds no automatic literal-handler registration, public writable
builder-buffer view, allocator singleton or failure rollback for the
ordinary throwing `Append` methods. Their exceptional partial-progress
contract, the builder's reusable state after successful `TryBuild`, and
effect/publication metadata remain **OPEN**. Transferring an owned result
does not permit shared writable aliases to its immutable text storage.

`Convert` accepts the reported prefix and never partially writes an encoded
scalar. Counts are in input and output *elements*, respectively. The codec
retains incomplete input in its private state only when it reports that input
as consumed; the caller advances solely by `Consumed`. `NeedOutput` requires more destination capacity before retrying the
unconsumed suffix; it may still report an already accepted prefix or
buffered incomplete input. Zero-length inputs do not imply EOF.
`Finish` explicitly signals EOF, drains pending output, and reports
`InvalidData` for a truncated sequence under `Reject`; `Replace` emits a
replacement scalar when output capacity permits. On `InvalidData`, counts
still identify the accepted prefix and offending units in the current input
remain unconsumed. An incomplete prefix consumed by an earlier call remains
codec-owned state until reset; reporting an error cannot retroactively return
those units to the caller. `Reset` abandons pending state. Source and
destination cannot overlap.
All destination slots are valid initialized values before the call; `write`
permits replacement, not construction of arbitrary uninitialized `T`.
An encoder consumes validated `rune` scalars, so it needs no malformed-input
policy; `InvalidData` applies to decoding malformed code units.

The UTF-16 services are **PROPOSED** counterparts using the same progress
protocol, not byte-stream codecs or new primitive validity rules. Decoder
input counts are `char16` code units and output counts are complete `rune`
scalars. A high surrogate at a fragment boundary may become consumed,
codec-owned pending state; the next fragment can complete its pair. A lone
low surrogate or an unmatched high surrogate follows the selected malformed
policy. `Finish` distinguishes a pending high surrogate from a successfully
finished input; an empty `Convert` call cannot silently discard it.

The encoder writes one code unit for a BMP scalar or a complete two-unit
surrogate pair for a supplementary scalar. With insufficient destination
space it does not consume that scalar or publish half a pair. For example,
a one-element output span cannot accept a supplementary scalar even when it
is otherwise empty. This preserves retry from the reported unconsumed
suffix, rather than requiring callers to reconstruct a partially emitted
scalar. Stateful decoding of a raw source-unit span does not certify it as a
valid immutable `TextSpan`; conversion and text-view validation have distinct
publication contracts.

These codecs operate on declared code units, without selecting a byte order,
stripping a byte-order mark or negotiating an encoding from content. U+FEFF
is ordinary scalar data in this proposed profile. Explicit byte framing,
replacement-subsequence granularity and post-`Finish` reuse rules remain
**OPEN**; they cannot be inferred from the names or an empty input. `Reset`
provides the explicit pending-state reset already shared by the UTF-8
services. Neither profile implies allocation, normalization or replacement
of malformed source text inside the compiler's lossless lexer.

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

The accepted `Span<T>` family, qualifier behavior, opaque backing dependencies,
and explicit interface projection are specified in
[library views](../specs/libraries.md#views-shapes-and-tensor-primitives)
and [lifetimes](../specs/lifetimes.md#three-independent-dependencies).
The candidate borrowed indexer and stored-view anchor/ABI remain open; a
by-value accessor for unconstrained unique `T` is not proposed.

```terrum
public struct InlineArray<T, const uint64 N>
    where T : [Sized]
{
    public nuint Length { get; }
    [MatchedAccess] public ref<T> this[nuint index] { get; }
    [MatchedAccess] public Span<T> AsSpan();
}
```

The proposed length formal uses the approved `uint64` constant-generic form
([grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments));
exact target layout limits and zero-length storage remain open. `Length`
reports `N` in the target indexing domain, and
instantiations must reject unrepresentable length/storage products rather
than truncate them. Borrowing retains enclosing-owner lifetime and access
under [lifetimes](../specs/lifetimes.md).

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
    public mut bool TryAllocate(nuint byteLength, nuint alignment,
        init ref<AllocationLease> storage);
    public mut void Dispose();
}
public mut unique struct Buffer<T>
    where T : [Sized]
{
    public nuint Capacity { get; }
    public nuint Count { get; }
    public static Buffer<T> Adopt(nuint capacity, AllocationLease storage);
    public mut void Initialize(nuint index, T value);
    public mut bool TryTakeLast(init ref<T> value);
    public Span<T> AsSpan();
    [ViewOf(Access.Write)] public mut write Span<T> GetWriteAccess();
    public mut void Clear();
    public mut void Dispose();
}
public mut unique struct Vector<T>
    where T : [Sized], [Copy]
{
    public nuint Count { get; }
    public nuint Capacity { get; }
    public static Vector<T> Create(AllocatorContext allocator);
    public T this[nuint index] { get; mut set; }
    public bool TryGet(nuint index, init ref<T> value);
    public mut void Add(T value);
    public mut bool TryAdd(T value);
    public mut void Insert(nuint index, T value);
    public mut bool TryInsert(nuint index, T value);
    public mut void RemoveAt(nuint index);
    public mut bool TryTakeLast(init ref<T> value);
    public mut void Reserve(nuint capacity);
    public mut bool TryReserve(nuint capacity);
    public Span<T> AsSpan();
    [ViewOf(Access.Write)] public mut write Span<T> GetWriteAccess();
    public mut void Clear();
    public mut void Dispose();
}
```

The accepted lease/provider distinction, `Buffer<T>.Adopt` validation and
initial `Vector<T>` growth profile are specified in the [library inventory](../specs/libraries.md#dependency-layers-and-package-boundaries).
The signatures below add proposed members and profiles; broader element and
allocator policies, view/guard metadata and failure transport remain open.
`Dispose` here names explicit release on a particular owned lease/provider;
it neither imports .NET `IDisposable` nor implies a universal object finalizer.
Provider cleanup must not invalidate allocations whose release responsibility
was transferred elsewhere.

These are **PROPOSED** complete member sets for the sampled native owners,
not cleanup registrations or implemented generic exports. `TryAllocate`
distinguishes an allocation refusal from invalid arguments: false leaves the
lease output vacant, true publishes the complete allocation/release
responsibility, and invalid size/alignment requests retain checked
validation rather than masquerading as resource exhaustion.

`Buffer.Initialize` extends the initialized prefix at exactly `Count`, with
`Count < Capacity`; it does not expose holes or overwrite a live owner without
its replacement contract. Both span accessors expose only initialized
elements. `GetWriteAccess` lends those existing elements exclusively and does
not grant construction authority over unused capacity. `Clear` cleans the
initialized prefix while retaining storage capacity. Explicit disposal ends
the owned storage's lifetime and must not proceed while any conflicting view
remains live. Subsequent storage access cannot silently produce an empty
span. Exact disposed-state diagnostics, release-failure reporting and
implicit no-throw cleanup metadata remain open; the `Dispose` name alone
supplies none of them. A vector retains its explicitly supplied allocator
dependency for future growth rather than consulting an ambient provider.

`Buffer.TryTakeLast` and `Vector.TryTakeLast` are **PROPOSED** initialized-tail
extraction members. They transfer the last initialized element into a vacant
output rather than copy an element and then destroy its original. The buffer
form therefore does not add a `[Copy]` requirement; the vector retains its
existing `[Sized], [Copy]` profile for its other operations.

An empty live owner returns false, leaves the output vacant and preserves
contents, count and capacity. True publishes the complete moved value,
decrements `Count` by one and retires the former final slot as vacant storage.
Earlier elements keep their order, and the allocation and capacity remain
owned by the buffer/vector. The moved value's cleanup/release responsibility
passes to the caller; extraction must not run its cleanup in the source or
later destroy the vacant slot during `Clear` or `Dispose`. No `default(T)`,
copy of an affine owner or readable stale tail is manufactured.

The output must satisfy ordinary vacant-destination, move, capture/rooting
and lifetime checks. Dependencies carried by `T` are retained by the moved
result; extraction neither extends a borrowed lifetime nor rebases an
interior reference. Conflicting backing loans must have ended before
structural mutation, including when no allocation or relocation occurs.
Disposed access, loan conflicts and other checked failures are not empty
results. Conditional publication, transferred cleanup/root responsibility
and source/output dependency metadata remain **OPEN**; the member spelling
alone supplies no executable move or NoThrow guarantee.

The vector's indexed access, insertion/removal and allocation-refusal
overloads are **PROPOSED** additions for its existing `[Sized], [Copy]`
profile. `TryGet` checks the initialized prefix: false leaves the output
vacant, and true publishes a copied element. The indexer and `RemoveAt`
require `index < Count`; `Insert` and `TryInsert` accept `index <= Count`.
Insertion preserves prefix order, and removal shifts later elements toward
the removed position before retiring the former final slot. These operations
must obey typed initialization, rooting/barrier and replacement/cleanup
contracts; `[Copy]` does not authorize untyped byte moves or exposing the
now-vacant tail as a readable element.

`TryAdd`, `TryInsert` and `TryReserve` report expected allocation refusal as
false with prior contents, count and capacity preserved. Invalid indexes,
capacity/count arithmetic overflow, conflicting loans and other checked
failures do not become false. An already sufficient reservation succeeds
without allocation; reservation never reduces capacity or changes count.
Successful growth preserves existing element order and publishes replacement
storage only after all required initialization succeeds. Non-`Try` operations
surface allocation failure through the provider's declared checked-failure
contract, not a silent no-op. Neither overload family adds implicit rollback
of provider side effects or retries the provider after refusal.

All structural operations require their conflicting backing loans to have
ended, even if spare capacity means no relocation is needed. The indexer
copies its result rather than returning a slot loan; writable span access
still requires the explicit exclusive accessor. Ordinary readonly span access
does not authorize replacement through an alias. Disposed-state and provider
lifetime checks remain those of the owner above; these conveniences neither
reacquire a disposed allocation nor select a global allocator.

### Proposed owned double-ended storage

These **PROPOSED** `Terrum.Collections` declarations complete the existing
owned-deque inventory for the same `[Sized], [Copy]` element profile as
`Vector<T>`. They describe a logical sequence with front/back access, not a
promise of one contiguous span or a selected physical ring-buffer layout.
The allocator is explicitly supplied and retained for future growth.

```terrum
public mut unique struct Deque<T>
    where T : [Sized], [Copy]
{
    public static Deque<T> Create(AllocatorContext allocator);
    public nuint Count { get; }
    public nuint Capacity { get; }
    public T this[nuint index] { get; mut set; }
    public bool TryGet(nuint index, init ref<T> value);
    public bool TryPeekFront(init ref<T> value);
    public bool TryPeekBack(init ref<T> value);
    public mut void PushFront(T value);
    public mut void PushBack(T value);
    public mut bool TryPushFront(T value);
    public mut bool TryPushBack(T value);
    public mut bool TryPopFront(init ref<T> value);
    public mut bool TryPopBack(init ref<T> value);
    public mut void Reserve(nuint capacity);
    public mut bool TryReserve(nuint capacity);
    public DequeSegments<T> GetSegments();
    [ViewOf(Access.Write)] public mut DequeWriteAccess<T> GetWriteAccess();
    public mut void Clear();
    public mut void Dispose();
}
public struct DequeSegments<T>
    where T : [Sized], [Copy]
{
    public Span<T> First { get; }
    public Span<T> Second { get; }
}
public mut unique struct DequeWriteAccess<T>
    where T : [Sized], [Copy]
{
    [ViewOf(Access.Write)] public write Span<T> First { get; }
    [ViewOf(Access.Write)] public write Span<T> Second { get; }
    public mut void Close();
}
```

Logical index zero names the front, and `Count - 1` names the back only
when nonempty. The indexer requires `index < Count`; `TryGet` returns false
for an index outside the initialized sequence and leaves its output vacant.
Empty peek/pop operations similarly return false without publication;
success publishes one copied value. A successful pop removes exactly that
end element and retires the vacated slot. Pushes add exactly one element at
the selected end without changing the relative order of existing elements.
Spare capacity is not readable initialized `T` storage, and no operation
constructs `default(T)` to fill it.

`Capacity >= Count`. Reservation never reduces capacity or changes the
logical sequence; a sufficient capacity succeeds without allocation.
The `TryPush`/`TryReserve` operations return false only for expected
allocation refusal and preserve prior contents, count and capacity on that
outcome. Invalid arithmetic, conflicting loans, disposed access and other
checked failures do not become false. Ordinary push/reserve expose the
provider's declared checked allocation-failure contract. Growth publishes
replacement storage only after complete checked initialization; it neither
retries a refusing provider nor promises rollback of its external effects.
`Clear` retires initialized elements while retaining capacity.

`GetSegments` returns two readonly loans whose concatenation is the complete
front-to-back logical sequence. Their lengths sum to `Count`, their nonempty
ranges are disjoint, and no vacant capacity is exposed. Either segment may
be empty; callers must not infer a stable split point, physical offset or
single-span representation. Both descriptors retain the deque's backing
dependency, including copies or derived slices. Empty results are valid for
an empty live deque, not a success-shaped substitute for disposed access.

`GetWriteAccess` instead creates one exclusive guard over that initialized
sequence. Its two writable spans follow the same logical order and disjoint
coverage; they permit element replacement, not resizing or constructing
vacant capacity. Structural mutation cannot occur behind either readonly
segment loans or the exclusive guard, even when no allocation would occur.
Every alias and reentrant operation checks conflicting authority.
`Close` releases the guard only after all derived span loans end; repeated
close is harmless, and segment access after close is a checked invalid-state
failure. A readonly segment pair has no owner-release operation.
Exact returned-dependency/guard metadata, implicit cleanup registration and
failure transport remain **OPEN**; these signatures do not establish a
concurrency protocol, stable element addresses or generic cursor selection.

### Proposed borrowed arena and typed rental storage

These **PROPOSED** `Terrum.Collections` declarations expand the storage
inventory without introducing a global allocator. `ByteArena` is a
fixed-capacity borrowed byte arena, not the owned/growable arena family.
The initial pool element profile is `[Sized], [Copy]`; the rental itself
remains a unique release owner.

```terrum
public mut unique struct ByteArena
{
    [Captures(nameof(storage))]
    public ByteArena(write Span<uint8> storage);
    public nuint Capacity { get; }
    public nuint Used { get; }
    public mut write Span<uint8> Allocate(nuint byteLength, nuint alignment);
    public mut bool TryAllocate(nuint byteLength, nuint alignment,
        init ref<write Span<uint8>> storage);
    public mut void Reset();
    public mut void Dispose();
}
public mut unique struct MemoryPool<T>
    where T : [Sized], [Copy]
{
    public static MemoryPool<T> Create(AllocatorContext allocator);
    public mut MemoryOwner<T> Rent(nuint minimumCapacity);
    public mut bool TryRent(nuint minimumCapacity, init ref<MemoryOwner<T>> owner);
    public mut void Dispose();
}
public mut unique struct MemoryOwner<T>
    where T : [Sized], [Copy]
{
    public nuint Capacity { get; }
    public nuint Count { get; }
    public mut void Initialize(nuint index, T value);
    public Span<T> AsSpan();
    [ViewOf(Access.Write)] public mut write Span<T> GetWriteAccess();
    public mut void Clear();
    public mut void Dispose();
}
```

| Candidate owner | Storage, publication and release contract |
|---|---|
| `ByteArena` | Captures exclusive access to caller-provided initialized bytes. Allocation publishes a disjoint slice from the remaining backing; `Used` includes alignment padding. Capacity never grows and existing slices never relocate. |
| `MemoryPool<T>` | Owns the explicitly supplied allocator context and reusable allocations. A successful rent publishes one allocation-specific return obligation; the pool itself is not an ambient provider or a freely copied handle. |
| `MemoryOwner<T>` | Owns one current rental with `Capacity >= minimumCapacity` and initially `Count == 0`. Returned spans expose only its initialized prefix and retain the rental's backing/release dependency. |

Arena allocation checks a supported nonzero power-of-two alignment and
the actual returned address, not merely the offset within the byte span.
Padding and the complete requested range must fit without overflow before
`Used` changes. `TryAllocate` returns false only for insufficient capacity,
leaving state unchanged and its output vacant; invalid size/alignment
requests retain checked validation. `Allocate` reports insufficient capacity
through the checked failure policy. Zero-length slices still retain backing
and access dependencies; no arbitrary typed values are constructed from
these bytes.

`Reset` reuses the backing only after every allocated-slice loan has ended.
It resets allocation state, not byte contents. `Dispose` likewise requires
the loans to end and releases the captured storage access, not the caller's
allocation. Additional allocations may coexist with earlier disjoint slices
only under a checked backing/region contract; a mutable receiver alone does
not establish that authority. Exact slice/arena dependency metadata,
alignment profiles and zero-length placement remain **OPEN**.

`TryRent` distinguishes resource refusal from invalid requests: false
publishes no rental; true publishes its complete return responsibility.
Size products, alignment and required tracing/barrier support are validated
for `T`, as for `Buffer<T>`. Reused storage does not expose the previous
renter's live elements or manufacture initialized `T` with `default(T)`.
`Initialize` extends the prefix at exactly `Count`, while `GetWriteAccess`
lends only existing elements. `Clear` cleans the prefix but keeps the rental.

Rental disposal requires its element/view loans to have ended, cleans
initialized elements and returns its allocation once; no public `Return(owner)`
path duplicates that responsibility. Pool disposal requires all rentals to
have been returned before releasing its retained allocations and provider.
Merely clearing a rental, including an empty rental, does not return it.
Outstanding rental dependencies must block pool disposal through every
alias, not by silently invalidating an owner. This proposes no concurrent
pool safety, automatic zeroing guarantee, unrestricted raw capacity view or
affine-element storage. Exact retained-provider/return metadata, release
failure transport and implicit cleanup registration remain **OPEN**.

### `MemoryPrimitives`

The accepted single-type memory API direction and storage boundaries are in
[library memory primitives](../specs/libraries.md#shared-memory-and-explicit-memory-primitives)
and the [memory-model surface](../specs/memory-model.md#library-access-surface).
The declarations below remain proposed signatures; `location` means a
checked access path, not an unqualified raw pointer, and each operation still
needs an eligible representation/width for its target.

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

These **PROPOSED** signatures select names and result shapes over the accepted
families in the [memory-model library surface](../specs/memory-model.md#library-access-surface).
That contract also specifies defaults, order validity, bitwise CAS semantics,
progress, and target-dependent non-temporal/cache obligations; the accepted
[CAS order-pair table](atomic-cas-order-pairs.md) gives the nine valid pairs.
Still open here are exact source/overload forms, eligible representation and
range profiles, richer CAS declaration, and target support. The named
`CompareExchangeObservation<T>` remains a candidate alternative to the
success-only result, not an approved API.

Previous/updated-value semantics are defined by the
[memory-model result forms](../specs/memory-model.md#access-and-atomic-operation-families).
`[Copy]` and `[Sized]` alone do not establish byte-representation
eligibility; the proposed unaligned byte-span overload excludes
managed-reference-containing types. Collector-aware typed-memory operations
need a separate explicit contract.

### Storage and memory inventory beyond the sample

| Family / namespace | Proposed signatures or members | Required authority |
|---|---|---|
| `AllocationLease` / `Terrum.Collections` | `ByteLength`, `Alignment`, `Dispose()`; optional checked `SliceBytes(offset, length) -> Span<uint8>` | Accepted lease/provider distinction: [library inventory](../specs/libraries.md#dependency-layers-and-package-boundaries); byte slicing remains a candidate |
| `AllocatorContext` / `Terrum.Collections` | `Allocate(bytes, alignment) -> AllocationLease`, `Dispose()`; `TryAllocate(..., init ref<AllocationLease>) -> bool` | Accepted retained-provider role: [library inventory](../specs/libraries.md#dependency-layers-and-package-boundaries); exact lifetime/release transfer remains open |
| `Buffer<T>` / `Terrum.Collections` | `Capacity`, `Count`, `Adopt`, `Initialize`, moving `TryTakeLast`, `AsSpan`, `GetWriteAccess`, `Clear`, `Dispose` | Accepted initialized-prefix/adoption profile: [library inventory](../specs/libraries.md#dependency-layers-and-package-boundaries); tail extraction remains a candidate |
| `Vector<T>` / `Terrum.Collections` | `Create`, `Count`, `Capacity`, copied index/`TryGet`, `Add`/`TryAdd`, `Insert`/`TryInsert`, `RemoveAt`, moving `TryTakeLast`, `Reserve`/`TryReserve`, span access, `Clear`, `Dispose` | Accepted initial Copy-profile growth: [library inventory](../specs/libraries.md#dependency-layers-and-package-boundaries); added members remain proposals |
| `Deque<T>` / `Terrum.Collections` | Explicit-provider `Create`, count/capacity/index/peek, front/back push/pop, reservation, readonly segment pair and exclusive writable guard, `Clear`, `Dispose` | Logical front-to-back initialized sequence; disjoint two-span coverage and retained backing; no fabricated contiguous span |
| `ByteArena` / `Terrum.Collections` | Borrowed-backing constructor, `Capacity`, `Used`, `Allocate`, `TryAllocate`, `Reset`, `Dispose` | Fixed initialized byte backing; disjoint-slice, alignment and reset/disposal loan checks; owned/growable arena remains OPEN |
| `MemoryPool<T>`, `MemoryOwner<T>` / `Terrum.Collections` | Explicit-provider `Create`, `Rent`/`TryRent`; rental `Capacity`, `Count`, `Initialize`, span access, `Clear`, `Dispose` | Unique rental/return responsibility; initialized prefix versus pooled capacity; pool disposal requires all rentals returned |
| `MemoryPrimitives` / `Terrum` | `AtomicRead/Write`, RMW families, strong CAS/observation, `Fence`, `CompilerFence`, non-temporal read/write/copy/complete, cache writeback/invalidate | Accepted families/defaults: [memory-model surface](../specs/memory-model.md#library-access-surface); source forms and observation record remain candidates |

The arena/pool declarations and rows are coverage candidates, **not** exports
in the current `foundation.api.terrum`. Neither allocator nor GC
provider is silently global. Native `T` storage is permitted only if its
initialization, alignment, move/copy, cleanup and any tracing requirements
can be met; a `Span<uint8>` is not an arbitrary typed allocation.

## 5. Collections, iteration and eager algorithms

Initial managed collections from `collections.api.terrum` (design-only).
The collection-builder type argument below follows the adopted
[constructed type-recipe schema](../specs/language/construction-handlers.md):
`typeof(ListBuilder<T>)` binds the target's own formal and substitutes forward.
The builder types and their complete operation/capture-representation/
lifecycle protocols remain candidate APIs, not executable admission from
this type reference. The adopted eager rule evaluates all element and
spread-source expressions before builder acquisition; a captured spread
is enumerated only when its drain position is reached.
For a direct cursor, eager capture adopts its value/close obligation without
advancing; abandonment closes it even if drain never reaches that spread.
An unreached factory sequence is not acquired.
Plain construction maps Boolean refusal to ordinary `require` failure;
explicit builder/writer calls retain their declared status/output contracts.

The instance TryBuild candidates below do not implement Core IBuilder's
[consuming source completion](../specs/language/construction-handlers.md#consuming-builder-completion)
by spelling. Source participation requires the selected static witness and
checked result/cleanup relation, directly or through an explicit adapter.

```terrum
[CollectionBuilder(typeof(ListBuilder<T>))]
public mut class List<T> : ISequence<T, ListCursor<T>>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public List();
    public List(nuint capacity);
    public nuint Count { get; }
    public nuint Capacity { get; }
    public bool TryGet(nuint index, init ref<T> value);
    public mut void Add(T value);
    public mut void Insert(nuint index, T value);
    public mut void Reserve(nuint capacity);
    public T this[nuint index] { get; mut set; }
    public mut void Clear();
    public mut void RemoveAt(nuint index);
    [ViewOf(Access.Write)] public mut ListWriteAccess<T> GetWriteAccess();
    [ViewOf(Access.Write, "access")]
    public mut bool TryGetWriteAccess(init ref<ListWriteAccess<T>> access);
    public ListCursor<T> GetEnumerator();
}
public mut unique struct ListCursor<T> : ICursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
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
    public mut void Close();
}
public mut class LinkedListNode<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public LinkedListNode(T value);
    public T Value { get; mut set; }
    [MatchedAccess] public LinkedListNode<T>? Previous { get; }
    [MatchedAccess] public LinkedListNode<T>? Next { get; }
}
[CollectionBuilder(typeof(LinkedListBuilder<T>))]
public mut class LinkedList<T> : ISequence<T, LinkedListCursor<T>>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public LinkedList();
    public nuint Count { get; }
    [MatchedAccess] public LinkedListNode<T>? First { get; }
    [MatchedAccess] public LinkedListNode<T>? Last { get; }
    public mut write LinkedListNode<T> AddFirst(T value);
    public mut void AddFirst(write LinkedListNode<T> node);
    public mut write LinkedListNode<T> AddLast(T value);
    public mut void AddLast(write LinkedListNode<T> node);
    public mut write LinkedListNode<T> AddBefore(write LinkedListNode<T> anchor, T value);
    public mut void AddBefore(write LinkedListNode<T> anchor, write LinkedListNode<T> node);
    public mut write LinkedListNode<T> AddAfter(write LinkedListNode<T> anchor, T value);
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
public mut unique struct LinkedListCursor<T> : ICursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
}
```

The accepted managed collection, guard, builder, node-identity and cursor
contracts are specified in
[libraries](../specs/libraries.md#collections-algorithms-and-application-services).
These candidate signatures retain the initial
`[ManagedStorable], [Copy], [Sized]` profile; it is not a permanent ban on
reference/affine values. Private buffer/access fields and public ABI/layout
remain unspecified.

The constructors, capacity/insertion members and concrete cursor
implementations remain **PROPOSED**. Their cursor signatures implement the
[selected value-iteration requirements](../specs/language/iteration.md),
including mandatory Close; concrete backing/guard representation is not
supplied by a signature. Their initialized-prefix, reserve and
mutation-preflight invariants follow the accepted
[collection contract](../specs/libraries.md#collections-algorithms-and-application-services).
For this candidate `List<T>` profile, `Reserve` never reduces capacity or
changes count; an already sufficient capacity succeeds without allocation.
Expected allocation refusal preserves elements, count and capacity. These
reservation guarantees remain proposed; constructor and allocation-failure
transport remain **OPEN**.

Accepted copied-value, structural-invalidation and slot-authority rules
are in the [collection contract](../specs/libraries.md#collections-algorithms-and-application-services).
These cursor candidates retain their source root/backing dependency and expose
one copied item through each true TryMoveNext, with vacant output on false.
Close releases its traversal obligations once under the selected standard
contract. Additional closed-use/reset APIs, concurrent access and exact
root/loan metadata remain **OPEN**.

The accepted write-guard exclusion and lifetime rules are in
[libraries](../specs/libraries.md#collections-algorithms-and-application-services)
and [lifetimes](../specs/lifetimes.md#three-independent-dependencies).
Synchronous cursor Close is the selected loop operation, not an implicit
no-throw cleanup registration. The separate guard Close candidate and exact
cursor/root and guard-loan metadata remain **OPEN**.

Accepted detached-node, membership, and matched-access behavior is specified
in the [managed collection contract](../specs/libraries.md#collections-algorithms-and-application-services).
Constructor/insertion signatures above remain proposed; their allocation
failure transport and exact metadata remain open.

The accepted eager callback, borrowing, and no-hidden-materialization
direction is in
[libraries](../specs/libraries.md#collections-algorithms-and-application-services).
The signatures below are candidate APIs, not executable generic exports or
an inferred SIMD contract:

```terrum
public interface IMapper<TSource, TResult>
{
    mut TResult Map(ref<TSource> value);
}
public interface IPredicate<T>
{
    mut bool Test(ref<T> value);
}
public interface IZipper<TLeft, TRight, TResult>
{
    mut TResult Zip(ref<TLeft> left, ref<TRight> right);
}
public interface IVisitor<T>
{
    mut void Visit(ref<T> value);
}
public static class SpanAlgorithms
{
    public static bool Contains<T, TComparer>(Span<T> source, ref<T> value,
        write ref<TComparer> comparer)
        where TComparer : IEqualityComparer<T>;
    public static bool TryIndexOf<T, TComparer>(Span<T> source, ref<T> value,
        write ref<TComparer> comparer, init ref<nuint> index)
        where TComparer : IEqualityComparer<T>;
    public static bool TryLastIndexOf<T, TComparer>(Span<T> source, ref<T> value,
        write ref<TComparer> comparer, init ref<nuint> index)
        where TComparer : IEqualityComparer<T>;
    public static bool SequenceEqual<T, TComparer>(Span<T> left, Span<T> right,
        write ref<TComparer> comparer)
        where TComparer : IEqualityComparer<T>;
    public static bool StartsWith<T, TComparer>(Span<T> source, Span<T> prefix,
        write ref<TComparer> comparer)
        where TComparer : IEqualityComparer<T>;
    public static bool EndsWith<T, TComparer>(Span<T> source, Span<T> suffix,
        write ref<TComparer> comparer)
        where TComparer : IEqualityComparer<T>;
    public static bool TryFind<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate, init ref<T> value)
        where T : [Copy]
        where TPredicate : IPredicate<T>;
    public static bool TryFindLast<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate, init ref<T> value)
        where T : [Copy]
        where TPredicate : IPredicate<T>;
    public static bool TryFindIndex<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate, init ref<nuint> index)
        where TPredicate : IPredicate<T>;
    public static bool TryFindLastIndex<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate, init ref<nuint> index)
        where TPredicate : IPredicate<T>;
    public static bool Any<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate)
        where TPredicate : IPredicate<T>;
    public static bool All<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate)
        where TPredicate : IPredicate<T>;
    public static nuint CountWhere<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate)
        where TPredicate : IPredicate<T>;
    public static void Select<TSource, TResult, TMapper>(Span<TSource> source,
        write Span<TResult> destination, write ref<TMapper> mapper)
        where TMapper : IMapper<TSource, TResult>;
    public static nuint Where<T, TPredicate>(Span<T> source,
        write Span<T> destination, write ref<TPredicate> predicate)
        where T : [Copy]
        where TPredicate : IPredicate<T>;
    public static void Zip<TLeft, TRight, TResult, TZipper>(Span<TLeft> left,
        Span<TRight> right, write Span<TResult> destination,
        write ref<TZipper> zipper)
        where TZipper : IZipper<TLeft, TRight, TResult>;
    public static void ForEach<T, TVisitor>(Span<T> source,
        write ref<TVisitor> visitor)
        where TVisitor : IVisitor<T>;
    public static void Fill<T>(write Span<T> destination, T value)
        where T : [Copy];
    public static void Reverse<T>(write Span<T> values)
        where T : [Copy];
    public static void CopyTo<T>(Span<T> source, write Span<T> destination)
        where T : [Copy];
    public static void MoveWithin<T>(write Span<T> values, nuint sourceStart,
        nuint destinationStart, nuint length)
        where T : [Copy];
}
```

`TryIndexOf` false means no index was published. Accepted `Select` and
`Where` destination, callback and overlap rules are in the
[collection contract](../specs/libraries.md#collections-algorithms-and-application-services);
the other listed operations remain subject to their candidate contracts
below. Stateful callbacks are mutable borrowed values. Callback failure may leave accepted prefix; no rollback or predicate replay. General lazy/GroupBy/Join surfaces are not inventoried; one bounded sort signature follows.

These additions are **PROPOSED** members in
`Terrum.Collections.Algorithms`, not executable generic exports. The
`mut` mapper/predicate requirements deliberately make callback-state writes
expressible through the supplied `write ref<TCallback>`; the readonly
requirements in the older design-only sketch cannot promise that behavior.
The equality witness remains readonly. Borrowing a callback mutably does not
permit it to retain element loans or invalidate the source/destination during
reentrant work. `IVisitor` observes elements through `ref<T>`; it does not
grant writable element access merely because its own state is mutable.

| Proposed member | Visit, result and storage contract |
|---|---|
| `Contains`, `TryIndexOf` | Visit from the beginning and stop at the first equality match; empty input returns false. A successful index names that first match. |
| `TryLastIndexOf` | Visit from the end and stop at the first equality match in that direction; success publishes its original source index. |
| `SequenceEqual` | Unequal lengths return false without witness calls. Otherwise compare aligned pairs in increasing index order, stopping at the first unequal pair; two empty inputs return true. |
| `StartsWith`, `EndsWith` | A longer prefix/suffix returns false without witness calls. Compare the matching source range against the supplied prefix/suffix in increasing index order, stopping at the first unequal pair; an empty prefix/suffix matches any source. |
| `TryFind` | Visit from the beginning and copy the first matching element into the vacant output; no match leaves it vacant. An optional/null element can be a successful result. |
| `TryFindLast` | Visit from the end and copy the first matching element in that direction; no match leaves the output vacant. No forward scan or predicate replay is implied. |
| `TryFindIndex`, `TryFindLastIndex` | Search in increasing/decreasing source-index order, respectively, stopping at the first true predicate in that direction. Success publishes the original source index; no match leaves it vacant. No `[Copy]` element capability is required. |
| `Any` | Stop at the first true predicate; empty input returns false without invoking it. |
| `All` | Stop at the first false predicate; empty input returns true without invoking it. |
| `CountWhere` | Visit every element once in increasing source-index order and count true results. Empty input returns zero. The result is at most the span length and fits `nuint` without widening or wrapping. |
| `Zip` | Require equal input lengths and sufficient destination capacity before callbacks. Inputs may overlap each other, but neither may overlap the written destination prefix. Visit paired elements once in increasing index order; no shorter-input truncation or hidden padding. |
| `ForEach` | Visit each element once in increasing index order; callback failure stops further visits. No deferred execution or materialized result. |
| `Fill`, `Reverse` | Fill every initialized destination slot with a copy, or reverse the initialized span in place. Empty spans are valid; neither operation constructs vacant capacity. |
| `CopyTo` | Preflight sufficient destination length and disjoint source/written-destination ranges, then copy the whole source into the destination prefix. A longer destination suffix is untouched. |
| `MoveWithin` | Check both complete ranges without index overflow before writes. Overlap is permitted with original-source snapshot semantics, without allocating a snapshot. The name denotes range relocation by copying, not transfer of an affine owner or vacancy of the old range. |

`Select` and `Where` follow the accepted
[collection contract](../specs/libraries.md#collections-algorithms-and-application-services).
`Zip` writes exactly the common input-length prefix; a longer destination
suffix remains unchanged. All span destinations refer to existing initialized slots;
replacement must obey the element's cleanup, rooting and write-barrier
requirements. None of these operations treats arbitrary bytes as valid
`T` or substitutes raw memory copying for checked typed access. `[Copy]`
is required wherever one existing element is duplicated; mapper/zipper
results instead transfer each separately produced result into its destination.
Output publication/effect metadata and callback-loan enforcement remain
**OPEN**. No callback is replayed after failure, and these signatures do not
promise rollback, NoThrow or a hidden allocation-free callback.

The predicate-only searches, quantifiers and count operation borrow elements
without copying or retaining them. Copied-value searches add `[Copy]` because
they publish an existing element, not because predicate execution itself
requires it. A stateful predicate can distinguish forward and reverse visits;
implementations must preserve the stated direction, invoke it at most once
per visited element and stop immediately on the specified result or failure.
They cannot replace a reverse search with a forward scan that remembers the
last match. A predicate exception is not a no-match result: it preserves the
existing checked failure behavior and leaves an initialization output vacant,
without undoing earlier callback-state writes. This does not grant element
mutation, reentrant source invalidation or escaping loans.

Sequence comparison is element-wise under the supplied witness, not text
normalization, ordering, key equivalence or raw representation comparison.
For prefix/suffix comparisons the source element is the left witness operand,
and the prefix/suffix element is the right operand. Readonly input ranges may
overlap, including identical ranges, but identical backing does not permit
skipping witness calls: a selected floating equality may reject a NaN compared
with itself, and callbacks retain their declared effects. These operations
need neither `[Copy]` nor hidden materialization; they cannot retain the
element loans or reinterpret padding as part of equality.

### Sequence and eager-operation inventory

The `SpanAlgorithms` declarations above supply concrete search, selection,
pairing, visitation and storage-operation signatures. The following
**proposed** matrix indexes them and fills out the protocol family
without repeating a concrete declaration for every element type. `Span<T>`
means a non-owning readonly input; a `write` destination must be explicit.
All algorithms check bounds, destination length and prohibited overlap before
their first callback or write; callback failures are not transactional unless
an operation says otherwise.

| Protocol / type | Proposed members and shape | Outcome / ownership |
|---|---|---|
| `ISequence<T, TCursor>` | Selected readonly `GetEnumerator() -> TCursor`; exact ICursor<T> relationship | Factory source; no implicit source consumption or universal re-enumeration |
| `ICursor<T>` | Selected `TryMoveNext(init ref<T> item) -> bool` and mandatory `Close()` | True/live, false/vacant; direct sources preserve ordinary copy/transfer/backing, without a universal Copy bound or Current |
| `IAsyncSequence<T, TCursor>` / `IAsyncCursor<T>` | Selected synchronous `GetAsyncEnumerator() -> TCursor`; `TryMoveNextAsync(init ref<T>) -> Task<bool>`; required `CloseAsync() -> Task<void>` | Completion/output reservation and saved Boolean relationship; no universal Copy bound or Current; lending remains separate |
| `ILendingSequence<T, TCursor>` / `ILendingCursor<T>` and writable counterparts | Selected `GetEnumerator() -> TCursor`, readonly/mut respectively; `TryMoveNext(init ref<ref<T>>)` or `init ref<write ref<T>>`; mandatory Close | Distinct reference-output roles; element-place aliases, cursor-bound loans and checked published backing refinements |
| `IAsyncLendingSequence<T, TCursor>` / `IAsyncLendingCursor<T>` and writable counterparts | Selected synchronous readonly/mut GetAsyncEnumerator; reference-output TryMoveNextAsync and awaited CloseAsync | Same element dependencies plus explicit descriptor reservation and completion publication; no implicit Task<TCursor> factory |
| `SpanAlgorithms` search and predicates | `Contains`, `TryIndexOf`, `TryLastIndexOf`, `TryFind`, `TryFindLast`, `TryFindIndex`, `TryFindLastIndex`, `All`, `CountWhere`, `Any` | Explicit equality witness or predicate; directional visits and vacant no-match outputs, no unknown-index sentinel |
| `SpanAlgorithms` sequence comparison | `SequenceEqual`, `StartsWith`, `EndsWith` | Explicit element-equality witness; readonly overlap allowed, no identical-backing shortcut that skips effects or NaN comparisons |
| `SpanAlgorithms` transform | `Select`, `Where`, `Zip`, `ForEach`, `Fill`, `Reverse`, `CopyTo`, `MoveWithin` | Mutable callbacks take `write ref<TCallback>`; destination/overlap contracts differ by operation |
| `OrderedSpanAlgorithms` aggregate | `Fold`, `TryMin`, `TryMax`; prospective `Sum` | Explicit seed or ordering witness; empty extrema leave output vacant, equal extrema choose the first source element |
| `OrderedSpanAlgorithms` order | `LowerBound`, `UpperBound`, `TryBinarySearch`, `Sort` | Explicit total-order witness; logarithmic sorted bounds, first-equal search and possibly partial permutation on sort failure |
| `List<T>` / managed | Constructors, `Count`, `Capacity`, index, `TryGet`, `Add`, `Insert`, `Reserve`, `RemoveAt`, `Clear`, write guard and concrete cursor | Initial `[ManagedStorable],[Copy],[Sized]` profile; accepted storage/guard rules: [libraries](../specs/libraries.md#collections-algorithms-and-application-services) |
| `LinkedList<T>` / managed | `First`, `Last`, `AddFirst/Last`, `AddBefore/After`, `Remove`, `Clear`, cursor | Accepted node identity/ownership rules: [libraries](../specs/libraries.md#collections-algorithms-and-application-services) |
| `Queue<T>`, `Stack<T>` / managed, **new** | Empty/capacity constructors, `Count`, `Capacity`, `Reserve`, `Enqueue/Push`, `TryDequeue/TryPop(init ref<T>)`, `TryPeek(init ref<T>)`, `Clear`, copied-value cursor and `Close` | Initial same managed Copy bounds; false output vacant; no `default(T)` |
| `SortedSet<T,TOrder>` / managed, **new** | Explicit-order constructors, `Count`, `Capacity`, `Order`, `Reserve`, `Contains`, `TryAdd`, `Remove`, `Clear`, ascending cursor and `Close` | Explicit total-order witness; comparison-zero means duplicate |
| `Deque<T>` / owned, **new** | Explicit-provider construction, count/capacity/index/peek, front/back push/pop, reservation, `GetSegments`, exclusive write guard, clear/disposal | Concrete Copy-profile declarations in §4; segment loans forbid structural mutation and never fabricate a contiguous span |
| `Dictionary<TKey,TValue,TComparer>`, `HashSet<T,TComparer>` | Explicit-comparer constructors, `Count`, `Capacity`, comparer, `Reserve`, `TryGet`/`ContainsKey`/`Contains`, `TryAdd`, `Remove`, `Clear`, guarded cursor | Explicit key comparer; complete sample and failure contracts in §13 |

#### Sequence and cursor protocol contracts

**SELECTED CORE CONTRACT:** the [value-iteration chapter](../specs/language/iteration.md)
owns Terrum.ICursor<T> and Terrum.ISequence<T, TCursor>.
The standard synchronous operation combines advance and item publication;
there is no separate Current state or universal item-Copy requirement.
Copying from a borrowed collection and transfer from an owning cursor retain
their actual published authority, output and backing contracts.

```terrum
public interface ICursor<T>
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
}
public interface ISequence<T, TCursor>
    where TCursor : ICursor<T>
{
    public TCursor GetEnumerator();
}
```

Both factory sequences and already-created direct cursors are valid sources.
A named affine direct cursor requires move; adoption does not acquire/reset
it, and required close does not dispose the source collection.
Distinct eligible source relationships are ambiguous, not a name/body-based
factory preference. An explicit nominal adapter can expose the intended role.
These shapes do not select a physical frame/witness/output codec.

**SELECTED GENERATION CONTRACT:** [generated producers](../specs/language/generated-producers.md)
use owned named IteratorCursor shells and one same-module Iterator callable.
The standard result is the direct affine cursor, not a sequence factory.
Source creation clauses/effects are separate from generated advance/close
contracts; frame/capture and required close remain checked.

The following concrete range and producer signatures remain **PROPOSED
INVENTORY FRAGMENTS**. The marked creator needs a real producer block; its
bodyless signature here is not an executable declaration.
Its int32 items are Copy values, not a constraint on all cursors.
Producer creation and explicit factory acquisition do not run the body:

```terrum
[IteratorCursor]
public mut unique struct Int32RangeCursor : ICursor<int32>
{
}
public struct Int32RangeSequence : ISequence<int32, Int32RangeCursor>
{
    public Int32RangeCursor GetEnumerator();
}
[Iterator]
public static Int32RangeCursor Range(int32 start, nuint count);
```

**SELECTED CORE CONTRACT:** the [async-value-iteration chapter](../specs/language/async-iteration.md)
owns the separate async roles. Acquisition is synchronous and readonly;
advance and close have checked completion contracts. TryMoveNextAsync reserves
the exact item destination and cursor access until terminal completion/cleanup
is observed. Its awaited Boolean supplies true/live or false/failure/vacant,
not a relation inferred from Task<bool> alone. No universal Copy item bound
or Current property is part of the standard protocol:

```terrum
public interface IAsyncCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(init ref<T> item);
    public mut Task<void> CloseAsync();
}
public interface IAsyncSequence<T, TCursor>
    where TCursor : IAsyncCursor<T>
{
    public TCursor GetAsyncEnumerator();
}
```

The following concrete async range and producer signatures remain **PROPOSED
INVENTORY FRAGMENTS**, likewise requiring the marked producer's block body:

```terrum
[IteratorCursor]
public mut unique struct Int32AsyncCursor : IAsyncCursor<int32>
{
}
public struct Int32AsyncSequence : IAsyncSequence<int32, Int32AsyncCursor>
{
    public Int32AsyncCursor GetAsyncEnumerator();
}
[Iterator]
public static async Int32AsyncCursor ValuesAsync(int32 start, nuint count);
```

`Range` and `ValuesAsync` are reference signatures for producer result
contracts, not executable bodyless concrete members. Their implementations
would use `yield expression;` and producer `return;`; the sync body starts on
its first advancement, and the async body likewise starts on its first
asynchronous advancement. `ValuesAsync` synchronously returns the proposed
named async cursor, **not** `Task<Int32AsyncCursor>` or an async-sequence
factory: its body is lazy, unlike ordinary eager async invocation.
Int32RangeSequence/Int32AsyncSequence are separate explicit factory-wrapper
inventory candidates and must satisfy actual recreation/capture contracts.
Synchronous cursor acquisition
does not start that producer body or introduce an acquisition task.
Sequence state, cursor backing and physical representation are intentionally
omitted; these fragments do not promise a storage strategy, legal affine
capture duplication or an implemented concrete producer body.

Both selected output operations preserve complete item dependencies.
Synchronous Close and async CloseAsync are the checked loop operations;
async close is awaited after pending advancement and item retirement.
Neither is automatically a `[NoThrow] [Cleanup]` destructor.
The accepted close-on-exit and awaited-close behavior is specified by the
[iteration grammar](../specs/grammar-spec.md#33-statements-and-local-declarations)
and [producer contract](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization).
Async loans and completion responsibility follow [language direction](../specs/language.md#storage-concurrency-entry-and-boundaries)
and [lifetimes](../specs/lifetimes.md#declaration-defaults-exceptions-and-published-compatibility).
Exact task/cursor/frame metadata remain open.

**SELECTED CORE CONTRACT:** [reference-output lending](../specs/language/lending-iteration.md)
owns distinct readonly/writable synchronous/asynchronous source and cursor
roles. These are not value iteration instantiated with a reference-valued
item. The loop name aliases the existing element T place; descriptor retirement
ends its loan rather than destroying T.

Readonly factories have readonly receivers. Writable factories require mut
receivers and actual writable element backing; descriptor mutability alone
does not grant it. Async acquisition remains synchronous:

```terrum
public interface ILendingCursor<T>
{
    public mut bool TryMoveNext(init ref<ref<T>> item);
    public mut void Close();
}
public interface ILendingSequence<T, TCursor>
    where TCursor : ILendingCursor<T>
{
    public TCursor GetEnumerator();
}
public interface IWritableLendingCursor<T>
{
    public mut bool TryMoveNext(init ref<write ref<T>> item);
    public mut void Close();
}
public interface IWritableLendingSequence<T, TCursor>
    where TCursor : IWritableLendingCursor<T>
{
    public mut TCursor GetEnumerator();
}
public interface IAsyncLendingCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(init ref<ref<T>> item);
    public mut Task<void> CloseAsync();
}
public interface IAsyncLendingSequence<T, TCursor>
    where TCursor : IAsyncLendingCursor<T>
{
    public TCursor GetAsyncEnumerator();
}
public interface IAsyncWritableLendingCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(init ref<write ref<T>> item);
    public mut Task<void> CloseAsync();
}
public interface IAsyncWritableLendingSequence<T, TCursor>
    where TCursor : IAsyncWritableLendingCursor<T>
{
    public mut TCursor GetAsyncEnumerator();
}
```

Each advance selects the initialized inner reference value of its exact init
formal and publishes true/live, false/vacant or failure/vacant after required
checks/cleanup. Async output additionally reserves its destination/cursor
access with the operation until checked completion observation.
The ordinary Boolean result or Task<bool> shape does not supply the element
origin, access, backing or phase relationship.

Loans have a conservative cursor-bound default. Checked published refinements
can permit survival across specified later advance/close operations when their
actual source/root/guard dependencies survive; generic callers need an exposed
refinement and proven condition. Private bodies, equal addresses, rooting or
Copy cannot narrow the public bound. Neither surviving validity nor write
authority is an exclusive loan, stable contents or NoAlias.
Exact logical output/source/refinement fields are selected by the chapter;
the [general dependency schemas](../specs/language/dependencies-and-views.md)
now supply source/target authoring, while physical codecs remain separate.
ForwardsBacking alone does not establish advance/close survival.
No Current, return annotation substituting for an init output, Item<'a>, MatchedAccess registration
or automatic Span/List lending witness is part of these contracts.

The following associated-cursor form remains a **BLOCKED CANDIDATE**:

```terrum
public interface ISequence<T>
{
    public type Cursor;
    public Cursor GetEnumerator();
}
```

`Cursor` uses the accepted associated-type declaration form, but that source's
binding and selection contract is not adopted here
([grammar](../specs/grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding)).
Accepted whole-source result/capture dependencies and the distinction between
receiver storage and forwarded backing are in the
[grammar contract](../specs/grammar-spec.md#result-dependency-declarations).
Return-targeted DependsOn does not select an init output or declare receiver/
suspension capture. Accepted producer phases and suspension dependencies are in the
[grammar coverage index](../specs/grammar-spec.md#6-source-form-coverage-and-open-boundaries)
and [lifetime contract](../specs/lifetimes.md#three-independent-dependencies).
The [named producer schema](../specs/language/generated-producers.md) separately
selects generated cursor/capture/operation phases. Physical frame/phase and
dependency representations/codecs remain open.

The output operations are the standard by-value ICursor and IAsyncCursor
requirements, not separate consuming protocols. They do not permit moving
stored elements out of an ordinary borrowed collection.
Async output publishes through its explicit completion relationship, not when
returning the task. Ordinary Task output defaults remain unchanged.
Lending/writable bindings use their separate selected reference-output roles;
generated producer registration is its separate selected contract. The grammar records that
boundary in its
[coverage index](../specs/grammar-spec.md#6-source-form-coverage-and-open-boundaries).

### Proposed ordered and sequential collection declarations

These signatures expand the matrix for one managed `[Copy]` storage profile.
They do not choose a generic associated-cursor contract or a universal
sequence witness. A concrete cursor returns copied values; its loan on the
collection and traversal invalidation are part of the proposed contract.
`IOrderComparer`, `IFolder` and `OrderedSpanAlgorithms` belong to proposed
`Terrum.Collections.Algorithms`; the collection and cursor types belong to
proposed `Terrum.Collections`.

```terrum
public interface IOrderComparer<T>
{
    int32 Compare(ref<T> left, ref<T> right);
}
public interface IFolder<T, TState>
{
    mut TState Fold(TState state, ref<T> value);
}
public static class OrderedSpanAlgorithms
{
    public static nuint LowerBound<T, TOrder>(Span<T> source, ref<T> value,
        ref<TOrder> order)
        where TOrder : IOrderComparer<T>;
    public static nuint UpperBound<T, TOrder>(Span<T> source, ref<T> value,
        ref<TOrder> order)
        where TOrder : IOrderComparer<T>;
    public static bool TryBinarySearch<T, TOrder>(Span<T> source, ref<T> value,
        ref<TOrder> order, init ref<nuint> index)
        where TOrder : IOrderComparer<T>;
    public static void Sort<T, TOrder>(write Span<T> values, ref<TOrder> order)
        where T : [Copy]
        where TOrder : IOrderComparer<T>;
    public static bool TryMin<T, TOrder>(Span<T> source, ref<TOrder> order,
        init ref<T> value)
        where T : [Copy]
        where TOrder : IOrderComparer<T>;
    public static bool TryMax<T, TOrder>(Span<T> source, ref<TOrder> order,
        init ref<T> value)
        where T : [Copy]
        where TOrder : IOrderComparer<T>;
    public static TState Fold<T, TState, TFolder>(Span<T> source,
        TState seed, write ref<TFolder> folder)
        where TFolder : IFolder<T, TState>;
}
public mut class SortedSet<T, TOrder> : ISequence<T, SortedSetCursor<T, TOrder>>
    where T : [ManagedStorable], [Copy], [Sized]
    where TOrder : IOrderComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public SortedSet(TOrder order);
    public SortedSet(TOrder order, nuint capacity);
    public nuint Count { get; }
    public nuint Capacity { get; }
    public TOrder Order { get; }
    public bool Contains(T value);
    public mut Result<void, Error> Reserve(nuint capacity);
    public mut Result<bool, Error> TryAdd(T value);
    public mut bool Remove(T value);
    public mut void Clear();
    public SortedSetCursor<T, TOrder> GetEnumerator();
}
public mut unique struct SortedSetCursor<T, TOrder> : ICursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
    where TOrder : IOrderComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
}
public mut class Queue<T> : ISequence<T, QueueCursor<T>>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public Queue();
    public Queue(nuint capacity);
    public nuint Count { get; }
    public nuint Capacity { get; }
    public mut Result<void, Error> Reserve(nuint capacity);
    public mut Result<void, Error> Enqueue(T value);
    public mut bool TryDequeue(init ref<T> value);
    public bool TryPeek(init ref<T> value);
    public mut void Clear();
    public QueueCursor<T> GetEnumerator();
}
public mut unique struct QueueCursor<T> : ICursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
}
public mut class Stack<T> : ISequence<T, StackCursor<T>>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public Stack();
    public Stack(nuint capacity);
    public nuint Count { get; }
    public nuint Capacity { get; }
    public mut Result<void, Error> Reserve(nuint capacity);
    public mut Result<void, Error> Push(T value);
    public mut bool TryPop(init ref<T> value);
    public bool TryPeek(init ref<T> value);
    public mut void Clear();
    public StackCursor<T> GetEnumerator();
}
public mut unique struct StackCursor<T> : ICursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
}
```

`Compare` is a readonly witness: negative/zero/positive determine order and
equality; it must remain consistent while values are stored or searched.
Predicate searches, `Any` and `CountWhere` belong to `SpanAlgorithms`
above; they require no ordering witness and are not duplicated in this
ordered-operation service.
`TryBinarySearch` requires sorted input, chooses the first equal index, and
leaves `index` vacant when absent. `LowerBound` and `UpperBound` expose
insertion boundaries under that same ordering witness; their concrete
contracts and copied extrema are below. `Sort` may leave a valid permutation if
comparison fails; it never manufactures or duplicates an affine owner.
`Fold` consumes the explicit seed and returns its final state, including for
an empty source. `SortedSet` iterates ascending, treats comparison-zero as
duplicate, and publishes no insertion on allocation failure. `Queue` traverses
oldest first, `Stack` newest first. Their insertion failures preserve contents;
an empty `TryDequeue`, `TryPop` or `TryPeek` leaves the output vacant. Capacity
constructors have the same unresolved checked construction-failure transport
as the keyed collections in §13. A cursor may not be used across structural
mutation; it never outlives its source. TryMoveNext copies one permitted
item into its vacant destination on true and leaves it vacant on false.
The [standard cursor contract](../specs/language/iteration.md) supplies required
close and logical publication, not concrete guard/frame or physical metadata.
Those representations remain **OPEN**; bodyless signatures do not implement them.

The four bound/extremum members are **PROPOSED** additions to the existing
eager Core service, not new language intrinsics or executable exports.
They use the explicitly selected readonly order witness; no default
comparison, equality witness or implicit total order is synthesized.

| Proposed member | Search, visit and result contract |
|---|---|
| `LowerBound` | For input sorted in ascending witness order, return the first index whose element compares greater than or equal to `value`, or `source.Length` if none. Empty input returns zero without comparison. |
| `UpperBound` | For the same sorted-input contract, return the first index whose element compares strictly greater than `value`, or `source.Length` if none. Empty input returns zero without comparison. |
| `TryBinarySearch` | Return the first comparison-zero index, not an arbitrary duplicate. False leaves `index` vacant; it does not encode an insertion position or bitwise-complement sentinel in the output. |
| `TryMin`, `TryMax` | Empty input returns false without comparison or publication. Otherwise seed the candidate from the first element, visit later elements in increasing source-index order and compare each once against the current candidate. Replace only for strictly smaller/larger order, retaining the first source element among equal extrema; true publishes a copy only after the scan succeeds. |

Both bounds return a position from zero through `source.Length`, inclusive.
That terminal position is not an element index and must not be dereferenced.
Comparisons use the source element as the left operand and the requested
value as the right operand. The equal-key range begins at `LowerBound` and
ends just before `UpperBound`; the boundaries coincide if no key compares
zero. Comparison-zero defines equivalence for these operations, not ordinary
`==`, bit identity or a separately selected key comparer.

Bounds and `TryBinarySearch` use logarithmically many comparisons and constant
auxiliary storage without allocation, element copies or a hidden full sortedness
scan. Sortedness under the same stable total-order witness is a caller
precondition, not a runtime-validated property. An unsorted range has no
search-result guarantee; normal checked typed access and index bounds still
apply. A false result is not evidence that the input was sorted.
Checked index arithmetic must remain valid even at the maximum representable
span length; no operation requires forming `source.Length + 1`. Exact binary
probe order is not a public contract. Comparer failure stops the operation,
propagates as a checked failure and leaves any initialization output vacant;
no retry, rollback or fabricated boundary is implied.

Extrema need no sorted input. Their witness call compares the visited element
on the left with the retained candidate on the right; a singleton publishes
its sole copied element without comparison. `[Copy]` is required for that
publication, not for binary search or insertion bounds. An optional/null
element can be a successful result; absence of an
extremum means an empty input, not a default or sentinel `T`. Failed comparison
leaves the output vacant without undoing earlier witness effects.

All of these operations borrow existing initialized elements and may not
retain their loans or permit reentrant source invalidation. Readonly comparer
requirements do not establish purity, NoThrow or a legal order for ordinary
IEEE NaN comparisons; callers need a witness with a consistent explicit
NaN/zero policy when those values are possible. Precise conditional-publication
metadata, witness-effect checking and comparator failure transport remain
**OPEN**, as for the other algorithm proposals.

The constructor, capacity/reservation and explicit-close signatures remain
**PROPOSED** additions for these three managed Copy-profile shapes. Empty
queue/stack construction needs no order witness; `SortedSet` still requires
one explicitly.

The accepted collection contract specifies mutation preflight, but not these
new types' reservation guarantees. For this proposed queue/stack/`SortedSet`
profile, capacity construction reserves at least the requested capacity
without constructing elements or `default(T)`, and `Capacity >= Count`.
`Reserve` never reduces capacity or changes contents, count or traversal
order; sufficient capacity returns `Success(())` without allocation.
Expected allocation failure returns `Failure(Error)` and preserves the prior
collection. Capacity/count arithmetic and conflicting-loan checks precede
storage changes. These guarantees are proposals, not accepted collection
semantics; exact failure transport remains open.

Accepted copied-value, structural-invalidation and lazy-producer rules are in
[libraries](../specs/libraries.md#collections-algorithms-and-application-services)
and the [iteration grammar](../specs/grammar-spec.md#33-statements-and-local-declarations).
These concrete cursor candidates retain their source root, publish no item
on false exhaustion and implement the selected TryMoveNext/Close roles.
Closing leaves the collection intact. They do not add associated-cursor
selection, implicit NoThrow cleanup or concurrent safety; extra closed-use/
reset APIs and exact metadata remain **OPEN**.

The accepted async advancement, suspension-dependency, and
structured/awaited-close rules are in the [producer contract](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization)
and [lifetimes](../specs/lifetimes.md#three-independent-dependencies);
they do not approve associated-cursor selection, lending annotations or
producer/frame representation. Those remain **OPEN** despite the selected
by-value async protocol. A deterministic snapshot requires an
explicit allocated result and policy. Collection-expression registration is
checked per builder, not inferred from `TryAdd` spelling.

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

The accepted parameter and overload rules are specified in
[language](../specs/language.md#callable-values-params-and-tuples) and the
[source grammar](../specs/grammar-spec.md#52-exact-overload-grammar-ranking-and-disambiguation).
The signatures/examples retain the proposed `List<T>` call shape; exact
diagnostics, generic collection-expression inference, explicit spread syntax,
and serialized binding remain work.

## 6. Numerics and conversions

The accepted literal, arithmetic, and conversion rules are specified in
[language](../specs/language.md#primitives-strict-arithmetic-and-text) and
[conversion policies](../specs/conversions.md#conversion-policies). The
following **PROPOSED** reference signatures extend the design-only
[numeric sketch](../../library/numerics.api.terrum), not implemented
primitive exports or a required universal number root. Core's privileged
primitive declarations attach members but cannot author source-defined
primitive operators; operator requirements below describe potential nominal
witnesses for intrinsic operations, not additional primitive operator bodies.

### Independently selectable operation protocols

The accepted [numeric capability groups](../specs/language/numeric-capabilities.md)
now own the exact same-type Core groups, constraints and primitive witnesses.
Min/Max/Clamp form one selection group. The heterogeneous/per-method and
classification declarations below remain broader proposed alternatives,
not additional adopted Core definitions or prerequisites.

These are independently witnessed. A different `TOther` or `TResult` does
not imply automatic promotion, and conformance to one protocol never grants
another. The optional first `<this TSelf>` binder is an approved interface
source form. The bodyless interface requirements use ordinary grammar;
the later concrete member `;` is reference-signature notation only.

```terrum
public interface ISubtractionOperators<this TSelf, TOther, TResult>
{
    static TResult operator -(TSelf left, TOther right);
}
public interface IDivisionOperators<this TSelf, TOther, TResult>
{
    static TResult operator /(TSelf left, TOther right);
}
public interface IRemainderOperators<this TSelf, TOther, TResult>
{
    static TResult operator %(TSelf left, TOther right);
}
public interface IUnaryPlusOperators<this TSelf, TResult>
{
    static TResult operator +(TSelf value);
}
public interface IUnaryNegationOperators<this TSelf, TResult>
{
    static TResult operator -(TSelf value);
}
public interface IEqualityOperators<this TSelf, TOther>
{
    static bool operator ==(TSelf left, TOther right);
    static bool operator !=(TSelf left, TOther right);
}
public interface IComparisonOperators<this TSelf, TOther>
{
    static bool operator <(TSelf left, TOther right);
    static bool operator <=(TSelf left, TOther right);
    static bool operator >(TSelf left, TOther right);
    static bool operator >=(TSelf left, TOther right);
}
public interface IBitwiseOperators<this TSelf>
{
    static TSelf operator &(TSelf left, TSelf right);
    static TSelf operator |(TSelf left, TSelf right);
    static TSelf operator ^(TSelf left, TSelf right);
    static TSelf operator ~(TSelf value);
}
public interface IShiftOperators<this TSelf, TCount>
{
    static TSelf operator <<(TSelf value, TCount count);
    static TSelf operator >>(TSelf value, TCount count);
    static TSelf operator >>>(TSelf value, TCount count);
}
public interface IMultiplicativeIdentity<this TSelf, TResult>
{
    static TResult One { get; }
}
public interface IIntegerRelations<this TSelf>
{
    type Signed;
    type Unsigned;
    type Wider;
}
public interface IFloatingClassification<this TSelf>
{
    static bool IsNaN(TSelf value);
    static bool IsFinite(TSelf value);
    static bool IsInfinity(TSelf value);
    static bool IsNormal(TSelf value);
    static bool IsSubnormal(TSelf value);
}
```

`IAdditionOperators`, `IMultiplyOperators`, `IAdditiveIdentity` and the
optional scalar composites in the original sketch below complete candidate
arithmetic/identity shapes; no composite here implies unlisted protocols.
The accepted independent operation and built-in operator contracts are in
[libraries](../specs/libraries.md#scalar-mathematics-keys-and-conversion)
and [language](../specs/language.md#primitives-strict-arithmetic-and-text).
**OPEN:** the additional heterogeneous/result/classification protocols and
their nominal implementation contracts. The selected same-type groups and
all integer data/count witnesses are no longer open.
`IIntegerRelations` is an optional relation proposal; the `uint32` witness
below uses the approved associated declaration form. Its particular
relationships, eligible widths, diagnostics and metadata remain **OPEN**;
accepted associated-type binding and identity rules are in the
[grammar](../specs/grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding)
and [type-family contract](../specs/type-families.md#nominal-witnesses-and-associated-relationships).

### Existing sampled declarations

These are unadopted earlier sampled alternatives, including the three-member
IScalarNumber candidate. A same-spelled proposed interface does not redefine
the accepted Core group or its complete required witness.

The numerics source is representative, not a complete primitive matrix.
The candidate `IScalarIeee754` here composes the new independent
`IFloatingClassification` requirement; the source sketch currently declares
only `IsNaN` and `IsFinite` directly, so this is an expansion, not a claim of
an existing checked conformance:

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
public interface IScalarIeee754<TSelf>
    : IScalarNumber<TSelf>, IFloatingClassification<TSelf>
    where TSelf : IScalarIeee754<TSelf>
{
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
    public static int32 One { get; }
    public static int32 MinValue { get; }
    public static int32 MaxValue { get; }
    public static bool TryParse(TextSpan text, init ref<int32> value);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
    public static bool TryAdd(int32 left, int32 right, init ref<int32> result);
    public static bool TrySubtract(int32 left, int32 right, init ref<int32> result);
    public static bool TryMultiply(int32 left, int32 right, init ref<int32> result);
    public static int32 AddWrapping(int32 left, int32 right);
    public static int32 FromBits(uint32 bits);
    public static uint32 ToBits(int32 value);
}
public struct float32 : IParsable<float32>, IFormattable, IScalarIeee754<float32>
{
    public static float32 Zero { get; }
    public static float32 One { get; }
    public static float32 PositiveInfinity { get; }
    public static float32 NegativeInfinity { get; }
    public static float32 NaN { get; }
    public static bool IsNaN(float32 value);
    public static bool IsFinite(float32 value);
    public static bool IsInfinity(float32 value);
    public static bool IsNormal(float32 value);
    public static bool IsSubnormal(float32 value);
    public static bool IsNegative(float32 value);
    public static float32 FusedMultiplyAdd(float32 left, float32 right, float32 addend);
    public static float32 CopySign(float32 value, float32 sign);
    public static float32 NextUp(float32 value);
    public static float32 NextDown(float32 value);
    public static float32 FromBits(uint32 bits);
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
    public static decimal32 One { get; }
    public static decimal32 PositiveInfinity { get; }
    public static decimal32 NegativeInfinity { get; }
    public static decimal32 NaN { get; }
    public static bool IsNaN(decimal32 value);
    public static bool IsFinite(decimal32 value);
    public static bool IsInfinity(decimal32 value);
    public static bool IsNormal(decimal32 value);
    public static bool IsSubnormal(decimal32 value);
    public static decimal32 FusedMultiplyAdd(decimal32 left, decimal32 right, decimal32 addend);
    public bool KeyEquals(decimal32 other);
    public int32 GetKeyHashCode();
    public static bool TryParse(TextSpan text, init ref<decimal32> value);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
```

`IScalarNumber` and `IScalarIeee754` are optional candidate composites;
`TOther`/`TResult` may differ from `TSelf`. Their proposed composition does
not make vectors scalar numbers. Exact numeric/conversion witness
relationships and their complete checking/metadata remain open; associated
forms and identity checking follow the canonical
[type-family rules](../specs/type-families.md#nominal-witnesses-and-associated-relationships).

`TryAdd`/`TrySubtract`/`TryMultiply` and `FromBits`/`ToBits` remain
candidate members; their output and representation contracts follow
[language contracts](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate)
and [conversion policies](../specs/conversions.md#conversion-policies).
Floating APIs are target-specific; exact source/destination witness lookup
and user-defined declarations remain open.

### Candidate primitive member families

This is a *generative* inventory, not a duplicate declaration for every
width. Each eligible concrete width may have its own same-type arithmetic,
unary, ordinary equality/comparison and identity witnesses from above,
`Zero`/`One`/range members, parse/format members and named checked/wrapping
helpers as applicable. The row does not declare that witness, representation
or ABI; the shaped concrete examples below illustrate substitutions. These
are the [proposed language spellings](../specs/language.md#primitives-strict-arithmetic-and-text),
not a compiler-required-type list or presently implemented matrix.

| Candidate width family | Per-type application of shaped declarations |
|---|---|
| Signed `int8/16/32/64/128` | Closed same-width `+`, `-`, `*`, `/`, `%`, unary `+`/`-`, equality, ordered comparison, bitwise and eligible shifts. Checked negation of MinValue fails; wrapping requires explicit selection. |
| Unsigned `uint8/16/32/64/128` | Closed same-width arithmetic, equality, ordered comparison, bitwise and shifts; checked subtraction can underflow. No same-type unary negation is inferred from signed types. |
| Target-size `nint`/`nuint` | Corresponding signed/unsigned shapes but target-dependent limits; no invented fixed serialized width. |
| Binary `float16/32/64/128`, `bfloat16` | Same-format arithmetic, ordinary IEEE comparisons, classification, target-specific functions and numeric-key policy; no integer bitwise/wrapping witness implied. |
| Decimal `decimal32/64/128` | Same-format arithmetic, ordinary numeric comparison, decimal classification, parse/format and cohort-normalized key policy; no binary layout or CLR decimal semantics. |

The `int32`, `float32`, `decimal32` and `uint32` samples establish
only the members they actually show. This extended concrete pair illustrates
the proposed common unsigned shape and a narrow signed substitution; other
widths require their own checked members, not textual ABI substitution.

```terrum
public struct uint32
    : IParsable<uint32>, IFormattable, IScalarNumber<uint32>,
      IIntegerRelations<uint32>
{
    type Signed = int32;
    type Unsigned = uint32;
    type Wider = uint64;
    public static uint32 Zero { get; }
    public static uint32 One { get; }
    public static uint32 MinValue { get; }
    public static uint32 MaxValue { get; }
    [Literal(LiteralKind.Integer)] [ConstEval]
    public static bool TryParse(TextSpan text, init ref<uint32> value);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
    public static bool TryAdd(uint32 left, uint32 right, init ref<uint32> result);
    public static bool TrySubtract(uint32 left, uint32 right, init ref<uint32> result);
    public static bool TryMultiply(uint32 left, uint32 right, init ref<uint32> result);
    public static uint32 AddWrapping(uint32 left, uint32 right);
    public static uint32 FromBits(uint32 bits);
    public static uint32 ToBits(uint32 value);
}
public struct int8 : IParsable<int8>, IFormattable
{
    public static int8 Zero { get; }
    public static int8 One { get; }
    public static int8 MinValue { get; }
    public static int8 MaxValue { get; }
    [Literal(LiteralKind.Integer)] [ConstEval]
    public static bool TryParse(TextSpan text, init ref<int8> value);
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
    public static bool TryAdd(int8 left, int8 right, init ref<int8> result);
    public static int8 AddWrapping(int8 left, int8 right);
    public static int8 FromBits(uint8 bits);
    public static uint8 ToBits(int8 value);
}
```

Other proposed signed widths substitute corresponding unsigned matching-width
bits types (e.g., `int64.FromBits(uint64)`); bit reinterpretation is *not*
`int8(uint8Value)` value conversion. The `Try...` family uses the accepted
vacant-output contract ([language contracts](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate));
per-member proof and compiled outcome metadata remain **OPEN**.
`FromBits` interprets its declared width; no native or foreign layout follows.

The proposed float/decimal `KeyEquals`/`GetKeyHashCode` members follow the
accepted numeric-key semantics in
[libraries](../specs/libraries.md#scalar-mathematics-keys-and-conversion);
collections still select an explicit paired comparer (§2). **OPEN adjacent
to the classification and stepping candidates:** negative-sign behavior for
zero/NaN, decimal counterparts, signaling-NaN behavior, `NextUp`/`NextDown`
endpoints, zero transitions, and `CopySign` payload handling. FMA and
bit-reinterpretation signatures remain candidate members with the canonical
[floating and representation boundaries](../specs/language.md#primitives-strict-arithmetic-and-text);
decimal bit layout and validity remain open.

### Numeric conversion and scalar operation signatures

The existing conversion-protocol declarations propose independently selected
source/destination pairs, not automatic primitive interfaces. The candidate
pairs below leave pair eligibility and witness lookup open; result types are
the declared destinations. Accepted conversion behavior is in
[conversion policies](../specs/conversions.md#conversion-policies).

| Destination/source candidate | `ConvertChecked` | `ConvertSaturating` | `ConvertTruncating` |
|---|---|---|---|
| `uint8 <- int32`; `int32 <- uint32`; `int64 <- int32` | `ConvertChecked` | `ConvertSaturating` | `ConvertTruncating` |
| `int32 <- float32`; `uint8 <- decimal32` | `ConvertChecked` | `ConvertSaturating` | `ConvertTruncating` |
| `float32 <- int64`; `float64 <- float32`; `decimal64 <- decimal32` | `ConvertChecked` | **OPEN** distinct pair policy | **OPEN** distinct pair policy |

These candidate pairs use the accepted
[conversion policies](../specs/conversions.md#conversion-policies); pair
eligibility and witness lookup remain open. For example, the proposed witness shape yields
`static uint8 ConvertChecked(int32 value)` in a `uint8`/`int32` conformance and
`static int32 ConvertTruncating(float32 value)` in an `int32`/`float32`
conformance. There is no generic `FromInt32` family or conversion chain. **OPEN:** exact
pair eligibility, source/destination witness lookup, user conversion
precedence, cross-format
NaN payloads and explicit alternate-rounding APIs; typed binary-to-decimal
eligibility cannot be inferred from the sample pairs. No conversion protocol
is a bit-reinterpretation protocol, and no declaration here promises
NoThrow.

The shared `BitOperations` utility and the following per-type integer
candidates make operand/result/count widths reviewable without pretending
that every scalar has every mathematical function or that an operation
secretly converts operands:

```terrum
public struct BitOperations
{
    public static int32 LeadingZeroCount(uint32 value);
    public static int32 TrailingZeroCount(uint32 value);
    public static int32 PopCount(uint32 value);
    public static uint32 RotateLeft(uint32 value, int32 count);
    public static uint32 RotateRight(uint32 value, int32 count);
    public static uint32 ReverseBits(uint32 value);
    public static uint32 ReverseEndianness(uint32 value);
    public static bool IsPow2(uint32 value);
    public static bool TryRoundUpToPowerOfTwo(uint32 value, init ref<uint32> result);
    public static int32 LeadingZeroCount(uint64 value);
    public static int32 TrailingZeroCount(uint64 value);
    public static int32 PopCount(uint64 value);
    public static uint64 RotateLeft(uint64 value, int32 count);
    public static uint64 RotateRight(uint64 value, int32 count);
    public static uint64 ReverseBits(uint64 value);
    public static uint64 ReverseEndianness(uint64 value);
    public static bool IsPow2(uint64 value);
    public static bool TryRoundUpToPowerOfTwo(uint64 value, init ref<uint64> result);
}
```

Accepted primitive-member placement and `Abs`/`Min`/`Max`/`Clamp` selection
are in the
[library specification](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
The integer-specific `TryAbs`, `Sign`, `DivRem`, `BigMultiply`, and
`GreatestCommonDivisor` contracts remain proposed; their per-type signatures
and generic-interface access are in the
[combined numeric-selection proposal](numeric-selection-members.md).

`BitOperations` candidates operate on actual bits of the indicated unsigned
width; a parallel `uint8/16/128` or `nuint` overload requires its own
width/target profile. Counts return `int32`, large enough for the proposed
fixed integer widths. The following boundary choices are **PROPOSED API
contracts**, not changes to language shift operators or approved primitive
members:

| Operation | Proposed boundary contract |
|---|---|
| `LeadingZeroCount`, `TrailingZeroCount` | Zero returns the operand width (32 or 64); other results are between zero and width minus one. |
| `PopCount` | Counts set bits in the declared width; zero returns zero and an all-ones value returns the width. |
| `RotateLeft`, `RotateRight` | Normalize the signed count modulo the operand width, including negative counts and `int32.MinValue`, without checked negation overflow. Multiples of the width return the original bits. Negative left counts rotate right and vice versa. |
| `ReverseBits` | Reverse all bits within the declared width; no widening, sign interpretation or target byte order participates. |
| `ReverseEndianness` | Reverse the order of the four or eight bytes while preserving the bit order within each byte. This is a value operation, not a load/store or a promise about native memory layout. |
| `IsPow2` | True exactly for a nonzero value with one set bit; zero is false. |
| `TryRoundUpToPowerOfTwo` | Publish the least representable power of two greater than or equal to a positive input. Zero or a required result beyond the width returns false with the output vacant; an existing power of two is unchanged. |

For `uint32`, the largest successful power-of-two rounding input is
`0x80000000`; `0x80000001` fails. The corresponding `uint64` boundary is
`0x8000000000000000`. Output publication metadata remains **OPEN**, as for
other `Try...` members. These named rotations deliberately normalize counts;
they do not weaken the checked count validation of source `<<`, `>>` or
`>>>`. No hardware instruction availability, compile-time-constant count
requirement or NoThrow declaration is inferred from a service name.
Native-size overloads and signed-bit handling remain **OPEN**. No hidden
`uint64` conversion or unsafe bit-layout access is assumed.

The following integer-specific additions remain **PROPOSED**
primitive-member contracts; they are not additional source operators or
inferred primitive witnesses:

| Integer member | Proposed boundary contract |
|---|---|
| Signed-minimum `Abs` | A same-width magnitude that is not representable follows checked overflow; do not widen or wrap implicitly. This remains a candidate for the per-type contract. |
| `TryAbs` | Publish the exact same-width nonnegative magnitude on success. Signed minimum is not representable and returns false with the output vacant; all other inputs succeed, including zero. |
| `Sign` | Returns `int32` -1, 0 or 1 according to the operand's order relative to zero, including a signed-minimum operand. The three-valued result does not depend on the input width. |
| `DivRem` | Produce one tuple containing the truncation-toward-zero quotient and dividend-signed remainder. Zero divisor and signed minimum divided by -1 fail before publishing any result. |
| `BigMultiply(int32, int32)` | Return the exact signed product in `int64`; every pair of `int32` operands is representable, including two signed minima. This is not a widened result chosen by overload inference. |
| `GreatestCommonDivisor` | Return the nonnegative greatest common divisor in the corresponding unsigned width. Signs do not affect it; two zero operands return zero, and one zero operand returns the other's full magnitude. |

`DivRem` follows the existing combined checked division/remainder contract in
the [instruction-set reference](../specs/il-instruction-set.md#terminators-and-cfg),
including its difference from remainder alone: signed minimum `% -1` yields
zero, but `DivRem` must reject because the quotient is unrepresentable.
Tuple labels are source-view information, not a new two-result foreign ABI.
No independent evaluation, retry or partial tuple publication is implied.

The unsigned GCD result deliberately represents the magnitude of signed
minimum: `GreatestCommonDivisor(int32.MinValue, 0)` is `0x80000000`, with
the analogous `0x8000000000000000` result at 64 bits. Implementations must
not obtain that magnitude by checked same-width signed negation or by using
the overflowing signed-minimum divided-by-minus-one case. This does not
provide a total signed `Abs` or an unchecked conversion policy. A wider
`BigMultiply` operation would need an explicitly available result/profile;
no `int128` primitive or allocation-backed big integer is implied here.
Conditional publication, concrete failure transport and effect metadata
remain **OPEN**; neither a `Try` name nor an exact widened product grants a
NoThrow declaration.

Accepted numeric-member placement and layered generic-math direction are in
the [library specification](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
Exact generic-math interface names, shape, member mapping, signatures,
witness mapping and implementation support remain **OPEN** in the
[combined numeric-selection proposal](numeric-selection-members.md); the general
library specification is not approved as a whole.
`Sqrt`/`Pow`/`Exp`/`Log`/`Sin`/`Cos`/`RoundToIntegral` separately need
special-value, domain, precision and rounding-mode contracts. Strict
candidates follow the accepted
[floating arithmetic contract](../specs/language.md#primitives-strict-arithmetic-and-text).
Native/approximate variants and reproducible reductions need separate names
and contracts. Decimal transcendental availability, cohorts and rounding
must be separately determined.

## 7. Fixed SIMD, scalable vectors and tensors

The accepted distinction between SIMD ABI values and logical tensor views,
including family, mask, shape, and reduction contracts, is specified in
[libraries](../specs/libraries.md#simd-as-a-family-of-shapes) and
[tensor views](../specs/libraries.md#views-shapes-and-tensor-primitives).
All declarations here remain proposals, not adopted APIs or runtime exports;
concrete-member semicolons use this document's reference-signature notation.

### Fixed-width SIMD

Fixed-vector membership and common operations require explicit checked
registration; the spelling and apparent mask pairing do not establish a
witness. The accepted family/mask semantics are in
[libraries](../specs/libraries.md#simd-as-a-family-of-shapes); exact family
registration and associated-mask spelling remain open.

`simd.api.terrum` supplies local design sketches for the `simd64`,
`simd128`, `simd256`, and `simd512` plus matching `simd*mask` names. The
following is a fully shaped **128-bit reference specialization**. Its
element/mask registration and operation eligibility follow the accepted
[SIMD family contract](../specs/libraries.md#simd-as-a-family-of-shapes);
exact registrations remain open, and not every `T` supports every listed
operation.

```terrum
public struct simd128<T> : IAdditionOperators<simd128<T>, simd128<T>, simd128<T>>
    where T : [SimdElement], [Copy], [Sized]
{
    public static nuint LaneCount { get; }

    public T GetLane(nuint lane);
    public simd128<T> WithLane(nuint lane, T value);

    public static simd128<T> Splat(T value);
    public static bool TryCreate(Span<T> lanes, init ref<simd128<T>> result);
    public static simd128<T> Load(Span<T> source);
    public static simd128<T> LoadMasked(Span<T> source, simd128mask<T> active,
        T inactive);
    public static void Store(simd128<T> value, write Span<T> destination);
    public static void StoreMasked(simd128<T> value, simd128mask<T> active,
        write Span<T> destination);

    public static simd128mask<T> CompareEqual(simd128<T> left, simd128<T> right);
    public static simd128mask<T> CompareNotEqual(simd128<T> left, simd128<T> right);
    public static simd128mask<T> CompareLessThan(simd128<T> left, simd128<T> right);
    public static simd128mask<T> CompareLessThanOrEqual(simd128<T> left,
        simd128<T> right);
    public static simd128mask<T> CompareGreaterThan(simd128<T> left,
        simd128<T> right);
    public static simd128mask<T> CompareGreaterThanOrEqual(simd128<T> left,
        simd128<T> right);
    public static simd128<T> Select(simd128mask<T> condition,
        simd128<T> whenTrue, simd128<T> whenFalse);
}

public struct simd128mask<T>
    where T : [SimdElement], [Copy], [Sized]
{
    public static nuint LaneCount { get; }

    public bool GetLane(nuint lane);
    public simd128mask<T> WithLane(nuint lane, bool value);
    public static simd128mask<T> First(nuint activeLanes);
    public static simd128mask<T> And(simd128mask<T> left, simd128mask<T> right);
    public static simd128mask<T> Or(simd128mask<T> left, simd128mask<T> right);
    public static simd128mask<T> Xor(simd128mask<T> left, simd128mask<T> right);
    public static simd128mask<T> Not(simd128mask<T> value);
    public static nuint CountTrue(simd128mask<T> value);
    public static bool Any(simd128mask<T> value);
    public static bool All(simd128mask<T> value);
    public static bool None(simd128mask<T> value);
}
```

`TryCreate` requires exactly `LaneCount` source elements; `Load` and `Store`
use the first `LaneCount` elements and require that many to be in bounds.
Their failure behavior is not selected here. Masked operations access only
active lanes: an inactive load lane uses `inactive`, and an inactive store
lane does not access memory. Active lanes must be in bounds. `write Span<T>`
is required to store; `mut Span<T>` only allows rebinding the descriptor.
Lane access checks bounds; exact failure contracts, mask bit layout, lane
order, alignment and fixed-vector ABI remain open. The accepted mask contract
is in [libraries](../specs/libraries.md#simd-as-a-family-of-shapes).

#### Operation and conversion signature inventory

The existing section-6 protocols remain the source of scalar operation
contracts. These are **candidate signature shapes** for supported
specializations, not a declaration that every `T` supports every operation.
Addition is the one protocol shown on the concrete generic head above; other
protocols remain independently selected. In particular, these do not add
vector shift-count rules or change scalar integer shift/division behavior.
No operator becomes a universal SIMD requirement.
The operator block is a member excerpt from an eligible `simd128<T>`
declaration, not namespace-level operator declarations or new family syntax.

```terrum
// Members of an eligible simd128<T> declaration.
public static simd128<T> operator +(simd128<T> left, simd128<T> right);
public static simd128<T> operator -(simd128<T> left, simd128<T> right);
public static simd128<T> operator *(simd128<T> left, simd128<T> right);
public static simd128<T> operator /(simd128<T> left, simd128<T> right);
public static simd128<T> operator %(simd128<T> left, simd128<T> right);
public static simd128<T> operator +(simd128<T> value);
public static simd128<T> operator -(simd128<T> value);
```

```terrum
public static class Simd128Conversions
{
    public static simd128<uint32> WidenLowerToUInt32(simd128<uint16> value);
    public static simd128<uint32> WidenUpperToUInt32(simd128<uint16> value);
    public static bool TryNarrowCheckedToUInt16(simd128<uint32> lower,
        simd128<uint32> upper, init ref<simd128<uint16>> result);
    public static simd128<uint16> NarrowSaturatingToUInt16(simd128<uint32> lower,
        simd128<uint32> upper);
    public static simd128<uint16> NarrowTruncatingToUInt16(simd128<uint32> lower,
        simd128<uint32> upper);
}
public static class Simd128Reductions
{
    public static float32 SumFloat32StrictCandidate(simd128<float32> value);
    public static float32 SumFloat32RelaxedCandidate(simd128<float32> value);
}
```

The operator signatures use the candidate scalar-protocol vocabulary; each
specialization remains subject to the accepted scalar rules in
[libraries](../specs/libraries.md#scalar-mathematics-keys-and-conversion)
and [conversions](../specs/conversions.md#conversion-policies). Vector
comparisons are named operations returning the registered mask, not scalar
`bool` witnesses. Widening and narrowing signatures illustrate concrete
lane-count-compatible shapes; a generic associated result relationship is not
specified. A full-vector widening retains both lower and upper results;
neither half alone preserves every source lane.
The two reduction names are only a place to review distinct
strict/relaxed policies. Their exact policy definitions and lane order are
**OPEN**; floating-point reduction, reassociation and contraction must not be
silently selected. The approved associated-identity clause is in the
[grammar](../specs/grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding);
it does not define this vector family's relationship or approve a generic
conversion signature.

| Explicit member substitution inventory | Registration status |
|---|---|
| `simd64<T>` + `simd64mask<T>` | Proposed same member categories; width, supported element/mask pairs and ABI require individual checking |
| `simd128<T>` + `simd128mask<T>` | Fully shaped sample above; family/mask registration remains OPEN |
| `simd256<T>` + `simd256mask<T>` | Proposed same member categories with checked lane count, target support and fallback |
| `simd512<T>` + `simd512mask<T>` | Proposed same member categories with checked lane count, target support and fallback |

This is a generative substitution inventory, not hundreds of duplicate
declarations: substitute a width only after registering and checking that
concrete pair. It makes no claim that the target supports every width or
element combination, nor that every operation is supplied by every pair.

### Scalable SIMD

The candidate `simd<T>` is genuinely hardware-scalable and initially
sizeless, not a fixed-width alias or scalar-callback vectorization promise;
see the [canonical SIMD direction](../specs/libraries.md#simd-as-a-family-of-shapes).
The complete callable declaration and target profile remain **BLOCKED/OPEN**:
lane/mask ABI, storage, call/return, suspension and spill rules cannot be
inferred from the fixed-width signatures.

### Tensor shapes and views

The accepted tensor shape, signed-stride, writable-view, and backing rules are
in [library tensor views](../specs/libraries.md#views-shapes-and-tensor-primitives)
and [lifetimes](../specs/lifetimes.md#three-independent-dependencies).
The declarations below remain candidate reference signatures. How shape
creation owns or retains metadata, and how a result expresses both storage
and shape dependencies, remain **OPEN**.

```terrum
public struct TensorShape
{
    public nuint Rank { get; }
    public nuint ElementCount { get; }
    public Span<nuint> Lengths { get; }
    public Span<nint> Strides { get; }

    public static bool TryCreate(Span<nuint> lengths, Span<nint> strides,
        init ref<TensorShape> result);
}

public struct TensorSpan<T>
{
    public TensorShape Shape { get; }
    public nuint ElementCount { get; }

    [MatchedAccess]
    public ref<T> this[Span<nuint> indexes] { get; }

    [MatchedAccess]
    public TensorSpan<T> Slice(Span<nuint> origin, Span<nuint> lengths);
    [MatchedAccess]
    public TensorSpan<T> Transpose(Span<nuint> permutation);
    [MatchedAccess]
    public TensorSpan<T> Reshape(TensorShape shape);
}

public static class TensorViews
{
    public static TensorSpan<T> View<T>(Span<T> storage, TensorShape shape,
        nuint origin);
    public static write TensorSpan<T> View<T>(write Span<T> storage,
        TensorShape shape, nuint origin);
    public static TensorSpan<T> BroadcastTo<T>(TensorSpan<T> source,
        Span<nuint> lengths);
    public static bool TryAsDenseSpan<T>(TensorSpan<T> source,
        init ref<Span<T>> result);
    public static void CopyTo<T>(TensorSpan<T> source,
        write TensorSpan<T> destination)
        where T : [Copy];
}
```

`TryCreate` leaves metadata ownership/source unresolved. The remaining view
operations follow the accepted shape, projection, alias and copy rules in
[library tensor views](../specs/libraries.md#views-shapes-and-tensor-primitives);
the multi-source lifetime notation for `View` remains open.

### Tensor operations

The signatures below are candidate names and shapes with explicit sources
and writable destinations. Accepted `[Copy]`, shape/alias, empty-reduction,
and strict row-major rules are in
[library tensor views](../specs/libraries.md#views-shapes-and-tensor-primitives).
Exact reduction names, cross-width reproducibility and separately selected
relaxed policies remain **OPEN**.

```terrum
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
    public static void Subtract<T>(TensorSpan<T> left, TensorSpan<T> right,
        write TensorSpan<T> destination)
        where T : ISubtractionOperators<T, T, T>, [Copy];
    public static void Multiply<T>(TensorSpan<T> left, TensorSpan<T> right,
        write TensorSpan<T> destination)
        where T : IMultiplyOperators<T, T, T>, [Copy];
    public static void Scale<T>(TensorSpan<T> source, T scalar,
        write TensorSpan<T> destination)
        where T : IMultiplyOperators<T, T, T>, [Copy];

    public static T SumStrictCandidate<T>(TensorSpan<T> source)
        where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, [Copy];
    public static T DotStrictCandidate<T>(TensorSpan<T> left, TensorSpan<T> right)
        where T : IAdditionOperators<T, T, T>, IMultiplyOperators<T, T, T>,
                 IAdditiveIdentity<T, T>, [Copy];
    public static void ReduceRowsStrictCandidate<T>(TensorSpan<T> source,
        write TensorSpan<T> destination)
        where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, [Copy];
    public static void MatMulStrictCandidate<T>(TensorSpan<T> left,
        TensorSpan<T> right, write TensorSpan<T> destination)
        where T : IAdditionOperators<T, T, T>, IMultiplyOperators<T, T, T>,
                 IAdditiveIdentity<T, T>, [Copy];

    public static float32 SumRelaxedCandidate(TensorSpan<float32> source);
    public static float32 DotRelaxedCandidate(TensorSpan<float32> left,
        TensorSpan<float32> right);
    public static void MatMulRelaxedCandidate(TensorSpan<float32> left,
        TensorSpan<float32> right, write TensorSpan<float32> destination);
}
```

These API signatures remain proposals; strict candidates follow the accepted
row-major contract rather than selecting an accelerated reduction policy.
`MatMulStrictCandidate` requires rank-two operands with matching contraction
dimensions and a destination of the explicit result shape; a zero-length
contraction produces the additive identity. `ReduceRowsStrictCandidate` reduces
the last dimension to an explicit destination shape under the accepted
logical order. The relaxed floating signatures must not imply an unrecorded
contraction or reassociation permission.
No tensor primitive promises SIMD dispatch or hidden scratch storage.

## 8. Callables, tasks, errors, time, random and application services

The accepted `func`/`fnptr` contracts, including closed signatures,
capture/backing dependencies, and the absence of implicit boxing or multicast,
are in [type families](../specs/type-families.md#callable-values-and-tuples)
and [lifetimes](../specs/lifetimes.md#three-independent-dependencies).
Foreign ABI/callback details and exact grammar remain open.

```terrum
// Fixed-arity family notation only; exact callable grammar remains illustrative.
func<Arg0, Arg1, TResult>
fnptr<Arg0, Arg1, TResult>

// Accepted type-family direction; Task members are not specified here.
Task<TResult = void>
Task // expands to Task<void>, not general type erasure
```

The accepted async result, start/await, executor, affinity, transfer and
completion-responsibility contracts are in
[language](../specs/language.md#storage-concurrency-entry-and-boundaries)
and [lifetimes](../specs/lifetimes.md#ownership-transfer-and-cleanup).
Exact task, executor and awaiter APIs remain open.

`IRandom` is referenced as a nominal protocol for mutable generic use through `write ref<TRandom>`, without defensive copies/boxing; storing concrete `TRandom` additionally requires `[Sized]`. The design sample defines `mut uint32 NextUInt32()` and `ISeedableRandom.Reseed(uint32 seed)` only; no RNG quality, entropy or cryptographic guarantee is specified.

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

`CounterRandom(uint32 seed)` and
`DifferentRandom<TRandom>(TRandom inner, uint32 mask)` with
`TRandom : IRandom, [Sized]` are sample implementations, not mandatory
algorithms. Entropy, seed material, statistical quality and cryptographic
security require separate capability contracts; `NextUInt32` alone promises
none. See the complete primitive/protocol inventory in §§1 and 6.

Accepted entry forms and argument-root lifetimes are specified in
[language](../specs/language.md#storage-concurrency-entry-and-boundaries);
the proposal retains those forms below as API context.

The accepted scoped terminal/output behavior is in
[language](../specs/language.md#storage-concurrency-entry-and-boundaries).
The following candidate declarations retain the proposed writer API:

```terrum
public enum TerminalOutput : int32 { Standard, Error }
public static class Terminal
{
    public static void Write<TContent>([TextTarget] TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    public static void WriteLine<TContent>([TextTarget] TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    public static Task WriteLineAsync<TContent>([TextTarget] TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    public static write TextWriter Output { get; }
    public static write TextWriter Error { get; }
}
public mut class TextWriter : ITextWriter
{
    public mut bool TryWrite(TextSpan text);
    public mut bool TryWriteScalar(rune value);
    public mut bool TryWriteFormatted<TValue>(ref<TValue> value, int32 alignment, TextSpan format)
        where TValue : IFormattable;
    public mut void Write<TContent>([TextTarget] TContent content)
        where TContent : IFormattable;
    public mut void WriteLine<TContent>([TextTarget] TContent content)
        where TContent : IFormattable;
    public mut Task WriteLineAsync<TContent>([TextTarget] TContent content)
        where TContent : IFormattable;
}
```

Partial writer progress and refusal follow the canonical
[scalar formatting contract](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
The proposed async writer calls format once into operation-owned buffering,
then drain it; task-drop and retained-capture behavior follow the accepted
[async contract](../specs/language.md#storage-concurrency-entry-and-boundaries).

## 9. Accepted direction and unresolved choices

This proposal records the library/API choices and their dependencies.
Source-language choices and unspecified forms remain in the
[grammar specification](../specs/grammar-spec.md), not in this API inventory.

The following separates semantic constraints from source and API decisions
that remain open:

1. **Presence/result:** accepted optional normalization and transparent alias rules are in [language](../specs/language.md#types-and-operations) and [grammar](../specs/grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding). Whether `Option<T>` is exported remains a library decision; `Result<T,E>` and error payload design remain proposed. Keep present-null distinct from missing when an API needs both; conditional publication follows [language contracts](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate).
2. **Equality/hash semantics:** accepted numeric-key distinctions are in [libraries](../specs/libraries.md#scalar-mathematics-keys-and-conversion); clarify whether concrete `int32.GetHashCode()` shares that semantics and settle reusable primitive key-comparer declarations. No universal root contract follows.
3. **`init ref<T>` contracts:** accepted initialization, `Create(init value)` spelling and outcome rules are in [language contracts](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate) and [grammar](../specs/grammar-spec.md#51-initialization-loans-at-call-sites). Per-API conditional publication clauses and compiled output metadata remain open; these proposed signatures define no executable ABI.
4. **Memory signatures:** checked location/range grammar; bitwise-eligible CAS representations; fence coverage/compiler-fence scope; non-temporal/cache safety and target support. Accepted operation defaults, progress limits and arithmetic policy are in the [memory model](../specs/memory-model.md#library-access-surface); the [compare-exchange order-pair table](atomic-cas-order-pairs.md) specifies the nine valid compile-time-known semantic pairs.
5. **Collections/elements:** allocator/failure contract, non-Copy/borrowed/affine storage, guard alias/reentrancy enforcement, write-access naming, builder abandonment/error contract, lending iteration.
6. **Numerics:** integer associated-relation declarations and witness lookup; primitive operator/conversion matrix; floating/decimal policy gaps; align key hashing with comparer.
7. **SIMD/tensors:** fixed family/mask syntax, member registration/ABI and scalable restrictions; tensor shape storage/anchor, checked stride math, alias contract and reproducible reduction API.
8. **Tasks/errors/services:** Task members/awaiter/completion ownership representation; exact scheduling/affinity APIs; cancellation; error/exception object/failure ABI; RNG guarantees beyond the sample methods; time and filesystem/network abstractions.
9. **Notation/metadata:** accepted generic defaults, aliases, character forms and output contracts are specified in [grammar](../specs/grammar-spec.md), [language](../specs/language.md) and [language contracts](../specs/language-contracts.md). Distinguish them from open family registration/conditional-member and fixed-signature family details; handler/output contract binding and public metadata/ABI still need checking.
10. **Collection `params`:** accepted normal/expanded binding, ranking and spread direction are in [libraries](../specs/libraries.md#callable-kernels-and-collection-parameters) and [language](../specs/language.md#callable-values-params-and-tuples). Exact grammar/diagnostics and generic collection-expression inference remain open.
11. **Adapters/extension interfaces:** accepted nominal-witness, adapter and wrapper distinctions are in [type families](../specs/type-families.md#nominal-witnesses-and-associated-relationships). Specify adapter intent/forwarding, dispatch metadata and any one-way projections with validity, representation, access, ownership, lifetime and container guarantees; no physical multiple-vtable layout is selected.
12. **Families and operation protocols:** accepted explicit family participation, checked projection and non-erasure rules are in [type families](../specs/type-families.md#explicit-families); the accepted `TextSpan<void>` relationship is specified in [text views](../specs/type-families.md#text-views). Specify eligible adapter projections and readonly views with representation, validity, access, backing/lifetime and cost checks; writable storage remains invariant.

## 10. Open signature and contract alignments

The application-library proposal includes `Result<T,E>`/`Error`,
`Dictionary`/`HashSet`, task completion and ownership, clocks/time,
cancellation, file/stream and network endpoints, and application entry. RNG
quality/state guarantees beyond the scoped sample, console input, and general
service registration are outside this proposal. No signature freezes private
layout or establishes a public managed ABI.

The following distinctions and signature questions remain open:
- `int32.GetHashCode()` does not imply a universal root contract. Review only whether the primitive method denotes the same key-hash semantics as `float32`/`decimal32` or is a separate concrete helper; generic map-key use remains governed by the accepted paired-comparer contract in [libraries](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
- `TensorShape.Lengths`/`Strides` are `Span` properties but the accepted shape rules require immutable independently live metadata ([libraries](../specs/libraries.md#views-shapes-and-tensor-primitives)). Their exact readonly/view qualification and ownership source are not expressed by these signatures.
- `TensorViews.View` must preserve the storage and shape dependencies distinguished in [lifetimes](../specs/lifetimes.md#three-independent-dependencies), but no final multi-source lifetime/anchor syntax exists; shown signature alone is insufficient proof.
- `IParsable.TryParse(... init ref<T>)` follows the accepted [parse/publication contract](../specs/libraries.md#scalar-mathematics-keys-and-conversion): true fully initializes, false leaves the output vacant. The library declarations and their compiled conditional-outcome metadata must encode and prove this contract; the signature alone does not.
- The [memory model](../specs/memory-model.md) defines accepted operation families, result forms and ordering; the signatures above propose source forms while exact grammar, target range contracts and some CAS declarations remain open.
- `Task<TResult = void>` uses the approved generic-default syntax; this particular library type and its task members are not finalized. `Task` does not erase an owning `Task<T>` result ([type families](../specs/type-families.md#explicit-families)). Optional normalization is semantic; source `T?` is approved, while exported `Option<T>` is only proposed ([language](../specs/language.md#types-and-operations)).
- [Filesystem metadata and traversal](#proposed-filesystem-metadata-and-directory-traversal) propose a terminal-on-error lending cursor and scoped rename operations. Review link traversal, enumeration invalidation, metadata availability/precision and which target rename guarantees are required; these are API/host-profile choices, not newly adopted source syntax.
- [Address/datagram operations](#proposed-address-values-and-datagram-operations) propose family-preserving address keys, capability-qualified binding and whole-message sends. Review numeric address grammar/scope IDs, client/listener capability separation and error consumption reporting before adopting these signatures.
- [Binary values and fixed codecs](#proposed-binary-values-and-fixed-codecs) propose explicit byte-order operations and a bounded fixed-length witness. Review wire profiles, permitted codec effects and conditional publication; fixed-size encoding does not settle incremental schema registration, reflection or general object serialization.
- [Globalization and civil-time providers](#proposed-globalization-and-civil-time-providers) propose immutable versioned data, explicit culture-sensitive formatting and inspectable local-time mappings. Review locale identifiers/fallback, calendar/range/leap-second profiles, zone rule versions and ambiguity policy; these do not alter invariant Core text or clock contracts.
- [Borrowed byte-stream storage](#proposed-borrowed-byte-stream-storage) proposes a fixed-capacity memory stream over an exclusive backing loan. [Progress-aware stream operations](#proposed-progress-aware-stream-operations) propose richer terminal results and exact-read/write-all helpers; review cancellation reporting, provider guarantees and capture metadata. Buffered owning wrappers and two-stream copy recovery remain open.
- [Arena and rental storage](#proposed-borrowed-arena-and-typed-rental-storage) proposes fixed borrowed bytes and explicit-provider Copy-element pooling. Review disjoint slice authority, alignment/zero-length placement, rental/provider dependencies and release metadata; no pooled capacity is implicitly initialized.

## 11. Scoped proposed signatures: random and `uint32`

The single scoped sample inventory is now in §8 (with primitive and conversion
relationships in §§1 and 6); this heading preserves earlier section links.
It is not a production RNG profile.

## 12. Proposed treatment of consequential open shapes

The following are candidate treatments, not accepted declarations. Each
remains subject to the accepted access, lifetime, and failure semantics.

| Open shape | Illustrative declaration | Candidate rationale and tradeoff |
|---|---|---|
| Presence versus expected failure | `T?` for absence; `Result<T, E>` as `Success(T)` / `Failure(E)` only if an API genuinely exposes a closed domain-failure set | Treat `T?` as the canonical presence spelling; do not add a second `Option<T>` runtime representation. Add nominal `Result<T,E>` only after `E`, initialization, and failure cleanup are specified. It makes domain errors composable, at the cost of a union/witness and more explicit handling. Keep thrown exceptions separate. |
| Conditional output | `bool TryParse(TextSpan, init ref<T> value);` | Follow the accepted [outcome/publication contract](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate); exact grammar and guarded outcome metadata must be specified together. This avoids default fabrication but makes output-flow checking a language/metadata obligation. |
| CAS observation | `CompareExchangeObservation<T> AtomicCompareExchangeObserve<T>(...)`, with `bool Exchanged` and `T Previous`; distinct from success-only `bool AtomicCompareExchange<T>(...)` | Use a named observation record only when callers need both success and the previous bits. Restrict it to copied values with eligible defined representations; do not infer success through user `==`. ABI and representation validity remain open; the accepted CAS order-pair table is in the memory model. |
| Task responsibility | `Task<TResult = void>` remains a completion result; responsibility belongs to a structured owner/supervisor contract | Do not make possession or dropping of a task silently imply join, cancellation, or cleanup. A separate structured owner is more explicit and permits transfer/supervision, but requires a defined scope/ownership API. Accepted execution and completion obligations are in [language](../specs/language.md#storage-concurrency-entry-and-boundaries); exact responsibility APIs remain separate library work. |
| General conversion matrix | `T(value)` for checked; separately declared checked/saturating/truncating source-destination witnesses | Follow accepted [conversion policies](../specs/conversions.md#conversion-policies); keep floating support target-only and specify pair eligibility, witness lookup and APIs separately. |
| Collection `params` | `Collect<T>(params List<T> values)` | Follow accepted [normal/expanded binding and ranking rules](../specs/language.md#callable-values-params-and-tuples); this keeps `params` from changing the parameter type. Exact grammar/diagnostics and generic collection-expression inference remain open; explicit construction expresses nesting, and future enumeration requires spread. |
| Time and randomness | Proposed signatures in §13 | Do not copy `DateTime`/`Random` APIs by analogy. §13 proposes distinct UTC and monotonic clock contracts, checked nanosecond duration arithmetic, and leaves RNG quality/entropy/cryptographic guarantees unspecified. This gains testable time APIs without pretending resolution equals precision, at the cost of separate timestamp domains and explicit clock injection. |

## 13. Proposed standard application-library/runtime surface

This section sketches one possible standard application-library/runtime
surface, not the minimal language baseline or a named target profile. It
offers candidate declaration shapes; none is approved by its spelling, and
the inventory is not mandatory for language conformance. Feature-specific
runtime dependencies remain explicit when used; a missing facility does not
weaken semantics or trigger an implicit fallback. The task lifecycle proposed
below does not change the accepted async function-call model. The accepted
[async execution direction](../specs/language.md#storage-concurrency-entry-and-boundaries)
does not select concurrent/parallel execution. Fields below are semantic
public state, not frozen private layout or wire ABI.

### Expected failures and error values

Use a proposed nominal `Result<T,E>` for expected domain alternatives and retain exceptions for exceptional language/runtime failures. Do not make every error an exception object or invent a universal object root. Union declaration grammar remains **OPEN**; payload ownership follows `T`/`E` (a result containing an owner is not `[Copy]`).

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

```

| **OPEN** nominal case representation | Proposed cases | Ownership |
|---|---|---|
| `Result<T,E>` | `Success(T value)`, `Failure(E error)` | Variant moves/copies only when its complete payload permits it |

`Domain`/`Value` identify a documented condition; numeric assignments/registry are open and should not be guessed from OS error constants. `Message` is optional owned immutable text, not a mandatory allocation or an implicit `ToString`; callers can handle codes without formatting. A failed Result is data, not an exception. `require` and thrown exceptions remain separate. Keep `T?`/`Option<T>` for absence, not error status.

### Keyed collections (proposed baseline)

```terrum
public mut class Dictionary<TKey, TValue, TComparer>
    : ISequence<DictionaryEntry<TKey, TValue>, DictionaryCursor<TKey, TValue, TComparer>>
    where TKey : [ManagedStorable], [Copy], [Sized]
    where TValue : [ManagedStorable], [Copy], [Sized]
    where TComparer : IKeyComparer<TKey>, [ManagedStorable], [Copy], [Sized]
{
    public nuint Count { get; }
    public nuint Capacity { get; }
    public TComparer Comparer { get; }
    public Dictionary(TComparer comparer);
    public Dictionary(TComparer comparer, nuint capacity);
    public bool ContainsKey(TKey key);
    public bool TryGet(TKey key, init ref<TValue> value);
    public mut Result<void, Error> Reserve(nuint capacity);
    public mut Result<bool, Error> TryAdd(TKey key, TValue value);
    public mut Result<void, Error> Set(TKey key, TValue value);
    public mut bool Remove(TKey key);
    public mut void Clear();
    public DictionaryCursor<TKey, TValue, TComparer> GetEnumerator();
}

public mut class HashSet<T, TComparer> : ISequence<T, HashSetCursor<T, TComparer>>
    where T : [ManagedStorable], [Copy], [Sized]
    where TComparer : IKeyComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public nuint Count { get; }
    public nuint Capacity { get; }
    public TComparer Comparer { get; }
    public HashSet(TComparer comparer);
    public HashSet(TComparer comparer, nuint capacity);
    public bool Contains(T value);
    public mut Result<void, Error> Reserve(nuint capacity);
    public mut Result<bool, Error> TryAdd(T value);
    public mut bool Remove(T value);
    public mut void Clear();
    public HashSetCursor<T, TComparer> GetEnumerator();
}

public struct DictionaryEntry<TKey, TValue>
    where TKey : [ManagedStorable], [Copy], [Sized]
    where TValue : [ManagedStorable], [Copy], [Sized]
{
    public DictionaryEntry(TKey key, TValue value);
    public TKey Key { get; }
    public TValue Value { get; }
}
public mut unique struct DictionaryCursor<TKey, TValue, TComparer>
    : ICursor<DictionaryEntry<TKey, TValue>>
    where TKey : [ManagedStorable], [Copy], [Sized]
    where TValue : [ManagedStorable], [Copy], [Sized]
    where TComparer : IKeyComparer<TKey>, [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<DictionaryEntry<TKey, TValue>> item);
    public mut void Close();
}
public mut unique struct HashSetCursor<T, TComparer> : ICursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
    where TComparer : IKeyComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public mut bool TryMoveNext(init ref<T> item);
    public mut void Close();
}
```

These proposed constructors always require a comparer; the comparer-only
overload constructs an empty collection without an element-capacity request.
No `DefaultComparer` or hidden comparer lookup is provided, and missing values
are never synthesized with `default(TValue)`. `TryGet` returns false for a missing key and leaves its output vacant; true publishes a copied value. `TryAdd` returns `Success(false)` for a duplicate without changing collection contents or count, `Success(true)` only after insertion, and `Failure(Error)` on allocation failure with no partial publication. `Set` inserts or updates and leaves the prior mapping unchanged if insertion allocation fails. `Remove` returns false when absent; `Clear` empties the collection. Construction validates capacity and is all-or-nothing; capacity/allocation failure must be a checked failure before an instance is published. The precise constructor-failure transport remains open because no constructor-failure ABI or library-wide exception hierarchy is accepted. Keys must retain equality/hash stability while stored; comparer behavior must also remain stable, and comparer equality must imply identical hashes. These collections add no automatic concurrent safety; callers need an explicit external synchronization contract. Initial key, value, element and stored-comparer constraints intentionally retain `[ManagedStorable]`, `[Copy]`, and `[Sized]`; broader affine/non-Copy storage is out of scope.

The comparer-only constructors, capacity/reservation members and dictionary
`ContainsKey` are **PROPOSED** additions. `ContainsKey` tests presence without
copying or publishing a value; a stored optional/null value is still present.
`Capacity` is usable element capacity, not the physical bucket count, and is
at least `Count`. Capacity construction and `Reserve` reserve at least the
requested capacity without creating readable empty-slot keys/values.
Successful reservation preserves contents and count; an already sufficient
capacity returns `Success(())` without allocation. Allocation failure returns
`Failure(Error)` without publishing a partial table. Other checked failures
propagate rather than being manufactured into an allocation error. Failed
reservation preserves the prior contents, count and capacity; no rollback of
external comparer side effects is promised.

Reservation cannot reduce capacity or replace the stored comparer.
`Clear` retains reserved capacity but releases initialized entries and their
root dependencies. Capacity/count arithmetic and backing-loan checks precede
storage changes. Unlike the list/ordered cursor invalidation proposal in §5,
the keyed cursor loan below prohibits collection mutation while live;
reservation cannot rehash or relocate that backing behind the loan.
Growth strategy, hash caching, bucket layout and iteration order remain
implementation details, not guarantees of these member names.

The **PROPOSED** concrete cursors complete the traversal surface without
choosing a generic associated-cursor witness or erased iterator. Each cursor
retains a loan on its collection until close and cannot outlive that backing.
Traversal visits each entry once in an unspecified order; it is not an
allocated snapshot or a promise of insertion/hash order. TryMoveNext publishes
one complete item on true and leaves the destination vacant on false
exhaustion. Close ends traversal and releases the loan under the selected
standard contract; standard consumers invoke it once, without inferring an
idempotent reset/restart or closed-use operation.

Every successful advance copies an explicitly Copy-constrained entry/value
into its item output. A copied entry does not
expose a writable dictionary slot, and its key/value copies retain their own
ordinary backing dependencies. The initial traversal contract excludes
collection mutation while a cursor is live, including `Set` replacing an
existing value; it does not rely on eventual mutation detection or allow an
aliased caller to invalidate storage behind a loan. Exact loan/frame metadata
remain **OPEN**; the logical operation identities and close obligation follow
the selected synchronous protocol in §5.
There is no implied `[NoThrow]` cleanup registration, cross-thread
safety, lending iteration or consuming transfer of stored elements.

### Tasks, await, cancellation and completion responsibility

The [Core task responsibility contract](../specs/language/task-responsibility.md)
selects affine Task<T>, readonly bool IsCompleted, consuming await with
explicit named move, and every-exit settlement/transfer. The initial wait is
compiler-owned; it does not use the candidate public GetAwaiter pattern below.
The remaining methods, public task/awaiter nominal shapes, TaskStatus/
TaskOutcome, scope, cancellation and executor APIs are proposals, not physical
layouts or prerequisites inferred from that logical approval.

```terrum
public enum TaskStatus : int32
{
    Created,
    Running,
    Succeeded,
    Faulted,
    Canceled
}

public mut unique class Task<T = void>
{
    public TaskStatus Status { get; }
    public bool IsCompleted { get; }
    public bool HasOutcome { get; }
    public mut bool TryTakeOutcome(init ref<TaskOutcome<T>> outcome);
    public mut TaskAwaiter<T> GetAwaiter();
}
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
    public mut void Adopt<T>(Task<T> operation);
    public mut Task<void> JoinAsync();
    public mut void RequestCancellation();
}
```

| **OPEN** task/cancellation family | Proposed signatures and relationships |
|---|---|
| `TaskOutcome<T>` nominal sum | `Completed(T value)`, `Faulted(Error error)`, `Canceled`; exact representation/failure contract OPEN |
| `Task<T>` | Affine owner and IsCompleted contract selected; other representative methods and nominal representation remain proposed |
| conditional Copy-only observation | `Task<T>.GetOutcomeCopy() -> TaskOutcome<T>` only when **all** variant payloads are `[Copy]`; relationship syntax OPEN |
| `TaskAwaiter<T>` | Complete representative signature above; continuation registration and error propagation OPEN |
| `CancellationToken` / `CancellationSource` | Complete representative signatures above; token retains request-state dependency and source owns unique request authority |
| `TaskScope` | Representative signatures above; explicit join settles every adopted completion/cleanup, with exact error/shutdown policy still OPEN |
| `IExecutor` / **new proposal** | `Schedule(func<void>)`, `Yield() -> Task<void>`, `ResumeOn(IExecutor) -> awaitable`; scheduler and continuation affinity remain separately checked contracts |

The approved generic-default form makes bare `Task` exactly `Task<void>`,
not a base type or result erasure ([type families](../specs/type-families.md#explicit-families)).
The selected `Task<T>` is an affine completion owner, not a freely copied
`[Copy]` value. Ordinary `await operation` rejects for a named owner;
`await move operation` consumes it and supplies T or ordinary terminal failure.
The candidate `TaskScope.Adopt` accepts responsibility for an operation,
not a cold-task `Start`; a named unique task is passed as
`scope.Adopt(move task)`, where `move` is a call expression, not a parameter
modifier.
`TaskAwaiter<T>.GetResult()` and Copy-only
`GetOutcomeCopy()` are distinct paths: the former borrows from the operation
owner and the latter copies only valid payloads. Borrowing cannot overlap
exclusive take. `TryTakeOutcome` transfers an outcome once; false leaves its
output vacant. Completion observation remains repeatable after a take, but
payload access cannot synthesize another owning result. For `Task<void>`,
completion carries `()`. `Completed(Result<T,E>.Failure(...))` is an expected
domain failure, `Faulted(Error)` a terminal diagnostic and `Canceled` a
cooperative outcome. No catchable universal exception object or preserved
runtime exception identity/stack is promised.

`HasOutcome` is false before completion and after a successful take.
`GetResult()` and the conditional copy accessor require a completed,
remaining outcome; otherwise they fail through a checked invalid-state
path, never fabricate a value. A borrowed result retains the task-owner
dependency and cannot overlap an exclusive take.

Accepted async invocation, await, affinity and completion rules are in
[language](../specs/language.md#storage-concurrency-entry-and-boundaries)
and [lifetimes](../specs/lifetimes.md#three-independent-dependencies).
Within those constraints, this proposal's `Run` schedules an initial
invocation and adopts its full async completion without binding every helper
continuation or choosing the caller's continuation. `ResumeOn(executor)`
selects the awaiting continuation without rescheduling the producer; `Yield`
may request a scheduling boundary.

TaskScope, adoption, nonconsuming outcome accessors and cancellation APIs
remain separate library proposals. Their exact methods and policies must
satisfy the selected owner/observation/dependency contract. Ordinary task
destruction never implicitly blocks, suspends, joins, cancels or supervises;
normal and exceptional exits require explicit settlement or valid transfer.
An adopted multi-task join must settle all accepted siblings before completing
even when one fails; error combination and shutdown policy need explicit
admission. The
[checked source/capture schemas](../specs/language/dependencies-and-views.md)
do not approve these task APIs or choose their responsible operation/
payload destinations. Captures remain subject to the accepted transfer/share
and lifetime rules; task handles do not gain implicit
thread-sharing. The accepted rule that dropping a handle does not cancel work
is in [language](../specs/language.md#storage-concurrency-entry-and-boundaries).
`CancellationSource` uniquely controls a shared request; its token retains
the source dependency and may not be default-fabricated. This proposal makes
cancellation requests idempotent, monotonic and cooperative; cross-thread
request/observation guarantees remain a memory/runtime contract to specify.

GetAwaiter and result-observation signatures are **PROPOSED**, not the initial
consuming-await lowering. Task-state synchronization, error aggregation in JoinAsync,
executor selection and task result borrowing rules remain open. Captured
borrow validity follows [lifetime contracts](../specs/lifetimes.md#three-independent-dependencies).
The accepted no-implicit-caller-executor-capture direction does not settle how
this draft's task/scope API realizes scheduling; a general executor contract
and callback effects remain open.

The [selected async cursor relationship](../specs/language/async-iteration.md)
additionally requires operation-bound init-output reservation, terminal cleanup
and Boolean/output observation. IsCompleted alone cannot release that reservation;
an outcome take/borrow or ownership transfer would have to preserve its exact
formal/destination/version and live-item cleanup obligations.
This requirement does not approve the Task/TaskAwaiter/TaskScope signatures
above or reinterpret ordinary Task-returning init outputs.

### Time values and clock capabilities

```terrum
public struct Duration
{
    public int64 Nanoseconds { get; }
    public static Duration Zero { get; }
    public static Duration FromNanoseconds(int64 nanoseconds);
    public static int32 Compare(Duration left, Duration right);
    public static bool TryAdd(Duration left, Duration right, init ref<Duration> result);
    public static bool TrySubtract(Duration left, Duration right, init ref<Duration> result);
    public static bool TryMultiply(Duration value, int64 factor, init ref<Duration> result);
    public static bool TryDivideTruncating(Duration value, int64 divisor,
        init ref<Duration> result);
}

public struct Instant
{
    public int64 UnixNanoseconds { get; }
    public static Instant FromUnixNanoseconds(int64 nanoseconds);
    public static int32 Compare(Instant left, Instant right);
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

The **PROPOSED** factories accept every `int64` payload exactly and expose
the same count through the corresponding getter; neither reads a clock nor
converts a local civil time. These two nominal types do not implicitly
interconvert. `Compare` returns negative/zero/positive for less/equal/greater
without subtracting counts into a potentially overflowing duration.
`TryMultiply` rejects an unrepresentable product. `TryDivideTruncating`
explicitly discards a fractional nanosecond toward zero; zero divisors and
the signed-minimum divided by `-1` reject. Every false arithmetic result
leaves its output vacant. No floating scaling, calendar unit, saturation,
wrapping or rounding choice is hidden in these signatures.

Instant ordering compares the proposed Unix counts, not observed clock
monotonicity. Epoch/leap-second mapping remains a host-profile question;
construction alone does not verify that a timestamp corresponds to an
observed event. Formatting, parsing and conversions involving civil time
remain separately provider-qualified proposals.

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
    [Captures("destination")]
    mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures("source")]
    mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
}

public interface ISeekableByteStream : IByteStream
{
    Result<uint64, Error> GetLength();
    mut Result<uint64, Error> Seek(int64 offset, SeekOrigin origin);
}

public mut unique struct FileHandle : ISeekableByteStream
{
    public bool IsOpen { get; }
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public Result<uint64, Error> GetLength();
    public mut Result<uint64, Error> Seek(int64 offset, SeekOrigin origin);
    public mut Result<void, Error> SetLength(uint64 length);
    public mut Result<nuint, Error> Read(write Span<uint8> destination);
    public mut Result<nuint, Error> Write(Span<uint8> source);
    public mut Result<void, Error> Flush();
    public mut Result<void, Error> Close();
    [Captures("destination")]
    public mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures("source")]
    public mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
}

public class FileSystem
{
    public Result<FileHandle, Error> OpenRead(TextSpan relativePath);
    public Result<FileHandle, Error> OpenWrite(TextSpan relativePath,
        FileDisposition disposition);
    public Result<bool, Error> FileExists(TextSpan relativePath);
    public Result<FileMetadata, Error> GetMetadata(TextSpan relativePath);
    public Result<DirectoryCursor, Error> OpenDirectory(TextSpan relativePath);
    public Result<void, Error> CreateDirectory(TextSpan relativePath);
    public Result<void, Error> DeleteFile(TextSpan relativePath);
    public Result<void, Error> DeleteEmptyDirectory(TextSpan relativePath);
    public Result<void, Error> Rename(TextSpan sourcePath,
        TextSpan destinationPath, FileRenamePolicy policy);
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
    [Captures("destination")]
    public mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures("source")]
    public mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
    public mut Result<void, Error> Close();
}
public mut unique struct TcpListener
{
    public static Result<TcpListener, Error> Bind(NetworkClient network,
        TcpEndpoint endpoint);
    public bool IsOpen { get; }
    public mut Task<Result<TcpConnection, Error>> AcceptAsync(CancellationToken cancellation);
    public mut Result<void, Error> Close();
}
public class NetworkClient
{
    public Task<Result<TcpConnection, Error>> ConnectAsync(TcpEndpoint endpoint,
        CancellationToken cancellation);
}
```

`FileSystem` is an explicit host-supplied capability rooted to an authorized filesystem scope; `relativePath` cannot escape that root. There is no ambient current directory, unrestricted global `File`, arbitrary path-based service locator, or implicit path encoding conversion in this minimum. Path normalization, symbolic-link policy and platform error mapping must be specified by the host profile before adoption. `TcpEndpoint` is a small immutable value over a shared immutable `Text` handle, not a unique resource owner; `ConnectAsync` takes this value rather than retaining a temporary text span, with backing lifetime still governed by the operation's declared dependencies. DNS/network errors are explicit `Result` failures.

Reads return bytes read; `0` means EOF only when the destination is nonempty. An empty read returns zero without probing EOF. Writes may be partial and the caller advances by the returned count; the proposed contract requires a successful write of nonempty input to report positive progress, not a zero-count success that invites a retry loop. Buffer-size/alias checks occur before access. Async stream calls capture their buffer and exclusive stream access until completion, including after cancellation is requested; their declared operation lifetime must cover those loans through completion and cleanup. One in-flight mutating operation per stream is the proposed default. `Close` invalidates the Terrum wrapper authority even when the target reports an error; the underlying OS resource may then be indeterminate. Its `Result` must carry the reported failure, and the target/runtime contract must define what release is known. Never blindly retry a close against a possibly reused numeric descriptor or claim target release on every error. Cleanup of an unclosed unique handle must report or supervise target failure under a separately specified no-throw cleanup contract; it cannot silently convert failure into success. This reference makes no per-target guarantee of OS release on every failure; callers needing an observable result use `Close`, while implicit cleanup behavior still requires a runtime contract. Unique handles cannot be copied; `move` transfers release responsibility. Result unions carrying handles are likewise non-`Copy`. Network listener/client tasks follow the same cancellation and task ownership rules; no zero-copy or TLS default is implied; proposed datagrams appear in the capability matrix below.

### Proposed borrowed byte-stream storage

This candidate `Terrum.IO` adapter supplies the already listed memory-stream
shape without allocating or taking release responsibility for caller storage.
It is **PROPOSED**, including its capture/close metadata. The public witness
does not turn every byte view into an owned or seekable OS resource.

```terrum
public mut unique struct MemoryByteStream : ISeekableByteStream
{
    [Captures("storage")]
    public MemoryByteStream(write Span<uint8> storage, nuint length);
    public bool IsOpen { get; }
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public nuint Capacity { get; }
    public uint64 Position { get; }
    public Result<uint64, Error> GetLength();
    public mut Result<uint64, Error> Seek(int64 offset, SeekOrigin origin);
    public mut Result<nuint, Error> Read(write Span<uint8> destination);
    public mut Result<nuint, Error> Write(Span<uint8> source);
    public mut Result<void, Error> Flush();
    public mut Result<void, Error> Close();
    [Captures("destination")]
    public mut Task<Result<nuint, Error>> ReadAsync(write Span<uint8> destination,
        CancellationToken cancellation);
    [Captures("source")]
    public mut Task<Result<nuint, Error>> WriteAsync(Span<uint8> source,
        CancellationToken cancellation);
}
```

The constructor requires `length <= storage.Length`, sets the initial
position to zero and retains exclusive access to the backing until close.
Every supplied byte slot is already initialized; `length` selects logical
content, not a permission to treat raw uninitialized allocation as a span.
Capacity is fixed. Reads copy at most the remaining logical content and
advance by their reported count; a nonempty read at the end reports EOF.
Writes replace/extend the prefix up to capacity without allocation, and a
nonempty write with no remaining capacity reports failure rather than
zero-count success. Validation precedes access; source/destination overlap
with retained backing needs an explicit rejection or checked overlap policy,
not a hidden temporary. Suffix bytes outside the accepted write are unchanged.

This initial candidate permits seek positions only in `[0, length]`; it does
not expose an unwritten gap or silently fill caller storage. Offset arithmetic
is checked before position changes. `Flush` has no external sink to drain;
`Close` releases the backing loan, not the allocation owner, and invalidates
further I/O. Async calls may complete inline but any incomplete operation must
retain stream/buffer access and completion responsibility until settlement;
dropping a task does not release the loan. Borrowed-view storage, cancellation,
overlap and cleanup metadata remain explicit contracts before adoption.

**Remaining composition questions:** An `IByteStream` witness alone proves
neither unique ownership nor authority to close an independently aliased
stream. A future `BufferedByteStream<TStream>` must specify which concrete
owner/loan it receives and how that responsibility survives flush/close
failure; a `[Sized]` constraint or erased interface spelling does not establish
it. Likewise, `Result<nuint, Error>` reports successful progress but does not
by itself report how much a failing read/write consumed or exposed. Exact-read,
write-all and copy helpers cannot promise rollback or exact failed-call
progress until the base stream contract or a richer progress result supplies
it. The following proposal supplies a separate witness for that guarantee;
it does not retroactively strengthen `IByteStream`. Buffered ownership and
two-stream copy recovery remain library questions, not new grammar or a
fallback to assumed zero progress.

### Proposed progress-aware stream operations

These **PROPOSED** `Terrum.IO` signatures preserve an observable byte prefix
on expected failure and cooperative cancellation. No existing stream
declaration automatically implements the stronger witness.

```terrum
public enum ByteTransferStatus : int32
{
    Success,
    EndOfStream,
    Failed,
    Canceled
}
public struct ByteTransferResult
{
    public ByteTransferStatus Status { get; }
    public nuint Transferred { get; }
    public Error? Failure { get; }
    public static ByteTransferResult Success(nuint transferred);
    public static ByteTransferResult EndOfStream(nuint transferred);
    public static ByteTransferResult Failed(nuint transferred, Error failure);
    public static ByteTransferResult Canceled(nuint transferred);
}
public interface IProgressByteStream : IByteStream
{
    mut ByteTransferResult ReadWithProgress(write Span<uint8> destination);
    mut ByteTransferResult WriteWithProgress(Span<uint8> source);
    [Captures("destination")]
    mut Task<ByteTransferResult> ReadWithProgressAsync(
        write Span<uint8> destination, CancellationToken cancellation);
    [Captures("source")]
    mut Task<ByteTransferResult> WriteWithProgressAsync(
        Span<uint8> source, CancellationToken cancellation);
}
public static class ByteStreamAlgorithms
{
    public static ByteTransferResult ReadExactly<TStream>(
        write ref<TStream> stream, write Span<uint8> destination)
        where TStream : IProgressByteStream;
    public static ByteTransferResult WriteAll<TStream>(
        write ref<TStream> stream, Span<uint8> source)
        where TStream : IProgressByteStream;
    [Captures("stream", "destination")]
    public static Task<ByteTransferResult> ReadExactlyAsync<TStream>(
        write ref<TStream> stream, write Span<uint8> destination,
        CancellationToken cancellation)
        where TStream : IProgressByteStream;
    [Captures("stream", "source")]
    public static Task<ByteTransferResult> WriteAllAsync<TStream>(
        write ref<TStream> stream, Span<uint8> source,
        CancellationToken cancellation)
        where TStream : IProgressByteStream;
}
```

The result factories produce validated states: `Failure` is present exactly
for `Failed`. `Transferred` is the contiguous requested prefix delivered to
the caller's read destination or accepted from its write source, including
progress made before a reported error/cancellation. It cannot exceed the
requested span length. Read destination bytes beyond that prefix are
unchanged. Accepted writes are logical stream progress, not an assertion
that buffered bytes are flushed or durable.

| Proposed operation | Terminal and progress contract |
|---|---|
| `ReadWithProgress` | A nonempty successful read reports positive progress. EOF may accompany a final prefix or zero bytes. An empty request succeeds with zero after ordinary validation, without probing EOF. |
| `WriteWithProgress` | A nonempty successful write reports positive progress; EOF is not a valid write result. Failure/cancellation may accompany an accepted prefix. |
| `ReadExactly` | Continue on successful partial reads using only the remaining destination suffix. Success means the whole request was filled. Premature EOF reports the total delivered prefix; EOF accompanying the final required bytes satisfies the request. |
| `WriteAll` | Continue on successful partial writes using only the unaccepted source suffix. Success means the whole request was accepted, not flushed; failure reports the total accepted prefix. |
| Async counterparts | Retain stream and buffer access until terminal completion/cleanup. Cooperative cancellation returns `Canceled` with the total known prefix, even if the request was already partially processed. |

Helpers stop on failure/cancellation without replaying an accepted prefix or
silently promoting a terminal error to success, even when its count equals
the requested length. Empty helper requests perform no underlying I/O after
validation. Count accumulation is bounded by the original request, not an
unchecked running byte total. Invalid counts, zero-progress nonempty
successes and write-EOF results are checked provider-contract failures, not
retry instructions or fabricated EOF. Checked/runtime failures still
propagate; these result contracts do not turn arbitrary exceptions into
domain errors or promise recoverable counts after such exceptions.

For these progress-aware async calls, handled cooperative cancellation is
data in the normally completed task result, rather than a canceled task
outcome that loses the byte count. This is an **API proposal**, not a change
to general task cancellation. Capture/conditional-result metadata, provider
conformance and failure transport must be specified before adoption.
An ordinary `IByteStream` cannot implement this witness through an adapter
that assumes zero progress on error. Helpers borrow the stream; they never
close it, rewind it, roll back output or release its owner.

A two-stream `CopyTo` needs separate consumed-source and accepted-destination
counts plus recoverable buffered remainder on destination failure. One
`Transferred` count cannot describe that state; its signature/ownership and
resumption contract remain **OPEN** rather than being inferred from these
single-stream helpers.

### Proposed filesystem metadata and directory traversal

These declarations expand the scoped filesystem inventory, not an ambient
`Directory`/`File` service or a promise of portable filesystem transactions.
The types and signatures remain **PROPOSED**. Optional metadata means
unavailable/not applicable, not a fabricated zero length or epoch timestamp.

```terrum
public enum FileKind : int32
{
    Regular,
    Directory,
    SymbolicLink,
    Other
}
public enum FileRenamePolicy : int32
{
    NoReplace,
    ReplaceExisting
}
public struct FileMetadata
{
    public FileKind Kind { get; }
    public uint64? Length { get; }
    public Instant? ModifiedTime { get; }
}
public struct DirectoryEntry
{
    public Text Name { get; }
    public FileMetadata Metadata { get; }
}
public mut unique struct DirectoryCursor
{
    public bool IsOpen { get; }
    public ref<DirectoryEntry> Current { get; }
    public mut Result<bool, Error> MoveNext();
    public mut Result<void, Error> Close();
}
```

`OpenDirectory` transfers one enumeration/release obligation in its successful
result. A cursor retains its authorized filesystem scope. `Current` is readable
only after `MoveNext` succeeds with `true`; its loan expires before the next
advance or close. A successful `false` means exhaustion, not an error or an
empty entry. An advance error vacates the current entry and makes this initial
cursor contract terminal, while `Close` remains available. A copied eligible
entry retains its immutable `Text` name independently of cursor storage.
Neither ordering nor a snapshot of concurrent directory changes is promised.

`GetMetadata` reports an observation, not a guarantee about a later open.
Missing paths, denied access and I/O errors remain distinct from an optional
metadata field. `FileExists` returns false for absence, not for every failure.
`DeleteEmptyDirectory` does not recursively delete children. Both rename paths
remain inside the supplied capability; `NoReplace` cannot be implemented by
an unchecked existence-test/rename race. A profile without the requested
rename guarantee reports an unsupported operation rather than silently
substituting copy/delete. `SetLength` requires a writable file handle, never
creates storage authority from seekability, and does not implicitly reposition
the stream. Target extent limits, metadata precision, link traversal and exact
rename guarantees remain host-profile/API decisions before adoption.

### Proposed address values and datagram operations

This candidate separates a parsed numeric address from `TcpEndpoint`'s host
name and from DNS resolution. It does not select an OS socket representation,
an implicit IPv4/IPv6 conversion or a default network capability.

```terrum
public struct IpAddress : IFormattable
{
    public bool IsIPv4 { get; }
    public bool IsIPv6 { get; }
    public static bool TryParse(TextSpan text, init ref<IpAddress> value);
    public bool Equals(IpAddress other);
    public uint64 GetKeyHashCode();
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
public struct IpEndpoint
{
    public IpEndpoint(IpAddress address, uint16 port);
    public IpAddress Address { get; }
    public uint16 Port { get; }
}
public struct DatagramRead
{
    public IpEndpoint Peer { get; }
    public nuint BytesWritten { get; }
    public nuint? OriginalLength { get; }
    public bool IsTruncated { get; }
}
public mut unique struct DatagramSocket
{
    public static Result<DatagramSocket, Error> Bind(NetworkClient network,
        IpEndpoint endpoint);
    public bool IsOpen { get; }
    public IpEndpoint LocalEndpoint { get; }
    [Captures("destination")]
    public mut Task<Result<DatagramRead, Error>> ReceiveFromAsync(
        write Span<uint8> destination, CancellationToken cancellation);
    [Captures("source")]
    public mut Task<Result<void, Error>> SendToAsync(IpEndpoint endpoint,
        Span<uint8> source, CancellationToken cancellation);
    public mut Result<void, Error> Close();
}
```

`IpAddress.TryParse` accepts a complete numeric address, not a host name;
success initializes the output and false leaves it vacant. Family and address
bits participate in its proposed key identity: IPv4-mapped IPv6 is not
silently equated with IPv4. Parsing/canonical formatting details, scope IDs
and a separate DNS resolver surface remain **OPEN**. A key hash must
agree with the selected equality; these members grant no universal root or
automatic `IKeyComparer<IpAddress>` witness.

Binding a TCP listener or datagram socket requires the supplied
`NetworkClient` capability even though the factory is static. A returned owner
retains the granted operation dependencies; passing a capability does not
grant all endpoints, protocols or listening rights. Whether this broader
capability should keep the `NetworkClient` name or split client/listener
permissions into separate types remains an API question.

A receive consumes at most one datagram. `BytesWritten` is the copied prefix;
`IsTruncated` reports discarded payload, and `OriginalLength` is absent when
the target cannot report its full length. A zero-length packet is a valid
message, not stream EOF. An empty destination is therefore not a no-op stream
read: it can consume a packet and report truncation. A successful send accepts
one complete message, including an empty one; it returns `void`, not a byte
count suggesting that the remainder may be retried as another datagram.
Delivery is not promised by send success. The operation retains its buffer,
socket access and completion responsibility through cancellation settlement.
Exact failure/consumption reporting, size limits, address-family support and
cleanup metadata remain required target/API contracts, not implicit success
or rollback guarantees.

### Proposed UTF-8 stream text adapters

Unlike the raw byte stream, these proposed `Terrum.IO` adapters select UTF-8
explicitly; the reader also selects a malformed-input policy. They borrow a
specific stream, rather than owning/closing it or discovering one from an
ambient terminal. The borrowed
stream remains exclusively accessible to the adapter until that adapter is
closed. The whole-source constructor Captures schema is specified; this
candidate's actual exclusion/release protocol and physical metadata remain
**OPEN**.

```terrum
public mut unique struct Utf8TextReader<TStream>
    where TStream : IByteStream
{
    [Captures("source")]
    public Utf8TextReader(write ref<TStream> source,
        MalformedTextPolicy policy, nuint bufferCapacity);
    public mut Result<bool, Error> ReadScalar(init ref<rune> value);
    public mut Result<bool, Error> ReadLine(init ref<own<val<Text>>> value);
    public mut Result<void, Error> Close();
}
public mut unique struct Utf8TextWriter<TStream>
    where TStream : IByteStream
{
    [Captures("destination")]
    public Utf8TextWriter(write ref<TStream> destination, nuint bufferCapacity);
    public mut Result<void, Error> Write(TextSpan source);
    public mut Result<void, Error> WriteScalar(rune value);
    public mut Result<void, Error> Flush();
    public mut Result<void, Error> Close();
}
```

The reader distinguishes EOF (`Success(false)`, output vacant) from an empty
line (`Success(true)`, initialized empty text); an error also leaves the
output vacant and makes the reader terminal after a partial line has been
consumed. A final unterminated line is returned once. `ReadLine`
recognizes LF and CRLF and excludes the terminator. The reader decodes
complete scalars across byte reads and finishes its decoder only on
EOF observed by reading a nonempty buffer; malformed input follows the
selected policy. The byte reader converts stream bytes into `char8` code
units explicitly; it does not reinterpret an arbitrary `Span<uint8>` as a
validated text view. Buffer capacity must be positive. Writer
calls transcode scalars to UTF-8, buffer as needed and drain partial writes.
An error after some bytes were written is not rollback: this initial
adapter contract makes such an error terminal for the adapter, forbids
retrying the same `Write`, and still requires an observable `Close`.
`Flush` drains adapter bytes and calls the underlying stream's `Flush`;
`Close` attempts the same drain, releases the loan on completion and reports
failure without pretending underlying stream closure. Adapter buffer
allocation/constructor failure transport and checked capture/close metadata
are **OPEN**. These synchronous methods create no task or early release of
captured buffers.

### Proposed binary values and fixed codecs

These candidate `Terrum.IO`/`Terrum.Serialization` signatures distinguish
scalar byte encoding from object serialization and native layout. The
closed primitive functions are a potential Core leaf; their widths remain
subject to the selected primitive catalog. They need neither a stream owner
nor an Application `Error` value. No declaration below is implemented or
approved by this reference.

```terrum
public struct BinaryPrimitives
{
    public static bool TryReadInt16LittleEndian(Span<uint8> source, init ref<int16> value);
    public static bool TryReadInt16BigEndian(Span<uint8> source, init ref<int16> value);
    public static bool TryReadUInt16LittleEndian(Span<uint8> source, init ref<uint16> value);
    public static bool TryReadUInt16BigEndian(Span<uint8> source, init ref<uint16> value);
    public static bool TryReadInt32LittleEndian(Span<uint8> source, init ref<int32> value);
    public static bool TryReadInt32BigEndian(Span<uint8> source, init ref<int32> value);
    public static bool TryReadUInt32LittleEndian(Span<uint8> source, init ref<uint32> value);
    public static bool TryReadUInt32BigEndian(Span<uint8> source, init ref<uint32> value);
    public static bool TryReadInt64LittleEndian(Span<uint8> source, init ref<int64> value);
    public static bool TryReadInt64BigEndian(Span<uint8> source, init ref<int64> value);
    public static bool TryReadUInt64LittleEndian(Span<uint8> source, init ref<uint64> value);
    public static bool TryReadUInt64BigEndian(Span<uint8> source, init ref<uint64> value);
    public static bool TryWriteInt16LittleEndian(write Span<uint8> destination, int16 value);
    public static bool TryWriteInt16BigEndian(write Span<uint8> destination, int16 value);
    public static bool TryWriteUInt16LittleEndian(write Span<uint8> destination, uint16 value);
    public static bool TryWriteUInt16BigEndian(write Span<uint8> destination, uint16 value);
    public static bool TryWriteInt32LittleEndian(write Span<uint8> destination, int32 value);
    public static bool TryWriteInt32BigEndian(write Span<uint8> destination, int32 value);
    public static bool TryWriteUInt32LittleEndian(write Span<uint8> destination, uint32 value);
    public static bool TryWriteUInt32BigEndian(write Span<uint8> destination, uint32 value);
    public static bool TryWriteInt64LittleEndian(write Span<uint8> destination, int64 value);
    public static bool TryWriteInt64BigEndian(write Span<uint8> destination, int64 value);
    public static bool TryWriteUInt64LittleEndian(write Span<uint8> destination, uint64 value);
    public static bool TryWriteUInt64BigEndian(write Span<uint8> destination, uint64 value);
}
public enum ByteOrder : int32
{
    LittleEndian,
    BigEndian
}
public enum BinaryCodecStatus : int32
{
    Complete,
    NeedInput,
    NeedOutput,
    InvalidData
}
public interface IFixedBinaryCodec<T>
    where T : [Copy], [Sized]
{
    nuint EncodedLength { get; }
    BinaryCodecStatus Encode(ref<T> value, write Span<uint8> destination);
    BinaryCodecStatus Decode(Span<uint8> source, init ref<T> value);
}
public struct UInt32BinaryCodec : IFixedBinaryCodec<uint32>
{
    public UInt32BinaryCodec(ByteOrder order);
    public nuint EncodedLength { get; }
    public BinaryCodecStatus Encode(ref<uint32> value, write Span<uint8> destination);
    public BinaryCodecStatus Decode(Span<uint8> source, init ref<uint32> value);
}
public struct Int64BinaryCodec : IFixedBinaryCodec<int64>
{
    public Int64BinaryCodec(ByteOrder order);
    public nuint EncodedLength { get; }
    public BinaryCodecStatus Encode(ref<int64> value, write Span<uint8> destination);
    public BinaryCodecStatus Decode(Span<uint8> source, init ref<int64> value);
}
```

Primitive reads/writes use exactly 2, 4 or 8 prefix bytes. Insufficient input
returns false with the output vacant; insufficient destination returns false
without writing. Successful writes leave a longer destination's suffix
unchanged. Signed integers use the specified two's-complement bit pattern,
not a checked numeric conversion from the unsigned interpretation. Byte order
is explicit and independent of the target's native order; unaligned access
does not imply an atomic snapshot. Ordinary backing, access and overlap
checks still apply. There is no generic `Read<T>` that trusts arbitrary
padding, `Blittable` layout, object references or nominal invariants.

The fixed-codec witness proposes one stable positive `EncodedLength` per
instance, not a size discovered by replaying a value's formatter. `Encode`
accepts one value, writes exactly that prefix on `Complete`, and leaves the
destination unchanged on `NeedOutput`/`InvalidData`. `Decode` consumes one
fixed prefix on `Complete` and fully initializes its output only then;
`NeedInput`/`InvalidData` leave it vacant. No input/output buffer is retained.
`NeedInput` is not a valid encode result, and `NeedOutput` is not a valid
decode result. Exceptions remain separate from these expected statuses.
The sample codecs have lengths 4 and 8 and validate their constructor's
`ByteOrder`; no process byte-order setting is consulted.

An authored codec must check its selected wire profile and the resulting
value's invariants; equal native size does not establish eligibility.
Conditional initialization for `Complete`, permitted callback effects and
nonpublication/cleanup on failure require explicit checked contracts before
adoption. This subset does not cover affine/borrowed payloads, variable-length
frames, recursive object graphs or schema discovery. The incremental
`IByteEncoder<T>`/`IByteDecoder<T>` and serializer families in the matrix below
remain separate **OPEN** contracts: they need framing, consumed/written
progress on errors, resource/depth limits, schema versions and registration,
not a hidden fallback to the fixed codec or raw memory copying.

### Proposed globalization and civil-time providers

The candidate `Terrum.Globalization` surface takes explicit immutable data
snapshots. It does not consult a current-thread/process culture, operating
system time zone or mutable global data registry. These are **PROPOSED**
reference signatures; no locale inventory, data format or provider ABI is
selected by their names.

```terrum
public struct Locale : IFormattable
{
    public Text Id { get; }
    public static bool TryParse(TextSpan text, init ref<Locale> value);
    public bool Equals(Locale other);
    public uint64 GetKeyHashCode();
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
public interface ICultureDataProvider
{
    Result<CultureData, Error> Load(Locale locale);
}
public class CultureData
{
    public Locale Locale { get; }
    public Text DataVersion { get; }
    public static Result<CultureData, Error> Load<TProvider>(Locale locale,
        ref<TProvider> provider) where TProvider : ICultureDataProvider;
}
public struct NumberFormatter
{
    public static bool TryFormat<TWriter>(int32 value, write ref<TWriter> writer,
        TextSpan format, ref<CultureData> culture) where TWriter : ITextWriter;
    public static bool TryFormat<TWriter>(uint64 value, write ref<TWriter> writer,
        TextSpan format, ref<CultureData> culture) where TWriter : ITextWriter;
    public static bool TryFormat<TWriter>(float64 value, write ref<TWriter> writer,
        TextSpan format, ref<CultureData> culture) where TWriter : ITextWriter;
    public static bool TryFormat<TWriter>(decimal64 value, write ref<TWriter> writer,
        TextSpan format, ref<CultureData> culture) where TWriter : ITextWriter;
    public static bool TryParse(TextSpan text, TextSpan format,
        ref<CultureData> culture, init ref<int32> value);
    public static bool TryParse(TextSpan text, TextSpan format,
        ref<CultureData> culture, init ref<uint64> value);
    public static bool TryParse(TextSpan text, TextSpan format,
        ref<CultureData> culture, init ref<float64> value);
    public static bool TryParse(TextSpan text, TextSpan format,
        ref<CultureData> culture, init ref<decimal64> value);
}
public struct LocalDateTime
{
    public int32 Year { get; }
    public uint8 Month { get; }
    public uint8 Day { get; }
    public uint8 Hour { get; }
    public uint8 Minute { get; }
    public uint8 Second { get; }
    public uint32 Nanosecond { get; }
    public static bool TryCreate(int32 year, uint8 month, uint8 day,
        uint8 hour, uint8 minute, uint8 second, uint32 nanosecond,
        init ref<LocalDateTime> value);
}
public struct DateTimeFormatter
{
    public static bool TryFormat<TWriter>(LocalDateTime value,
        write ref<TWriter> writer, TextSpan format, ref<CultureData> culture)
        where TWriter : ITextWriter;
    public static bool TryParse(TextSpan text, TextSpan format,
        ref<CultureData> culture, init ref<LocalDateTime> value);
}
public interface ITimeZoneDataProvider
{
    Result<TimeZoneDatabase, Error> Load();
}
public class TimeZoneDatabase
{
    public Text DataVersion { get; }
    public static Result<TimeZoneDatabase, Error> Load<TProvider>(
        ref<TProvider> provider) where TProvider : ITimeZoneDataProvider;
    public Result<TimeZone, Error> Find(TextSpan id);
}
public class TimeZone
{
    public Text Id { get; }
    public Text DataVersion { get; }
    public Result<LocalDateTime, Error> Convert(Instant instant);
    public Result<LocalTimeMapping, Error> Map(LocalDateTime value);
}
public enum LocalTimeResolution : int32
{
    RejectAmbiguous,
    Earlier,
    Later
}
public class LocalTimeMapping
{
    public LocalDateTime Value { get; }
    public Text ZoneId { get; }
    public Text DataVersion { get; }
    public nuint Count { get; }
    public Instant this[nuint index] { get; }
    public Result<Instant, Error> Resolve(LocalTimeResolution resolution);
}
```

`Locale.TryParse` validates a complete identifier and initializes its output
only on success. A syntactically valid locale does not imply that its data is
available. `CultureData.Load` reports missing/invalid data instead of choosing
the caller's process locale. Its immutable snapshot owns or explicitly retains
all required backing independently of the provider loan. Version labels
identify the selected data, not a promise of interchange compatibility.
Identifier grammar, canonical key equivalence, parent-locale fallback and
data-version selection remain **OPEN**. The key hash must match the chosen
equality; no automatic comparer/root conformance is inferred.

The formatter overloads operate on their actual declared scalar types;
additional widths need their own selected profile. Parsing uses the explicit
format/culture and whole input, returning false with the output vacant on
expected malformed/range failure. These calls cannot change ordinal/invariant
primitive `TryParse` or `TryFormat`. Writer refusal retains already accepted
fragments and does not replay them. Format grammar, grouping, calendars and
rounding remain explicit API/profile decisions, not a silently adopted .NET
format-string catalog or ambient floating-point mode.

`LocalDateTime` is a civil field value, not a UTC instant, offset, duration
or clock-domain timestamp. `TryCreate` must validate the chosen calendar and
field ranges before publication; supported years, the initial Gregorian
profile and leap-second treatment remain **OPEN**, with no `default(T)`
construction of invalid fields. Supporting multiple calendars would require
an explicit calendar identity/relationship, not reinterpreting identical
fields according to a current culture. `DateTimeFormatter` formats that civil value
without secretly selecting a time zone. `Instant` keeps its separately
proposed bounded Unix-nanosecond domain; not every valid civil value must fit it.

The zone database and returned zones retain a particular immutable rule
snapshot. Loading newer rules does not retarget an existing zone or mapping.
`Map` exposes candidate instants in chronological order: zero for a local-time
gap, one for an unambiguous value and multiple for an overlap, without an
arbitrary two-candidate representation limit. The indexed result is copied;
no cursor loan or mutable backing escapes. `Resolve` requires an explicit
policy: reject ambiguity, choose the earliest, or choose the latest candidate.
A gap fails under every listed policy; shifting it to another civil time
would require a separately named operation. Unavailable rules and values
outside the selected instant domain are errors, not an empty successful
mapping. Exact calendar/rule coverage, resource bounds and failure transport
must be specified before adoption; this invents no implicit OS zone provider.

### Capability breadth: files, networking, terminal, globalization, serialization

The complete representative byte-stream/handle and scoped filesystem shapes
above establish the access/ownership pattern. The following is a **proposed
reference inventory** for review breadth, not additional exports from the
sample `library/application.api.terrum`. Candidate namespace assignments are
`Terrum.IO`, `Terrum.Net`, `Terrum.Globalization`, and
`Terrum.Serialization`; moving application services out of `Terrum` requires
an explicit namespace/compatibility decision. No row implies an ambient
singleton, a default encoding, or authority to escape its supplied capability.

| Candidate family | Proposed signature inventory | Substrate / boundary |
|---|---|---|
| `IByteStream`, `ISeekableByteStream` | `Read`, `Write`, `Flush`, async counterparts; seekable adds `GetLength() -> Result<uint64,Error>`, `Seek(int64,SeekOrigin) -> Result<uint64,Error>` | Stream may be nonseekable, partial, owned or borrowed; close and cleanup policy separately specified |
| `IProgressByteStream`, `ByteTransferResult`, `ByteStreamAlgorithms` | Progress-reporting read/write and async counterparts; `ReadExactly`, `WriteAll` and async helpers | Exact accepted prefix on expected failure/cancellation; stronger provider witness, no assumed zero progress or rollback |
| `MemoryByteStream` | Constructor over writable storage and logical length; `Read`, `Write`, `Seek`, `Position`, fixed `Capacity`, async counterparts and `Close` | Exclusive backing loan, no allocation/release ownership or escalation from readonly bytes |
| `BufferedByteStream<TStream>` / **OPEN ownership** | Candidate `Create(TStream, Buffer<uint8>)`, with `TStream : IByteStream`; `Read`, `Write`, `Flush`, `Close` | Exact inner owner/loan constraint is OPEN; ordinary moves cannot prove ownership from a behavioral witness; no implied erased owned-interface representation; flush/close errors remain observable |
| `FileSystem`, `FileHandle` | `OpenRead/Write/Directory`, `GetMetadata`, `FileExists`, `CreateDirectory`, `DeleteFile/EmptyDirectory`, `Rename`; handle `GetLength`, `SetLength`, `Seek`, `Read/Write`, `Flush`, `Close` | Explicit filesystem root; authorized relative path and symlink policy; seekability alone grants no write authority |
| `DirectoryCursor` | `MoveNext() -> Result<bool,Error>`, `Current -> ref<DirectoryEntry>`, `Close()` | Directory capability retained; entry view expires on advance/close |
| `FileMetadata`, `DirectoryEntry` | Metadata `Kind`, `Length?`, `ModifiedTime?`; entry `Name: Text`, `Metadata: FileMetadata` | Metadata is observed state, not atomic with later open; text encoding policy explicit |
| `TcpEndpoint`, `TcpConnection`, `TcpListener`, `NetworkClient` | `ConnectAsync`, capability-qualified `Bind`, `AcceptAsync`, stream I/O and `Close` | Host networking capability; DNS and endpoint-name policy explicit |
| `IpAddress`, `IpEndpoint`, `DatagramSocket`, `DatagramRead` | Numeric `TryParse`/format/key members; capability-qualified `Bind`; `ReceiveFromAsync -> Task<Result<DatagramRead,Error>>`; `SendToAsync -> Task<Result<void,Error>>` | One complete message versus stream progress; truncation, optional original length and peer address reported explicitly |
| `Terminal`, `TextWriter` | `Output`, `Error`, `Write`, `WriteLine`, `WriteLineAsync`; proposed input adapter `Utf8TextReader<TStream>` | Explicit standard-stream host capability; input decoding and output encoding not inferred |
| `Locale`, `CultureData`, `ICultureDataProvider` | `Locale.TryParse`, explicit-provider `CultureData.Load`; immutable locale/data-version properties | Injected versioned data snapshot; no implicit process locale or culture in core parse |
| `NumberFormatter`, `DateTimeFormatter` | Typed `TryFormat<TWriter>(value,writer,format,culture)` and `TryParse(text,format,culture,init value)` | Locale-sensitive APIs explicitly separate from ordinal/invariant primitive formatting; no scalar promotion or implicit zone/calendar |
| `LocalDateTime`, `TimeZoneDatabase`, `TimeZone`, `LocalTimeMapping`, `ITimeZoneDataProvider` | Validated civil `TryCreate`; provider `Load`, zone `Find`/`Convert`/`Map`; mapping `Count`, copied index and explicit `Resolve` | Immutable rule versions, no UTC/civil conflation; gaps/overlaps and instant-domain failure remain explicit |
| `BinaryPrimitives`, `IFixedBinaryCodec<T>`, `UInt32BinaryCodec`, `Int64BinaryCodec` | Closed signed/unsigned 16/32/64-bit byte-order reads/writes; fixed `EncodedLength`, `Encode`, `Decode` | Explicit wire bytes, no native-layout inference; checked vacancy/publication and no retained buffers |
| `IByteEncoder<T>`, `IByteDecoder<T>` | `Encode(ref<T>,write Span<uint8>,init ref<nuint> written) -> Result<bool,Error>`; `Decode(Span<uint8>,init ref<T>,init ref<nuint> consumed) -> Result<bool,Error>` | Explicit schema/version, incremental state, output-publication rules |
| `BinarySerializer<T>`, `TextSerializer<T>` | `Write(ref<T>,write ref<IByteStream>) -> Result<void,Error>`; `Read(write ref<IByteStream>,init ref<T>) -> Result<bool,Error>` | Checked registration/schema/encoding; no reflection on arbitrary objects or hidden allocation |

These rows are signature families, not a generic capability-object base
class. `IByteStream` is a behavioral witness; an erased owned stream requires
an explicit ownership/dispatch representation. `FileSystem` and
`NetworkClient` must be constructed or injected by a host that actually
grants their substrate; a `static File.Open(path)` would bypass that authority.
`Utf8TextReader`/terminal input depends on an explicit decoder and distinguishes
EOF from an empty line. Locale-aware operations cannot change invariant
`TryParse`/`TryFormat` merely by changing process state. Deserialization
checks length/limits, depth, version, references and partial-initialization
cleanup before publishing `T`; schema registration is **OPEN**, not inferred
from type spelling. Incremental codecs must state which prefix has been
consumed or written on error. A writable `Span<uint8>` grants destination
access, not ownership of the stream or permission to retain it indefinitely.

### Application entry

The existing scoped entry direction remains minimal; no `Environment` root or service locator is needed:

```terrum
public static int32 Main(Span<TextSpan> args);
public static Task<int32> MainAsync(Span<TextSpan> args);
```

Accepted entry signatures, program-name exclusion, and argument-root lifetime
are specified in
[language](../specs/language.md#storage-concurrency-entry-and-boundaries).
This inventory's `MainAsync` candidate must not imply that host shutdown
silently cancels or abandons its task; no entry-specific exception is
proposed.
