# Terrum core-library proposal

> [!IMPORTANT]
> Proposal only: signatures and inventories are candidate designs, not
> approved APIs or claims of implementation.

This proposal describes candidate library declarations and dependency layers.
Accepted semantic direction is identified separately from proposed signatures;
exact syntax and several API contracts remain open. Examples do not establish
conformance or implementation. This is not an ABI layout specification.

The approved [source grammar](../grammar-spec.md) and
[approved current semantic IL/metadata design](../il-metadata.md) are separate
documents; neither adopts the signatures here. Related accepted direction is
described in [language](../language.md), [libraries](../libraries.md),
[language contracts](../language-contracts.md), [lifetimes](../lifetimes.md),
[type families](../type-families.md), [conversions](../conversions.md), and
[memory model](../memory-model.md). The design-only
[`library/*.api.terrum`](../../library/) sketches are inventory inputs, not
approved or executable reference assemblies.

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

**Accepted layering direction:** the lowest **`language`** profile specifies
the selected language's compilation/conformance requirements and closed
reserved-primitive binding catalog. **Core** is above that floor: a generally
useful, deliberately scoped surface rather than everything needed for a rich
framework/ecosystem. Distinguish these from a standard application-library/
runtime surface and from feature-specific dependencies. Illustrative
foundational forms include `ref<T>`, `ptr<T>` and `int32`; this is not an
exhaustive required-type or API list. The extensive inventories in this
reference, especially the application services proposed in §13, are not
mandatory for language conformance and do not require a universal object root,
heap, tracing GC, scheduler or OS services solely for the language.

The `language` baseline does not adopt every candidate primitive width or
require every optional feature in every embedded/kernel/application
environment. Its exact inventory, source-version/target mapping and primitive
artifact authority remain explicit profile/build work. A designated Core
compilation may attach checked APIs to selected primitive identities without
changing their representation or authoring source primitive operators;
merely omitting standard-library references grants no such authority.

The **Core**, **Foundation** and **Application** headings remain proposed library
dependency layers above the language floor, not standardized runtime-profile names. Features
that need runtime facilities retain those dependencies and their checked
contracts when used. An environment without a required facility cannot claim
that feature by silently weakening its semantics or substituting an implicit
fallback. This direction allows embedded/kernel environments in principle; it does not
require every environment to support every feature. Exact baseline/API mapping
and feature-to-runtime support remain open.

| Baseline / candidate cumulative layer | Representative surface | Required substrate |
|---|---|---|
| **`language` baseline** | Selected compiler/runtime-owned primitive bindings, representation/reference forms and language-required checking services; not the entire signature inventory below | Explicit selected language/target facilities; ordinary packages cannot redefine intrinsic identity or operations |
| **Core** | Numeric primitives and protocols, `void`/options, `Span<T>`, immutable code-unit/text views, static eager algorithms, shape/view and SIMD contracts | Checked value execution, bounds/access and cleanup/failure services; no ordinary-path allocation or OS facility |
| **Foundation** | Explicitly owned buffers/vectors/maps, allocators/arenas/pools, owning native `Text`, persistent tensor-shape metadata and storage leases | Core and a caller/host-supplied allocator; no mandatory tracing collector |
| **Application** | Managed list/linked list/dictionary/set/queue, ordinary managed `Text`, tasks, streams/terminal, files/network, globalization and serialization | Foundation plus tracing and appropriate host services |
| **Platform leaves** | COM/Windows/DirectX and other native bindings | Only layers their adapters actually use; no reverse dependency on platform APIs |

## Reading declarations

- `mut` grants a writable place or receiver; `write` qualifies authority through a reference/view. They are not interchangeable. Readonly is the declaration default.
- `ref<T>` is an ordinary reference/loan. `init ref<T>` names a vacant output destination, not writable access; the approved call-site form is `Create(init value)`. Declaration-specific conditional publication clauses and their metadata remain open.
- `write Span<T>` lends write access to backing slots; `mut Span<T>` only permits descriptor rebinding. `Span<write C>` is separately writable through each retained handle. Generic substitutions preserve their complete qualification.
- `[Copy]`, `[Sized]`, `[ManagedStorable]`, `[TextCodeUnit]`, and `[SimdElement]` are checked properties/capabilities, not user assertions inferred from an interface name.
- The approved interface grammar permits an optional first `<this TSelf>` formal, but does not force every protocol to use it. Associated defaults and interface-qualified bindings have approved declaration forms; selecting a witness and conditional family registration still need checked contracts. `.via<I>` and `default<I>` are not arbitrary runtime dispatch or construction: `default<I>` is restricted to the conforming `this` in an interface default body.
- A `params` parameter must be final; its approved production does not include a default expression. Eligible ordinary by-value parameters may have compile-time `[Copy]` defaults under the approved call rules. Neither a defaulted parameter nor `init ref<T>` supplies an uninitialized value to the callee; `init` remains a vacant output until publication. Constructor and `init`-property guarantees belong to contract clauses; mixed property backing and `init` mutability do not turn a readonly receiver into writable storage.
- A signature does not express all dependencies. A returned view retains its backing/root/lease and any shape metadata. Ownership, allocation, checked failure, effect and task-completion rules are stated under the relevant family.

## 1. Fundamental values, presence and outcomes

`void` is the zero-data result type, with value `()`, usable as `func<void>` or `Task<void>`; there is no public `unit` or `never` primitive. There is no universal `default(T)` and no required universal root with `Equals`, `GetHashCode`, or `ToString`.

The accepted semantic direction normalizes optional `T?` substitutions: `T?` with already-optional `T` remains one optional layer. `Option<T>` is a proposed transparent alias for that same normalized presence type, not a nominal `Some(T)` wrapper; alias scope and syntax are approved, while this concrete library export remains open. `Option<void>` still represents absent versus present `()`.

A distinct nominal union can preserve distinctions that optional normalization cannot, including missing versus present-null by making its payload itself optional. These are illustrative type shapes, not approved declaration grammar:

```terrum
// Approved alias declaration form; this particular exported alias is PROPOSED.
public alias Option<T> = T?;
```

| Proposed nominal sum | Cases (not source union syntax) | Distinction |
|---|---|---|
| `Lookup<T>` | `Missing`; `Found(T? value)` | Missing versus a present optional payload, including null |

`Lookup<T>.Missing` remains distinct from `Lookup<T>.Found(null)` when `T` is optional; `Option<T>` itself does not add a second presence state. Niche representations must be declared and validated; no invalid bit pattern, padding, or null encoding is available implicitly. General `init ref<T>` does **not** universally mean false leaves the destination vacant: a declaration must specify initialization/publication outcomes and the checker must verify them. For example, an API contract for `bool TryParse(TextSpan, init ref<T>)` may specify false => vacant and true => initialized; the signature alone does not convey that rule.

Expected domain alternatives should be represented by a status/union/result contract; the proposed `Result<T,E>` shape is defined once in §13. Exceptional failures propagate. `require` can lift a declared false outcome into checked failure, but no general library-wide exception hierarchy/object ABI is selected. Catch/recovery is not implied by `require`.

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
[grammar](../grammar-spec.md) and [conversions](../conversions.md), not to a
fictional shared primitive base class.

