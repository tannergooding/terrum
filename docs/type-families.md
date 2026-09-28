# Type-family design

These are accepted semantic directions expressed as design contracts, not
accepted syntax or implemented APIs. The spelling of family declarations,
conditional members, extensions and witnesses remains open; participation
itself requires an explicit family opt-in with declared projections and
constraints. The local pseudo-code below labels relationships rather than
specifying Terrum grammar. Current compiler support for any part of this
document is tracked in [implementation](implementation.md), not restated here.

## Definitions

| Term | Meaning |
|---|---|
| Family | A declared, explicitly opted-in set of related types (its members), together with members' common operations and any distinguished specialization. A shared name or generic arity does not by itself establish membership. |
| Member | One of the concrete types that belongs to a family, checked against the family's declared representation, validity, copying/moving, initialization/cleanup, tracing/write-barrier, access/backing-dependency and lifetime obligations. |
| Nominal witness | A type's explicit, checked declaration that it implements a behavioral contract (an interface). Conformance is never inferred from matching members, matching representation, or extension-member lookup alone. |
| Associated type / relation | A named type member of an interface witness (for example `IInteger<TSelf>.Wider`), bound to a concrete type by the implementing witness and resolved through that witness, not inferred backward from a result. |
| Projection | A checked, explicitly declared conversion between related types (for example an erased family member to/from a typed member, or an original type to an adapter) that preserves validity, representation, access, ownership and lifetime. Equal representation bits do not by themselves establish a projection. |
| Erasure | A specific, declared projection that discards static type information (for example a typed `TextSpan<char8>` to `TextSpan<void>`) while forwarding the member's backing, access and lifetime dependencies unchanged. |
| Adapter | An explicitly declared type that carries another type's representation and implements additional behavioral contracts without changing the original type's own witness set. |

## Generic qualification and substitution

Family substitution preserves the complete qualification chosen by the caller.
A concrete default-readonly type does not erase a caller-supplied `write`
capability from `T`; constraints must state which access is required. `mut` is
not valid as a generic argument qualifier. For example, a family member over
`Span<T>` must distinguish descriptor rebinding (`mut Span<T>`), writable
element slots (`write Span<T>`), and writable contained handles
(`Span<write C>`). A writable-storage contract is invariant; a readonly
projection is not automatically covariant and would need a checked
representation/lifetime/cost proof. Generic `write T` is not a universal
make-mutable transform; representation-aware mutation and its protocol
constraints remain open.
A readonly-only constraint on the complete `T` is distinct from projecting an
unknown qualified `T` through a readonly view; projection does not rewrite the
type argument or discard its original authority.

## Explicit families

A family opts in explicitly to type-argument expansion and declares its members, including any distinguished specialization. A shared name or generic arity does not establish membership. The checker must preserve each member's representation and ABI, validity, copying and moving, partial initialization and cleanup, tracing and write barriers, access and backing dependencies, and lifetime across projections. An erased member may need a representation tag, a checked dictionary or a changed descriptor ABI; none follows automatically from substituting `void`. Generic extension lookup alone cannot establish a member's validity, representation, interface witnesses or lifetime obligations. Family operations must not turn erased or borrowed views into owning values, invent a `default(T)`, or assume that all members are interchangeable. Projection is legal only where the declared relationship and its costs and effects are checked. This is a general facility, not an automatic rule that every `Type<T>` acquires `Type<void>`; `void` is no universal top or bottom type. Explicit adapters may declare extension-interface behavior without changing the original type's witness set; ordinary extension-member lookup is not conformance. No universal root is introduced merely to provide equality, hashing or formatting.

`Task<TResult = void>` has its own existing no-result meaning: `Task` expands to `Task<void>`. It does not thereby opt into an encoding-erasure projection or a general erasure rule. Hiding a result from `Task<T>` would require an explicit ownership, cleanup and observation contract for that result, not just a cast to `Task<void>`. Type defaults alone do not establish family relationships.

## Nominal witnesses and associated relationships

Behavioral conformance is nominal and requires a checked witness; conditional
member guarantees do not themselves establish conformance. Conflicting
type/interface witness claims are rejected rather than selected by import
order. A type declares its ordinary implementations; package import or loading
does not retroactively add a global conformance to an unrelated type. Explicit
adapter types are the intended way to add an extension-interface behavior set
without changing the original type's conformance.

ABI-transparent nominal wrappers and adapters serve different purposes. A
wrapper such as `NeverNegativeInt32` may share `int32` representation while
allowing only nonnegative values; equal bits do not make every `int32` a valid
wrapper or authorize arbitrary reinterpretation. Unused encodings are
potential niches, not an adopted niche encoding. An explicit `Int32Adapter`
may carry `int32` and implement `IExtra` without making `int32` conform to
`IExtra`. The behavior sets may use two dispatch tables/vtables or an
equivalent; the value need not physically store two vtable pointers.