## 2. Explicit equality, keying and formatting witnesses

These are opt-in behavior contracts, not automatic primitive or root
conformance. Bodyless interface members require a checked witness; a body
would supply an overridable default. The sketches use the source grammar
without adopting these candidate API declarations.
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
    [Captures("destination")]
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
    [TextTarget("content")]
    public static own<val<Text>> Create<TContent>(TContent content)
        where TContent : IFormattable;
}
```

| **OPEN** conditional family contract | Proposed member/relationship |
|---|---|
| `TextSpan<char8/char16/char32>` | `Length: nuint`; `AsSpan() -> Span<TCodeUnit>`; `AsTextSpan() -> TextSpan<void>`; implements `IContiguousSequence<TCodeUnit>` |
| `TextSpan<void>` | `TryProject<TRequested>(init ref<TextSpan<TRequested>> result) -> bool`, with `TRequested : [TextCodeUnit]`; false leaves output vacant |
| all `TextSpan` members | `IsEmpty`, scalar-ordinal comparisons and `TryFormat`; none promises writable text storage |

Conditional family membership/conformance and registration are **OPEN**; an
ordinary extension lookup cannot establish an interface witness. Typed
erasure and successful projection preserve actual encoding, validation,
access, backing/root/lease and lifetime without copying/transcoding. Mismatch
exposes no typed view. `AsSpan()` drops text validity as a view, never
immutability or backing lifetime; there is no `Span<void>`. Ordinal
comparisons operate over scalar sequences without normalization, culture or
case folding. `EqualsOrdinal(rune)` requires the *whole* view to be precisely
one scalar. The approved character forms are target-typed: `char8` admits
only ASCII as a standalone unit, `char16` a nonsurrogate BMP scalar, and
`char32`/`rune` any scalar. There is no default character-literal type.

`SpanTextWriter` holds an exclusive loan to caller-provided storage; `Written` is accepted code-unit count. A refused fragment does not modify storage for that fragment, but prior fragments remain. `Text.Create` validates/freezes text and may transfer eligible exclusive backing; final representation and builder contract remain open.

### Text/view/writer signature families

| Type / proposed layer | Additional signature inventory | Source/ownership boundary |
|---|---|---|
| `Span<T>` / Core | `Length`; `Slice(start, length) -> Span<T>`; matched-access borrowed index; `IContiguousSequence<T>.AsSpan()` | Slot and descriptor mutability are independent; empty slices retain dependencies |
| `InlineArray<T,N>` / Core | `Length: nuint` (constant `N`); matched-access index; `AsSpan() -> Span<T>` | Fixed inline storage has enclosing-owner lifetime, not allocator ownership |
| `TextSpan<TCodeUnit>` / Core | `IsEmpty`; `EqualsOrdinal(TextSpan/rune)`; `CompareOrdinal(TextSpan)`; `TryFormat(writer, format)` | Validation and encoding survive erasure; no implicit decoding fallback |
| `Text` / Foundation or Application | `AsSpan() -> TextSpan`; `TryFormat(writer, format)`; `Create<TContent>(content) -> own<val<Text>>` | Copy/transfer/lease of backing must be explicit; no mandated native object layout |
| `TextBuilder<TCodeUnit>` / Foundation, **OPEN** registration | `Append(TextSpan)`, `AppendScalar(rune)`, `TryBuild(init ref<own<val<Text>>> output)` | Registered builder, owned/borrowed backing and failure cleanup need checked contracts |
| `TextSearch` / Core | `StartsWithOrdinal`, `EndsWithOrdinal`, `TryIndexOfOrdinal`, `TryLastIndexOfOrdinal` | Scalar offsets; no culture, normalization or sentinel |
| `TextFormatting` / Core | `TryWrite<TWriter,TContent>(write ref<TWriter>, TContent) -> bool` | Typed forwarding, not stringify-to-temporary |
| `ITextWriter`, `SpanTextWriter<TCodeUnit>` / Core | `TryWrite`, `TryWriteScalar`, `TryWriteFormatted`; `Written` for bounded writer | Refusal is per-fragment; no replay after visible partial writes |
| `ITextDecoder<TCodeUnit>`, `ITextEncoder<TCodeUnit>` / Foundation | `Convert`, `Finish`, `Reset` with `TextConversionProgress`; concrete UTF-8 witnesses below | Caller advances by reported code units/scalars; EOF requires `Finish` |
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

```terrum
public mut unique class TextBuilder<TCodeUnit>
    where TCodeUnit : [TextCodeUnit]
{
    public TextBuilder(nuint capacityHint);
    public nuint Length { get; }
    public mut void Append(TextSpan source);
    public mut void AppendScalar(rune value);
    public mut bool TryBuild(init ref<own<val<Text>>> result);
    public mut void Clear();
}