An explicitly declared one-way `Original`-to-`Adapter` projection, potentially
including `List<Original>` to `List<Adapter>`, is a design possibility, not
automatic covariance or a general container reinterpretation rule. A checked
projection must preserve validity, representation, access, ownership, lifetime
and container representation; writable storage remains invariant. A narrowing
invariant is not a total projection just because its payload bits match.
Exact adapter/projection syntax, forwarding, conflicts, dispatch metadata and
eligibility remain specification work. `Blittable` is a separate
fixed-representation promise. Applicable members outrank ordinary extension
members; member-extension qualification syntax is open. Ordinary
extension-member lookup never substitutes for a nominal witness; an explicitly
declared adapter has its own nominal extension-interface witness and leaves
the original type unchanged.

Associated types are named type members of a selected interface witness and
`Self` relationships express relations such as integer `Wider`, `Signed`, and
`Unsigned` types and a SIMD member's element and mask types. For example,
`IInteger<TSelf>` may declare `type Wider;`, with an implementation binding
that member to a concrete type; `T.Wider` is resolved through the selected
`IInteger<T>` witness. A constraint may explicitly bind an associated type,
for example `where T : IInteger<T, Wider = U>`. This does not infer the
original `T` backward from an associated result. If multiple valid witnesses
or relations could supply a projection, the use must explicitly disambiguate
them. These semantic directions are accepted; exact grammar and metadata
remain to be specified. Associated bindings do not replace independent
`[Copy]`, access, ownership, or family constraints.
For a lending iterator, `Item<'a>` is one candidate associated relation; it
must retain the complete item's qualification and its declared backing and
loan dependencies. The exact iterator/item selection and ABI are later library
design, not an inference from the current method body.

## Text views

`TextSpan<TCodeUnit = void>` is **one** family; bare `TextSpan` expands to exactly `TextSpan<void>`. The allowed members are `TextSpan<char8>`, `TextSpan<char16>`, `TextSpan<char32>` and the declared encoding-erased `TextSpan<void>`. `[TextCodeUnit]` describes only the three character types; it does **not** admit `void`. There is no unconditional `where TCodeUnit : [TextCodeUnit]` on the entire family. Here `void` is neither a zero-sized code unit, a default encoding, a top/bottom type nor a raw `Span<void>`.

Every member denotes validated immutable text. An erased view carries the *actual* UTF-8/16/32 encoding together with its validated text, immutable backing, access, root/lease and lifetime dependencies. Erasing a typed view forwards those dependencies without allocating, copying or transcoding. A checked typed projection succeeds only if the recorded encoding matches the requested character width; failure exposes no typed view. The caller cannot get mutable backing from a text view. A typed `AsSpan()` drops text validity as a *view* but retains the immutable backing and dependencies. Only typed members expose `AsSpan()` and `IContiguousSequence<TCodeUnit>`; an erased member exposes neither, and there is no `Span<void>`. `IsEmpty`, ordinal equality/comparison and formatting are common to all members. Ordinal operations compare Unicode scalar sequences across encodings without implicit normalization or locale; formatting retains the existing typed-writer protocol. `EqualsOrdinal(rune)` requires the *whole view* to contain exactly that one scalar, not a matching prefix, normalization or case folding. Thus `operation.EqualsOrdinal('+')` accepts only the ASCII plus operator regardless of backing encoding.

**Local pseudo-code -- conditional and extension syntax provisional:**

```terrum
family TextSpan<TCodeUnit = void> {
    members TextSpan<void>, TextSpan<char8>, TextSpan<char16>, TextSpan<char32>;
    common bool IsEmpty;
    common bool EqualsOrdinal(TextSpan other);
    common bool EqualsOrdinal(rune other); // whole view is exactly one scalar
    common int32 CompareOrdinal(TextSpan other);
    common bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;

    when TCodeUnit : [TextCodeUnit] {
        implements IContiguousSequence<TCodeUnit>;
        Span<TCodeUnit> AsSpan();
        TextSpan<void> AsTextSpan();  // no copy or transcode
    }
    on TextSpan<void> {
        bool TryProject<TRequested>(init ref<TextSpan<TRequested>> result)
            where TRequested : [TextCodeUnit]; // encoding checked
    }
}
```

The existing `IContiguousSequence<T>.AsSpan()` is a read projection, not a writable escape hatch. Retaining an empty/end view does not drop its backing or lifetime obligations. Validation and immutable publication precede either typed or erased exposure; erasure cannot repair an invalid raw code-unit slice.

## Character fragments

A target-typed single-scalar `'x'` literal has **no fallback type**. For `char8` it must be ASCII (one standalone UTF-8 code unit); for `char16` it must be a non-surrogate BMP scalar (one standalone UTF-16 code unit). `char32` and `rune` admit any Unicode scalar. Surrogates and out-of-range values never denote scalars. A text target's character fragment context is `rune`, including where the target writer uses UTF-8 or UTF-16: encoding happens at the writer boundary. `rune : IFormattable` forwards to `ITextWriter.TryWriteScalar(rune)` without constructing temporary `Text`; `SpanTextWriter<TCodeUnit>` checks space for the *entire* encoded scalar before writing. This adds one scalar primitive to the existing typed-writer path, not a matrix of character/text overloads. Literal grammar and final target-typing rules remain open.

## Callable values and tuples

The bounded target families are `func<Args..., R>` (a lifetime-aware,
single-cast callable) and `fnptr<Args..., R>` (a raw target-defined code
pointer), with result last and each concrete signature closed at any supported
arity. A `func` retains its target and declared capture dependencies; it does
not automatically own borrowed state or extend its lifetime. A `fnptr` carries
no independent environment, and its code must outlive use. There is no
multicast callable, custom nominal-delegate declaration, arbitrary type-pack
expansion, full open/closed-by-thin/fat cross-product, fat `fnptr`, or
mandatory no-capture family. Bound callables may use a thin trampoline, but
their receiver dependencies remain explicit. Aliases, including generic
signature aliases, are transparent; nominal wrappers are separate.

Callable labels and tuple element labels are source-view metadata, not ABI
names or nominal identity. A call binds names from the statically visible
callable contract, and named arguments evaluate in source order. Tuples are
inline ordered heterogeneous values with fixed closed shape. Their
copy/uniqueness/borrow/access contracts follow the complete element types;
labels do not change representation, and function arguments are not
implicitly bundled into a tuple or assigned a tuple ABI. There is no
user-defined pack declaration, expansion, indexing, fold, or forwarding.

`fnptr` uses the Terrum calling convention by default; foreign ABI selection
is a checked type-use attribute on its callable entry and follows nested
pointers, aliases, parameters, and results. An illustrative
`[CallConv(NativeCallingConvention.Winapi)] fnptr<...>` uses existing design
vocabulary; exact attribute names/grammar and import/export ergonomics remain
open. No implicit convention casts are allowed. A `func` invocation has the
Terrum callable contract even when an explicit adapter targets another ABI.
Static functions, open instance receivers, and bound instances are distinct:
an open receiver is an explicit argument with access matching the method,
whereas a bound receiver is retained with its lifetime dependencies. The
spelling of a receiver marker remains open. Native callback pointer/context
pairs can be ordinary explicit aggregates with declared lifetimes; C++
member-function pointers are not generally raw code addresses.

## Fixed and scalable vectors

Fixed-width SIMD is a family of explicitly registered *members*, not an obligation for every vector to implement a nominal `ISimdVector` interface or call through a public witness service class. A member provides a concrete width, lane type and corresponding boolean mask shape through checked membership; common static operations are declared by the family and accessed naturally as `TVector.Splat(...)`, `simd128<float32>.Load(...)` and `simd128mask<float32>.First(...)`. Generic fixed-vector algorithms quantify over **any** registered member. Addition takes two operands of the same fully qualified instantiation and produces that instantiation, while comparisons produce its matching mask, mask reductions produce `bool`, and lane reductions may produce the element type. Writable stores require a writable view (`write Span<TElement>`), not a mutable descriptor (`mut Span<TElement>`). Membership checking verifies width, element/mask granularity, operators, ABI and effects; similar names (`simd128` and `simd128mask`) are not evidence of membership. Other unrelated types continue to use granular generic-math protocols such as `IAdditionOperators<TSelf, TOther, TResult>` without becoming SIMD members.

**Local pseudo-code -- family registration and constraint spelling provisional:**

```terrum
fixed_simd_family<TVector, TElement, TMask> {
    each T : [SimdElement], [Copy], [Sized];
    member simd128<T> with simd128mask<T>, width 128;
    // Other valid members register their own concrete width and mask.
    static on TVector {
        TVector Splat(TElement value);
        TVector Load(Span<TElement> source);
        TMask CompareEqual(TVector left, TVector right);
    }
    static on TMask { TMask First(nuint activeLanes); }
}

where TVector : [FixedSimdMember<float32, TMask>],
                IAdditionOperators<TVector, TVector, TVector>
// The selected family witness binds TMask; the spelling is illustrative.
```

The desired user-level constraint is “any fixed vector of `float32`”: its concrete width and matching mask remain statically known for each instantiation. Whether that is written with an associated mask, an explicit `TMask` parameter or another checked family form is open; the displayed spelling is not Terrum grammar.

`simd<T>` remains a separate genuinely hardware-scalable, initially sizeless type with target-specific vector length, mask and ABI. It is not an alias for a convenient fixed width or an automatic member of the fixed-vector family. Scalable-value storage, suspension and calling restrictions must be validated for each target.

## Open questions

The rules above are accepted; the following remain unresolved design work,
not settled contracts:

- Exact declaration syntax and metadata for family opt-in, conditional
  members, extensions, associated-type binding and adapter/projection
  declarations.
- Representation-aware mutation and its protocol constraints for generic
  `write T`.
- Final literal grammar and target-typing rules for character fragments.
- Exact spelling of a bound-receiver marker, and of foreign-calling-convention
  attribute names/grammar.
- Family/associated-member grammar for fixed-vector registration, including
  whether an "any fixed vector of `float32`" constraint is expressed with an
  associated mask, an explicit mask type parameter, or another checked form.