public static class TextSearch
{
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
```

The builder copies or transcodes each fragment into its chosen code-unit
encoding while the fragment is borrowed; it retains no source view. A failed
`TryBuild` leaves `result` vacant and the builder responsible for its storage;
successful publication transfers exactly one owned `Text`. `TextSearch`
returns scalar offsets, not byte offsets; an empty needle matches position
zero (or the source's scalar length for the last match). Ordinal search does
not normalize or fold. Its `TryIndexOfOrdinal`/`TryLastIndexOfOrdinal` false
outcomes leave `scalarIndex` vacant.

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
public struct InlineArray<T, const uint64 N>
    where T : [Sized]
{
    public nuint Length { get; }
    [MatchedAccess] public ref<T> this[nuint index] { get; }
    [MatchedAccess] public Span<T> AsSpan();
}
```

The candidate length formal uses a fixed-width unsigned domain, consistent
with the approved initial constant-generic domains; native-sized `nuint` is
not silently admitted as a constant formal. Choosing `uint64` rather than
another fixed width remains an API proposal. `Length` reports the same
constant `N` in the target's indexing domain. A closed instantiation must
reject if `N` or the checked storage-size product cannot be represented or
supported by that target; it must not truncate the length or defer an invalid
inline layout to runtime. Exact target layout limits and zero-length storage
remain open. Borrowed indexing and `AsSpan` retain the enclosing owner's
lifetime and supplied access; they neither copy arbitrary `T` nor grant write
authority through a readonly owner.

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
`Dispose` here names explicit release on a particular owned lease/provider;
it neither imports .NET `IDisposable` nor implies a universal object finalizer.
Provider cleanup must not invalidate allocations whose release responsibility
was transferred elsewhere.

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

### Storage and memory inventory beyond the sample

| Family / namespace | Proposed signatures or members | Required authority |
|---|---|---|
| `AllocationLease` / `Terrum.Collections` | `ByteLength`, `Alignment`, `Dispose()`; optional checked `SliceBytes(offset, length) -> Span<uint8>` | Owns one allocation/release, not a provider; slicing retains lease |
| `AllocatorContext` / `Terrum.Collections` | `Allocate(bytes, alignment) -> AllocationLease`, `Dispose()`; `TryAllocate(..., init ref<AllocationLease>) -> bool` | Provider must outlive outstanding allocations or explicitly transfer release capability |
| `Buffer<T>` / `Terrum.Collections` | `Capacity`, `Count`, `Adopt`, `Initialize`, `AsSpan`, `GetWriteAccess`, `Clear`, `Dispose` candidate | Initialized prefix versus vacant capacity; unique owner and trace/barrier policy |
| `Vector<T>` / `Terrum.Collections` | `Create`, `Count`, `Capacity`, `Add`, `Reserve`, `AsSpan`, `Clear`, `GetWriteAccess` candidate | Growth retains allocator context, replaces lease transactionally; views prevent relocation |
| `Arena` / `Terrum.Collections`, **OPEN** | `Allocate(bytes, alignment) -> Span<uint8>`, `Reset()`, `Dispose()` | Borrowed slices block reset/disposal; arena does not independently own every returned slice |
| `MemoryPool<T>` / `Terrum.Collections`, **OPEN** | `Rent(length) -> unique MemoryOwner<T>`; owner `AsSpan`, `Dispose` | Return/reuse only after all loans end; pooled bytes do not imply initialized `T` |
| `MemoryPrimitives` / `Terrum` | `AtomicRead/Write`, RMW families, strong CAS/observation, `Fence`, `CompilerFence`, non-temporal read/write/copy/complete, cache writeback/invalidate | See the complete proposed member set and strict representation/order/range constraints above |

The added arena/pool rows are coverage candidates, **not** exports in the
current `foundation.api.terrum`. In particular, neither allocator nor GC
provider is silently global. Native `T` storage is permitted only if its
initialization, alignment, move/copy, cleanup and any tracing requirements
can be met; a `Span<uint8>` is not an arbitrary typed allocation.

## 5. Collections, iteration and eager algorithms

Initial managed collections from `collections.api.terrum` (design-only).
The collection-builder attribute argument below is **UNAPPROVED CANDIDATE
SPELLING**: `typeof(ListBuilder<>)` is outside the adopted expression and
attribute-argument grammar, which specifies neither `typeof` nor open-generic
type arguments.

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
    [ViewOf(Access.Write, "access")]
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

`TryIndexOf` false means no index was published. `Select` preflights destination size and rejects any overlap before callback. `Where` reserves upper-bound capacity, calls predicate once per element, and permits disjoint storage or exact same-start compaction; displaced overlap is rejected before callbacks. Stateful callbacks are mutable borrowed values. Callback failure may leave accepted prefix; no rollback or predicate replay. General lazy/GroupBy/Join surfaces are not inventoried; one bounded sort signature follows.

### Sequence and eager-operation inventory

The three `SpanAlgorithms` declarations above supply the complete
representative signature shapes for a mutable comparer, selector and
predicate. The following **proposed** matrix fills out the protocol family
without repeating a concrete declaration for every element type. `Span<T>`
means a non-owning readonly input; a `write` destination must be explicit.
All algorithms check bounds, destination length and prohibited overlap before
their first callback or write; callback failures are not transactional unless
an operation says otherwise.

| Protocol / type | Proposed members and shape | Outcome / ownership |
|---|---|---|
| `ISequence<T, TCursor>` | `GetEnumerator() -> TCursor`; cursor is an explicit type argument | Proposed value/copy protocol below; no universal element-copy guarantee |
| `ICursor<T>` | `MoveNext() -> bool`; `Current -> T` where `T:[Copy]`; candidate `Close()` | The concrete copied-value path does not define lending or consuming iteration |
| `IAsyncSequence<T, TCursor>` / `IAsyncCursor<T>` | `GetAsyncEnumeratorAsync() -> Task<TCursor>`; `MoveNextAsync() -> Task<bool>`; `Current -> T` where `T:[Copy]`; candidate `CloseAsync() -> Task<void>` | Each task has a completion owner; borrowed variants remain blocked below |
| `SpanAlgorithms` search | `Contains`, `IndexOf`/`TryIndexOf`, `LastIndexOf`, `Find`/`TryFind`, `CountWhere`, `All`, `Any` | Explicit equality witness or predicate; no unknown-index sentinel |
| `SpanAlgorithms` transform | `Select`, `Where`, `Zip`, `ForEach`, `Fill`, `Reverse`, `CopyTo`, `MoveWithin` | Mutable callbacks take `write ref<TCallback>`; destination/overlap contracts differ by operation |
| `OrderedSpanAlgorithms` aggregate | `Fold<T,TState,TFolder>`; prospective `Sum<T>`, `Min/Max<T,TOrder>` | Initial accumulator/witness explicit; empty min/max return presence, not fabricated `T` |
| `OrderedSpanAlgorithms` order | `Sort<T,TOrder>(write Span<T>,ref<TOrder>)`, `TryBinarySearch<T,TOrder>(Span<T>,ref<T>,ref<TOrder>,init ref<nuint>)` | Ordering witness must define total key policy; sort comparator failures can leave permutation partial |
| `List<T>` / managed | `Count`, index, `TryGet`, `Add`, `Insert`, `RemoveAt`, `Clear`, `GetWriteAccess`, `GetEnumerator` | `[ManagedStorable],[Copy],[Sized]` initial bounds; structural mutation checks all guards |
| `LinkedList<T>` / managed | `First`, `Last`, `AddFirst/Last`, `AddBefore/After`, `Remove`, `Clear`, cursor | Stable node identity, detached node is not attached to two lists |
| `Queue<T>`, `Stack<T>` / managed, **new** | `Count`, `Enqueue/Push`, `TryDequeue/TryPop(init ref<T>)`, `TryPeek(init ref<T>)`, `Clear` | Initial same managed Copy bounds; false output vacant; no `default(T)` |
| `SortedSet<T,TOrder>` / managed, **new** | `Count`, `Order`, `Contains`, `TryAdd`, `Remove`, `Clear`, ascending cursor | Explicit total-order witness; comparison-zero means duplicate |
| `Deque<T>` / owned, **new** | `Count`, `PushFront/Back`, `TryPopFront/Back(init ref<T>)`, `GetSegments()` | Segments are guarded loans, never a fabricated one-span contiguous view |
| `Dictionary<TKey,TValue,TComparer>`, `HashSet<T,TComparer>` | `Count`, comparer, `TryGet`/`Contains`, `TryAdd`, `Remove`, `Clear`, guarded cursor | Explicit key comparer; complete sample and failure contracts in §13 |

#### Proposed sequence and cursor protocol candidates

The following declarations are **PROPOSED** API shapes. The generic
copy-value protocols and concrete `int32` examples describe only copied
elements; they do not require every sequence or collection to store or copy
its items. A cursor is acquired separately from traversal, and acquiring or
constructing an `[Iterator]` producer does not eagerly run its body.

```terrum
public interface ICursor<T>
    where T : [Copy]
{
    public mut bool MoveNext();
    public T Current { get; }
    public mut void Close();
}
public interface ISequence<T, TCursor>
    where T : [Copy]
    where TCursor : ICursor<T>
{
    public TCursor GetEnumerator();
}
public mut unique struct Int32RangeCursor : ICursor<int32>
{
    public mut bool MoveNext();
    public int32 Current { get; }
    public mut void Close();
}
public struct Int32RangeSequence : ISequence<int32, Int32RangeCursor>
{
    public Int32RangeCursor GetEnumerator();
}
[Iterator]
public static Int32RangeSequence Range(int32 start, nuint count);

public interface IAsyncCursor<T>
    where T : [Copy]
{
    public mut Task<bool> MoveNextAsync();
    public T Current { get; }
    public mut Task<void> CloseAsync();
}
public interface IAsyncSequence<T, TCursor>
    where T : [Copy]
    where TCursor : IAsyncCursor<T>
{
    public Task<TCursor> GetAsyncEnumeratorAsync();
}
public mut unique struct Int32AsyncCursor : IAsyncCursor<int32>
{
    public mut Task<bool> MoveNextAsync();
    public int32 Current { get; }
    public mut Task<void> CloseAsync();
}
public struct Int32AsyncSequence : IAsyncSequence<int32, Int32AsyncCursor>
{
    public Task<Int32AsyncCursor> GetAsyncEnumeratorAsync();
}
[Iterator]
public static async Int32AsyncSequence ValuesAsync(int32 start, nuint count);
```

`Range` and `ValuesAsync` are reference signatures for producer result
contracts, not executable bodyless concrete members. Their implementations
would use `yield expression;` and producer `return;`; the sync body starts on
its first advancement, and the async body likewise starts on its first
asynchronous advancement. `ValuesAsync` returns the proposed async-sequence
type directly, **not** `Task<Int32AsyncSequence>`: async producer creation is
lazy, unlike ordinary eager async invocation. The proposed async acquisition
task completes with a cursor but must not start that producer body. Sequence
state, cursor backing and representation are intentionally omitted and
unselected; these signatures do not promise a storage strategy or Copy
capability beyond the explicit `int32` examples and generic constraints.

The `Current` value is available only after a successful advancement; a
subsequent advancement changes the cursor's current position, but a copied
`T` already read is independent of that cursor. `Close` and `CloseAsync` are
candidate checked protocol operations, not `[NoThrow] [Cleanup]` destructors.
Whether either name is registered as the loop-close operation is **OPEN**.
Structured iteration closes its acquired iterator once on exhaustion, `break`, `return`, or
failure; `await for` awaits asynchronous close before completing the exit.
Close failure follows ordinary checked failure/chaining rules. Async
acquisition, advancement and close tasks retain any required sequence,
cursor, backing and affinity dependencies until their completion; a caller
must await them or transfer responsibility to an explicit owner. Cancellation
is only a request and does not release those dependencies or discharge task
completion/error obligations.

Borrowed and consuming forms are deliberately distinct from those
copy-value declarations. The following are **BLOCKED CANDIDATE FORMS**, not
approved API contracts: their signatures alone do not establish an associated
cursor mapping, item lifetime, access mode, invalidation rule, or dependency
metadata.

```terrum
public interface ISequence<T>
{
    public type Cursor;
    public Cursor GetEnumerator();
}
public interface ILendingCursor<T>
{
    public mut bool MoveNext();
    [return: DependsOn("this")]
    public ref<T> Current { get; }
    public mut void Close();
}
public interface IAsyncLendingCursor<T>
{
    public mut Task<bool> MoveNextAsync();
    [return: DependsOn("this")]
    public ref<T> Current { get; }
    public mut Task<void> CloseAsync();
}
public interface IConsumingCursor<T>
{
    public mut bool TryMoveNext(init ref<T> value);
    public mut void Close();
}
public interface IAsyncConsumingCursor<T>
{
    public mut Task<bool> TryMoveNextAsync(init ref<T> value);
    public mut Task<void> CloseAsync();
}
```

The associated `Cursor` spelling uses the grammar's associated-type
declaration form, but its binding and selection contract is not adopted here.
`[return: DependsOn("this")]` is only a dependency-annotation candidate from
[Q20](grammar-open-questions.md#q20-declaring-result-capture-and-backing-dependencies);
it is **UNAPPROVED** and does not itself specify which advancement invalidates
the borrow. An item borrowing cursor state cannot outlive that state or
silently survive a conflicting advance/suspension. A backing-only item loan
instead preserves its declared backing dependency; it is not automatically
shortened to the cursor-state lifetime. Async item loans must
also retain backing and access obligations across each awaitable completion.
The precise producer clause phases and suspension loans remain open
([Q31](grammar-open-questions.md#q31-producer-contract-phases-and-suspension-loans)).

`TryMoveNext(init ref<T>)` and its async counterpart are alternative
consuming-advance candidates: a successful completion would transfer one
element into the vacant output; false would leave it vacant. For the async
form, publication would occur at task completion, not when the task is
returned. Exact advancement, close registration and failure contracts are
unresolved; no consuming or writable binding is implied by ordinary
by-value iteration. Selection among by-value, borrowed and consuming element
modes, including when advancement invalidates an item, remains open
([Q41](grammar-open-questions.md#q41-iteration-element-binding-modes)).

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
    public static bool Any<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate)
        where TPredicate : IPredicate<T>;
    public static nuint CountWhere<T, TPredicate>(Span<T> source,
        write ref<TPredicate> predicate)
        where TPredicate : IPredicate<T>;
    public static bool TryBinarySearch<T, TOrder>(Span<T> source, ref<T> value,
        ref<TOrder> order, init ref<nuint> index)
        where TOrder : IOrderComparer<T>;
    public static void Sort<T, TOrder>(write Span<T> values, ref<TOrder> order)
        where T : [Copy]
        where TOrder : IOrderComparer<T>;
    public static TState Fold<T, TState, TFolder>(Span<T> source,
        TState seed, write ref<TFolder> folder)
        where TFolder : IFolder<T, TState>;
}
public mut class SortedSet<T, TOrder>
    where T : [ManagedStorable], [Copy], [Sized]
    where TOrder : IOrderComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public SortedSet(TOrder order, nuint capacity);
    public nuint Count { get; }
    public TOrder Order { get; }
    public bool Contains(T value);
    public mut Result<bool, Error> TryAdd(T value);
    public mut bool Remove(T value);
    public mut void Clear();
    public SortedSetCursor<T, TOrder> GetEnumerator();
}
public mut unique struct SortedSetCursor<T, TOrder>
    where T : [ManagedStorable], [Copy], [Sized]
    where TOrder : IOrderComparer<T>, [ManagedStorable], [Copy], [Sized]
{
    public mut bool MoveNext();
    public T Current { get; }
}
public mut class Queue<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public Queue(nuint capacity);
    public nuint Count { get; }
    public mut Result<void, Error> Enqueue(T value);
    public mut bool TryDequeue(init ref<T> value);
    public bool TryPeek(init ref<T> value);
    public mut void Clear();
    public QueueCursor<T> GetEnumerator();
}
public mut unique struct QueueCursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool MoveNext();
    public T Current { get; }
}
public mut class Stack<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public Stack(nuint capacity);
    public nuint Count { get; }
    public mut Result<void, Error> Push(T value);
    public mut bool TryPop(init ref<T> value);
    public bool TryPeek(init ref<T> value);
    public mut void Clear();
    public StackCursor<T> GetEnumerator();
}
public mut unique struct StackCursor<T>
    where T : [ManagedStorable], [Copy], [Sized]
{
    public mut bool MoveNext();
    public T Current { get; }
}
```

`Compare` is a readonly witness: negative/zero/positive determine order and
equality; it must remain consistent while values are stored or searched.
`Any` short-circuits on its first match; `CountWhere` visits every element
once. Both use the existing `IPredicate<T>` and retain its explicitly
borrowed callback, without materializing a sequence or replaying earlier
elements.
`TryBinarySearch` requires sorted input, chooses the first equal index, and
leaves `index` vacant when absent. `Sort` may leave a valid permutation if
comparison fails; it never manufactures or duplicates an affine owner.
`Fold` consumes the explicit seed and returns its final state, including for
an empty source. `SortedSet` iterates ascending, treats comparison-zero as
duplicate, and publishes no insertion on allocation failure. `Queue` traverses
oldest first, `Stack` newest first. Their insertion failures preserve contents;
an empty `TryDequeue`, `TryPop` or `TryPeek` leaves the output vacant. Capacity
constructors have the same unresolved checked construction-failure transport
as the keyed collections in §13. A cursor may not be used across structural
mutation; it never outlives its source, and a copied `Current` is available
only after successful advancement. Conditional publication and cursor
registration/close metadata remain **OPEN**; neither follows from these
bodyless signatures.

`[Iterator]` denotes a lazy producer rather than a collection of eager
results; async producers have a distinct awaitable advancement contract.
Neither function-call syntax nor a sequence interface alone authorizes hidden
materialization, replay of a stateful callback, or a captured loan that
escapes its lifetime. Exact close registration, associated-cursor selection,
borrowed dependency annotations and representation remain **OPEN**. A
deterministic snapshot requires an explicit allocated result and policy.
Collection-expression registration is checked per builder, not inferred from
`TryAdd` spelling. The accepted lazy-start, structured-close and
awaited-close rules are in the [grammar's iteration contract](../grammar-spec.md#33-statements-and-local-declarations)
and [producer contract](../grammar-spec.md#512-modifiers-cleanup-and-finalization);
those operational rules do not approve the candidate member names above.

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

**ACCEPTED DIRECTION:** Numeric literals construct directly in their target;
already typed numeric operands do not promote implicitly. Integer arithmetic
is checked by default. Floating conversion follows the canonical
[conversion policies](../conversions.md#conversion-policies). The following
**PROPOSED** reference signatures extend the design-only
[numeric sketch](../../library/numerics.api.terrum), not implemented
primitive exports or a required universal number root. Core's privileged
primitive declarations attach members but cannot author source-defined
primitive operators; operator requirements below describe potential nominal
witnesses for intrinsic operations, not additional primitive operator bodies.

### Independently selectable operation protocols

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
optional scalar composites in the original sketch below complete the
arithmetic/identity shapes. No scalar composite automatically entails
division, bitwise operations, key equality or classification. Vector
operators do not establish scalar classification. Built-in integer operations
retain their accepted language contracts: division truncates toward zero,
division by zero and signed-minimum division by `-1` fail, and signed-minimum
remainder by `-1` is zero. Shift counts are checked; `<<` truncates to the
operand width, `>>` is arithmetic right shift and `>>>` is zero-filling right
shift. **OPEN:** per-type operator/witness eligibility, eligible count types,
heterogeneous operator eligibility and contracts for nominal implementations.
Those choices do not reopen built-in arithmetic. Unary operators do not confer increment/decrement;
ordinary numeric equality is not numeric-key equality or bit identity.
For floats NaN is unordered and `==` leaves NaN unequal.
`IIntegerRelations` is an optional relation proposal; the `uint32` witness
below shows approved `type Name = Type;` bindings. The maximum width need not have
a `Wider` or this witness at all. Relations confer no conversion and do
not infer `T` backward from `T.Wider`; associated equality, inherited
witnesses and metadata remain **OPEN** in [type families](../type-families.md#nominal-witnesses-and-associated-relationships).

### Existing sampled declarations

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

`IScalarNumber` and `IScalarIeee754` are optional composites; universal number protocol is not required. `TOther`/`TResult` may differ from `TSelf`. Vector arithmetic is not scalar classification. Named integer associated types such as `Wider`/`Signed`/`Unsigned` resolve through the selected interface witness, with no reverse inference from a projection. Associated declarations and explicit `.via<I>` witness selection have approved forms; associated-equality constraints and these particular numeric/conversion witness relationships remain open.

`int32.TryAdd/Subtract/Multiply` are checked expected failures; false publishes no output. `int32.FromBits` and `float32.ToBits` reinterpret bits, not numerically convert. Integer arithmetic is checked by default. Bare numeric literals have no default width; target context selects their type. The canonical [conversion policy](../conversions.md#conversion-policies) is: checked integer conversion preserves or throws, unchecked integer conversion wraps to destination-width bits, and saturation is explicit; floating-to-floating conversion follows IEEE-754 without language failure; float-to-int truncates toward zero, then checked conversion throws on out-of-range results or NaN, while unchecked or explicitly saturating conversion clamps out-of-range results and converts NaN to zero, never modulo-narrowing. Floating APIs are target-specific; exact source/destination witness lookup and user-defined declarations remain open.

### Candidate primitive member families

This is a *generative* inventory, not a duplicate declaration for every
width. Each eligible concrete width may have its own same-type arithmetic,
unary, ordinary equality/comparison and identity witnesses from above,
`Zero`/`One`/range members, parse/format members and named checked/wrapping
helpers as applicable. The row does not declare that witness, representation
or ABI; the shaped concrete examples below illustrate substitutions. These
are the [proposed language spellings](../language.md#primitives-strict-arithmetic-and-text),
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

Other proposed signed widths substitute their corresponding unsigned
matching-width bits types (e.g., `int64.FromBits(uint64)`); bit reinterpretation
is *not* `int8(uint8Value)` value conversion. The `Try...` family returns
false with a vacant output for overflow, rather than fabricating zero;
publication proof and compiled outcome metadata are still **OPEN**. An
ordinary checked operator instead uses the selected language failure policy.
For a signed integer, `FromBits` interprets precisely its own two's-complement
width; these examples specify no arbitrary native or foreign layout.

The concrete float/decimal blocks' `KeyEquals`/`GetKeyHashCode` pair equates all NaNs
and signed zeros; decimal numeric cohorts such as `1.0` and `1.00` also
share a key hash. A collection still selects an explicit paired
`IKeyComparer<T>` (§2), not a name-matched `GetHashCode`. Ordinary `==`
leaves NaN unequal and signed zeros equal. **OPEN adjacent to the
classification and stepping candidates:** whether `IsNegative` reads a
sign bit for negative zero and NaNs, the decimal counterpart, signaling-NaN
behavior, `NextUp`/`NextDown` endpoints and zero transitions, and `CopySign`
NaN payload handling. FMA promises one final rounding, not implicit
contraction of separate operators. Matching-width binary `FromBits`/
`ToBits` requires an explicit per-format bit/payload mapping, including
`bfloat16`; `decimal32` has no proposed `FromBits` until BID/DPD, cohorts,
canonical encodings and validity are specified. Decimal is not CLR decimal.

### Numeric conversion and scalar service signatures

The existing `ICheckedConversion<TDestination,TSource>`,
`ISaturatingConversion<...>` and `ITruncatingConversion<...>` requirements
above express independent, selected pairs, not automatic interfaces on
every primitive. `T(value)` is the checked typed-value spelling; literals
parse in the target directly. These illustrative *pair candidates* show
the result type is the destination, not a promoted intermediate:

| Destination/source candidate | `ConvertChecked` | `ConvertSaturating` | `ConvertTruncating` |
|---|---|---|---|
| `uint8 <- int32`; `int32 <- uint32`; `int64 <- int32` | Preserve exact integer or fail if out of range | Clamp to destination limits | Modulo destination 2^N, then signed-interpret if applicable |
| `int32 <- float32`; `uint8 <- decimal32` | Truncate toward zero then range-check; NaN fails | Truncate then clamp; NaN -> zero | Truncate then clamp; NaN -> zero, **never modulo** |
| `float32 <- int64`; `float64 <- float32`; `decimal64 <- decimal32` | IEEE target-format rounding (no failure for inexactness, infinity, underflow) | **OPEN** distinct pair policy | **OPEN** distinct pair policy |

For example, the existing witness shape yields
`static uint8 ConvertChecked(int32 value)` in a `uint8`/`int32` conformance and
`static int32 ConvertTruncating(float32 value)` in an `int32`/`float32`
conformance. There is no generic `FromInt32` family or conversion chain.
Unchecked integer conversion wraps to the destination width; floating to
integer *never* modulo-narrows. Floating destination conversion uses
nearest/ties-even by default without traps. **OPEN:** exact pair eligibility,
source/destination witness lookup, user conversion precedence, cross-format
NaN payloads and explicit alternate-rounding APIs; typed binary-to-decimal
eligibility cannot be inferred from the sample pairs. No conversion protocol
is a bit-reinterpretation protocol, and no declaration here promises
NoThrow.

Proposed closed service signatures make operand/result/count widths
reviewable without pretending that every scalar has every mathematical
function or that a generic service secretly converts operands:

```terrum
public struct BitOperations
{
    public static int32 LeadingZeroCount(uint32 value);
    public static int32 TrailingZeroCount(uint32 value);
    public static int32 PopCount(uint32 value);
    public static uint32 RotateLeft(uint32 value, int32 count);
    public static uint32 RotateRight(uint32 value, int32 count);
    public static uint32 ReverseBits(uint32 value);
    public static int32 LeadingZeroCount(uint64 value);
    public static int32 TrailingZeroCount(uint64 value);
    public static int32 PopCount(uint64 value);
    public static uint64 RotateLeft(uint64 value, int32 count);
    public static uint64 RotateRight(uint64 value, int32 count);
    public static uint64 ReverseBits(uint64 value);
}
public struct Int32Math
{
    public static int32 Abs(int32 value);
    public static int32 Min(int32 left, int32 right);
    public static int32 Max(int32 left, int32 right);
    public static int32 Clamp(int32 value, int32 minimum, int32 maximum);
}
public struct Float32Math
{
    public static float32 Abs(float32 value);
    public static float32 Min(float32 left, float32 right);
    public static float32 Max(float32 left, float32 right);
    public static float32 Clamp(float32 value, float32 minimum, float32 maximum);
    public static float32 Sqrt(float32 value);
    public static float32 Pow(float32 value, float32 exponent);
    public static float32 Exp(float32 value);
    public static float32 Log(float32 value);
    public static float32 Sin(float32 value);
    public static float32 Cos(float32 value);
    public static float32 RoundToIntegral(float32 value);
}
```

`BitOperations` candidates operate on actual bits of the indicated unsigned
width; a parallel `uint8/16/128` or `nuint` overload requires its own
width/target profile. Counts return `int32`, large enough for the proposed
fixed integer widths. **OPEN:** zero-input leading/trailing counts,
rotate-count normalization, native-size overloads and signed-bit handling.
No hidden `uint64` conversion or unsafe bit-layout access is assumed.
`Abs(int32.MinValue)` follows checked overflow.
**OPEN next to math candidates:** invalid `Clamp` bounds need a declared failure contract.
Float `Min`/`Max`/`Clamp` need NaN propagation and signed-zero tie rules,
while `Sqrt`/`Pow`/`Exp`/`Log`/`Sin`/`Cos`/`RoundToIntegral` need special-value,
domain, precision and rounding-mode contracts. Strict target-format
`float32` arithmetic neither reassociates nor contracts implicitly.
Native/approximate variants and reproducible reductions need separate names
and contracts. Decimal transcendental availability, cohorts and rounding
must be separately determined, not cloned from `Float32Math`.

## 7. Fixed SIMD, scalable vectors and tensors

SIMD values are fixed/scalable ABI values; tensors are logical views over
memory. These are different representations. All declarations and inventories
in this section are **proposals**, not adopted APIs, compiler bindings or
runtime exports. A semicolon on a concrete member is the reference-signature
notation used throughout this document: its implementation is omitted; it is
not a new runnable bodyless-declaration form.

### Fixed-width SIMD

A fixed-width vector is a member only after explicit, checked registration.
Neither a type's spelling, width nor apparent mask pairing proves membership;
the checker must validate the registered lane type, width, mask shape,
operators, representation, ABI and effects. Generic fixed-vector algorithms
range over registered members, not all vector-looking types. The exact family
registration and associated-mask spelling remain open; do not read the
inventory below as valid family grammar or infer a nominal `ISimdVector`
witness.

`simd.api.terrum` supplies local design sketches for the `simd64`,
`simd128`, `simd256`, and `simd512` plus matching `simd*mask` names. The
following is a fully shaped **128-bit reference specialization**. `T` remains
an explicitly registered, sized SIMD element, and each listed operation is
available only for the registered element/protocol combinations that support
it. No universal `Number` classification or automatic scalar-to-vector
promotion is implied.

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
order, alignment and fixed-vector ABI remain open.
Masks represent Boolean lane predicates, not numeric vectors of all-one bits.

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

The operator signatures use the existing `IAdditionOperators`,
`ISubtractionOperators`, `IMultiplyOperators`, `IDivisionOperators`,
`IRemainderOperators`, `IUnaryPlusOperators` and `IUnaryNegationOperators`
vocabulary; each specialization must retain the corresponding scalar contract
and exact target-default arithmetic/conversion behavior. Vector comparisons are named
operations returning the registered mask, not `IComparisonOperators` methods
returning scalar `bool`. Widening and narrowing signatures illustrate concrete
lane-count-compatible shapes; a generic associated result relationship is not
specified. A full-vector widening retains both lower and upper results;
neither half alone preserves every source lane.
The two reduction names are only a place to review distinct
strict/relaxed policies. Their exact policy definitions and lane order are
**OPEN**; floating-point reduction, reassociation and contraction must not be
silently selected. See [associated equality, Q14](grammar-open-questions.md#q14-associated-equality-constraints)
before proposing a generic associated-type spelling.

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

`simd<T>` is a separate, genuinely hardware-scalable, initially sizeless
candidate—not an alias for a convenient fixed width, a member of the fixed
family, or a vectorization promise for scalar callback bodies.
`simd.api.terrum` sketches splat, predicated load/store, comparison,
selection and active-lane reductions, but no complete callable declaration
is proposed until a target profile settles scalable lane/mask ABI, storage,
call, return, suspension and spill restrictions. Those source and profile
requirements are **BLOCKED/OPEN**; fixed-width signatures above must not be
reused as evidence that scalable values are storable or passable.
The existing initial profile permits locals, scoped loans and statically
specialized compatible calls, not ordinary fields, heap boxes, arrays or
aggregate payloads. Live values cannot cross suspension or incompatible
vector contexts; the exact target ABI must preserve those restrictions.

### Tensor shapes and views

Tensor APIs describe logical multidimensional views over memory; they are not
SIMD ABI values. Lengths are `nuint`, element strides are signed `nint`, and
shape metadata has a backing/lifetime dependency of its own. A rank-zero shape
has one element. Any zero dimension makes the count zero without multiplying
other dimensions or evaluating `(length - 1)` address terms. For nonempty
shapes, check signed offset/address bounds. Writable views require injective
index-to-address mappings; broadcasting that aliases logical indexes is
readonly. A `mut` descriptor alone never grants write access.

The members present in `tensors.api.terrum` remain proposed reference
signatures. Creation/result backing syntax is **OPEN**: a shape must preserve
its own metadata lifetime, and a returned view must preserve both storage and
shape dependencies. The following signatures are inventory shapes wherever
they need those unresolved dependency annotations; none selects new syntax.
See [Q20 on backing dependencies](grammar-open-questions.md#q20-declaring-result-capture-and-backing-dependencies).

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

`TryCreate` is an inventory shape only: whether it copies metadata into
shape-owned storage or retains explicitly supplied backing, and how that
dependency is expressed, remain unresolved. `Slice` bounds-checks origin and
lengths; `Transpose` validates a permutation; `Reshape` requires a dense-layout
proof and equal element count. `BroadcastTo` is deliberately readonly.
`TryAsDenseSpan` succeeds only when the view is representable as one
contiguous span, and its result retains the view's backing dependency. The
multi-source lifetime notation for `View` is also open.

`CopyTo` requires equal logical shapes and preflights shape, extent and
aliasing before writing. It permits disjoint backing or exactly identical
per-index mappings (a no-op); displaced or differently-strided overlap is
rejected, with no hidden scratch allocation. Copies require `[Copy]`
independently of view capability. Elementwise operations likewise preflight
shape, destination extent and alias safety, do not implicitly broadcast or
reduce, and allow disjoint or same-per-index mappings only.

### Tensor operations

The full signatures below show explicit tensor sources and writable
destinations. Protocol constraints stay granular; arithmetic does not imply
`[Copy]`, and no universal numeric classification is required. Reduction
signature names and relaxed-policy definitions remain **OPEN** candidates: empty
sum/dot use additive identity/zero, but floating-point reductions,
reassociation and contraction require separately named policies.
Strict tensor reductions retain the accepted logical row-major order;
the open questions concern exact API naming, cross-width reproducibility
and separately selected relaxed policies, not permission to reorder a strict
reduction.

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

These API signatures remain proposals; strict candidates retain the existing
row-major contract rather than selecting an accelerated reduction policy.
`MatMulStrictCandidate` requires rank-two operands with matching contraction
dimensions and a destination of the explicit result shape; a zero-length
contraction produces the additive identity. `ReduceRowsStrictCandidate` reduces
the last dimension to an explicit destination shape in logical row-major order.
`SumStrictCandidate`, `DotStrictCandidate` and the relaxed floating signatures
must not imply an unrecorded order, contraction or reassociation permission.
No tensor primitive promises SIMD dispatch or hidden scratch storage.

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

Entry shapes: `Main` returning `void`/`int32` or `MainAsync` returning `Task`/`Task<int32>`, with no arguments or `Span<TextSpan> args` (excluding program name). Host arguments and roots last through async completion/structured cleanup.

Terminal/TextWriter are the existing scoped application I/O surface:

```terrum
public enum TerminalOutput : int32 { Standard, Error }
public static class Terminal
{
    [TextTarget("content")]
    public static void Write<TContent>(TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    [TextTarget("content")]
    public static void WriteLine<TContent>(TContent content,
        TerminalOutput output = TerminalOutput.Standard)
        where TContent : IFormattable;
    [TextTarget("content")]
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
    [TextTarget("content")]
    public mut void Write<TContent>(TContent content)
        where TContent : IFormattable;
    [TextTarget("content")]
    public mut void WriteLine<TContent>(TContent content)
        where TContent : IFormattable;
    [TextTarget("content")]
    public mut Task WriteLineAsync<TContent>(TContent content)
        where TContent : IFormattable;
}
```

Standard output is default; `Error` selects stderr. Writes can expose partial progress; async formatting occurs once into operation-owned buffering then drains to completion. Dropping returned task cannot cancel I/O or release captures. `TryWrite` refusal differs from format-contract failure.

## 9. Accepted direction and unresolved choices

Unresolved source-language decisions are collected separately in the
[grammar question proposal](grammar-open-questions.md); this section retains
the library/API choices and their dependencies.

The following separates semantic constraints from source and API decisions
that remain open:

1. **Presence/result:** normalized optional flattening and namespace-level transparent alias syntax/export are approved; whether `Option<T>` is publicly exported is a library decision. A nominal `Missing | Found(T?)` union remains distinct when present-null must differ from missing. `Result<T,E>` and error payload design remain proposed; declaration-specific vacancy/publication contracts must be explicit.
2. **Equality/hash semantics:** clarify how concrete `int32.GetHashCode()` relates to numeric-key hashing; it does not imply a universal root contract. Settle reusable primitive key-comparer declarations.
3. **`init ref<T>` contracts:** semantics and `Create(init value)` call-site spelling are accepted direction; per-API conditional publication clauses and compiled output metadata remain open. These proposed signatures define no executable ABI.
4. **Memory signatures:** checked location/range grammar; legal CAS success/failure order pairs and bitwise-eligible representations; fence coverage/compiler-fence scope; non-temporal/cache safety and target support. Preserve compile-time-known ordering, accepted defaults, lock-free default and checked/wrapping selection.
5. **Collections/elements:** allocator/failure contract, non-Copy/borrowed/affine storage, guard alias/reentrancy enforcement, write-access naming, builder abandonment/error contract, lending iteration.
6. **Numerics:** integer associated-relation declarations and witness lookup; primitive operator/conversion matrix; floating/decimal policy gaps; align key hashing with comparer.
7. **SIMD/tensors:** fixed family/mask syntax, member registration/ABI and scalable restrictions; tensor shape storage/anchor, checked stride math, alias contract and reproducible reduction API.
8. **Tasks/errors/services:** Task members/awaiter/completion ownership representation; exact scheduling/affinity APIs; cancellation; error/exception object/failure ABI; RNG guarantees beyond the sample methods; time and filesystem/network abstractions.
9. **Notation/metadata:** approved generic-default, alias and character grammar must be kept distinct from OPEN family registration/conditional-member and fixed-signature family details. Handler/output contract binding and the relevant public metadata/ABI still need checking.
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
- `IParsable.TryParse(... init ref<T>)` follows the canonical parse contract: true fully initializes, false leaves the output vacant. The library declarations and their compiled conditional-outcome metadata must encode and prove this contract; the signature alone does not.
- The [memory model](../memory-model.md) defines accepted operation families, result forms and ordering; the signatures above propose source forms while exact grammar, target range contracts and some CAS declarations remain open.
- `Task<TResult = void>` uses the approved generic-default syntax; this particular library type and its task members are not finalized. `Task` does not erase an owning `Task<T>` result. Optional normalization is semantic; source `T?` is approved, while exported `Option<T>` is only proposed.
- [Filesystem metadata and traversal](#proposed-filesystem-metadata-and-directory-traversal) propose a terminal-on-error lending cursor and scoped rename operations. Review link traversal, enumeration invalidation, metadata availability/precision and which target rename guarantees are required; these are API/host-profile choices, not newly adopted source syntax.
- [Address/datagram operations](#proposed-address-values-and-datagram-operations) propose family-preserving address keys, capability-qualified binding and whole-message sends. Review numeric address grammar/scope IDs, client/listener capability separation and error consumption reporting before adopting these signatures.
- [Binary values and fixed codecs](#proposed-binary-values-and-fixed-codecs) propose explicit byte-order operations and a bounded fixed-length witness. Review wire profiles, permitted codec effects and conditional publication; fixed-size encoding does not settle incremental schema registration, reflection or general object serialization.
- [Globalization and civil-time providers](#proposed-globalization-and-civil-time-providers) propose immutable versioned data, explicit culture-sensitive formatting and inspectable local-time mappings. Review locale identifiers/fallback, calendar/range/leap-second profiles, zone rule versions and ambiguity policy; these do not alter invariant Core text or clock contracts.
- [Borrowed byte-stream storage](#proposed-borrowed-byte-stream-storage) proposes a fixed-capacity memory stream over an exclusive backing loan. Buffered owning wrappers still need an explicit inner-stream ownership constraint; exact-read/copy helpers need error-progress reporting before they can promise precise consumption.

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
| Conditional output | `bool TryParse(TextSpan, init ref<T> value);` | Preserve false => vacant and success => fully initialized; grammar and guarded outcome metadata must be specified together. `require` is a caller-side lift, not an exception convention. This avoids default fabrication but makes output-flow checking a language/metadata obligation. |
| CAS observation | `CompareExchangeObservation<T> AtomicCompareExchangeObserve<T>(...)`, with `bool Exchanged` and `T Previous`; distinct from success-only `bool AtomicCompareExchange<T>(...)` | Use a named observation record only when callers need both success and the previous bits. Restrict it to copied values with eligible defined representations; do not infer success through user `==`. ABI, validity and order-pair rules remain open. |
| Task responsibility | `Task<TResult = void>` remains a completion result; responsibility belongs to a structured owner/supervisor contract | Do not make possession or dropping of a task silently imply join, cancellation, or cleanup. A separate structured owner is more explicit and permits transfer/supervision, but requires a defined scope/ownership API. The accepted [async execution direction](../language.md#storage-concurrency-entry-and-boundaries) is independent of concurrency and parallelism; exact responsibility APIs remain separate library work. |
| General conversion matrix | `T(value)` for checked; separately declared checked/saturating/truncating source-destination witnesses | Accepted: checked integer-to-integer preserves or throws, unchecked wraps destination-width bits, and saturation is explicit; floating-to-floating conversion follows IEEE-754 with nearest/ties-even default and no language failure for inexactness, infinity, underflow or NaN; float-to-int truncates toward zero, then checked conversion throws on out-of-range results or NaN, while unchecked or explicitly saturating conversion clamps out-of-range results and converts NaN to zero, never modulo-narrowing. Keep floating support target-only; specify witness lookup and APIs separately. |
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
| `TaskOutcome<T>` nominal sum | `Completed(T value)`, `Faulted(Error error)`, `Canceled`; union source grammar OPEN |
| `Task<T>` | Complete representative signature above; method and capture contracts still proposed |
| conditional Copy-only observation | `Task<T>.GetOutcomeCopy() -> TaskOutcome<T>` only when **all** variant payloads are `[Copy]`; relationship syntax OPEN |
| `TaskAwaiter<T>` | Complete representative signature above; continuation registration and error propagation OPEN |
| `CancellationToken` / `CancellationSource` | Complete representative signatures above; token retains request-state dependency and source owns unique request authority |
| `TaskScope` | Complete representative signature above; scope close waits for adopted completion and cleanup |
| `IExecutor` / **new proposal** | `Schedule(func<void>)`, `Yield() -> Task<void>`, `ResumeOn(IExecutor) -> awaitable`; scheduler and continuation affinity remain separately checked contracts |

The approved generic-default form makes bare `Task` exactly `Task<void>` if
this candidate declaration is adopted, not a base type or result erasure.
`Task<T>` is a unique completion owner, not a freely copied `[Copy]` value.
An async call has **already started** inline through its first real
suspension; `TaskScope.Adopt` accepts responsibility for an operation, not a
cold-task `Start`. A named unique task is passed as `scope.Adopt(move task)`;
`move` is a call expression, not a parameter modifier.
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

The accepted execution model starts an async function inline when called and continues until actual suspension. Completed awaits continue inline; incomplete ordinary awaits do not capture the caller's executor or require a worker hop. Affinity-bound functions/callbacks retain their own declared executor through suspension, cleanup and unwinding; independent helpers do not inherit affinity merely from their caller. `Run` schedules an initial invocation and adopts its full async completion without binding every helper continuation or choosing the caller's continuation. An explicit awaitable adapter such as `ResumeOn(executor)` selects the awaiting continuation without rescheduling the producer; an explicit `Yield` may request a scheduling boundary. Executor choice does not bypass transfer, sharing, lifetime, affinity or cleanup checks.

`TaskScope`, adoption, capture, task-handle cleanup and outcome APIs remain
separate library proposals, not an adopted lifecycle contract. Repeated
observation, responsibility ownership, cancellation and retained dependencies
need coherent exact contracts. Any cross-thread scheduling requires captured
values to satisfy explicit transfer/share and lifetime contracts; arbitrary
thread-confined captures cannot be sent to a worker. `Task<T>` is not
automatically `[ThreadShareable]`, and no ambient scheduler waives checks.
Dropping a handle is never cancellation. `CancellationSource` uniquely
controls a shared request; its token retains the source dependency and may
not be default-fabricated. Cancellation is an idempotent, monotonic,
cooperative request; it does not release borrowed buffers or captured
receivers before completion and cleanup. Cross-thread request/observation
guarantees remain a memory/runtime contract to specify.

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
it. These remain library questions, not new grammar or a fallback to assumed
zero progress.

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
closed; actual constructor capture/lifetime metadata is still **OPEN**.

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

`args` excludes the program name and stays rooted through asynchronous completion and structured cleanup. `MainAsync` must be responsible for its task to completion; host shutdown does not silently cancel or abandon it. A no-argument `void`/`int32` `Main` and `Task<void>` `MainAsync` remain valid variants from the canonical language contract.
