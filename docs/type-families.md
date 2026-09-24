# Type-family design

These are design contracts, not accepted syntax or implemented APIs. The spelling of family declarations, conditional members, extensions, witnesses and custom participation remains open. The local pseudo-code below labels relationships rather than specifying Terrum grammar.

## Explicit families

A family opts in explicitly to type-argument expansion and declares its members, including any distinguished specialization. A shared name or generic arity does not establish membership. The checker must preserve each member's representation and ABI, validity, copying and moving, partial initialization and cleanup, tracing and write barriers, access and backing dependencies, and lifetime across projections. An erased member may need a representation tag, a checked dictionary or a changed descriptor ABI; none follows automatically from substituting `void`. Generic extension lookup alone cannot establish a member's validity, representation, interface witnesses or lifetime obligations. Family operations must not turn erased or borrowed views into owning values, invent a `default(T)`, or assume that all members are interchangeable. Projection is legal only where the declared relationship and its costs and effects are checked. This is a general facility, not an automatic rule that every `Type<T>` acquires `Type<void>`; `void` is no universal top or bottom type.

`Task<TResult = void>` has its own existing no-result meaning: `Task` expands to `Task<void>`. It does not thereby opt into an encoding-erasure projection or a general erasure rule. Hiding a result from `Task<T>` would require an explicit ownership, cleanup and observation contract for that result, not just a cast to `Task<void>`. Type defaults alone do not establish family relationships.

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
    common bool TryFormat<TWriter>(mut ref TWriter writer, TextSpan format)
        where TWriter : ITextWriter;

    when TCodeUnit : [TextCodeUnit] {
        implements IContiguousSequence<TCodeUnit>;
        Span<TCodeUnit> AsSpan();
        TextSpan<void> AsTextSpan();  // no copy or transcode
    }
    on TextSpan<void> {
        bool TryProject<TRequested>(out TextSpan<TRequested> result)
            where TRequested : [TextCodeUnit]; // encoding checked
    }
}
```

The existing `IContiguousSequence<T>.AsSpan()` is a read projection, not a writable escape hatch. Retaining an empty/end view does not drop its backing or lifetime obligations. Validation and immutable publication precede either typed or erased exposure; erasure cannot repair an invalid raw code-unit slice.

## Character fragments

A target-typed single-scalar `'x'` literal has **no fallback type**. For `char8` it must be ASCII (one standalone UTF-8 code unit); for `char16` it must be a non-surrogate BMP scalar (one standalone UTF-16 code unit). `char32` and `rune` admit any Unicode scalar. Surrogates and out-of-range values never denote scalars. A text target's character fragment context is `rune`, including where the target writer uses UTF-8 or UTF-16: encoding happens at the writer boundary. `rune : IFormattable` forwards to `ITextWriter.TryWriteScalar(rune)` without constructing temporary `Text`; `SpanTextWriter<TCodeUnit>` checks space for the *entire* encoded scalar before writing. This adds one scalar primitive to the existing typed-writer path, not a matrix of character/text overloads. Literal grammar and final target-typing rules remain open.

## Fixed and scalable vectors

Fixed-width SIMD is a family of explicitly registered *members*, not an obligation for every vector to implement a nominal `ISimdVector` interface or call through a public witness service class. A member provides a concrete width, lane type and corresponding boolean mask shape through checked membership; common static operations are declared by the family and accessed naturally as `TVector.Splat(...)`, `simd128<float32>.Load(...)` and `simd128mask<float32>.First(...)`. Generic fixed-vector algorithms quantify over **any** registered member. Addition takes two operands of the same instantiation and produces that instantiation, while comparisons produce its matching mask, mask reductions produce `bool`, and lane reductions may produce the element type. Membership checking verifies width, element/mask granularity, operators, ABI and effects; similar names (`simd128` and `simd128mask`) are not evidence of membership. Other unrelated types continue to use granular generic-math protocols such as `IAdditionOperators<TSelf, TOther, TResult>` without becoming SIMD members.

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
// TMask is explanatory until the associated-mask syntax is settled.
```

The desired user-level constraint is “any fixed vector of `float32`”: its concrete width and matching mask remain statically known for each instantiation. Whether that is written with an associated mask, an explicit `TMask` parameter or another checked family form is open; the displayed spelling is not Terrum grammar.

`simd<T>` remains a separate genuinely hardware-scalable, initially sizeless type with target-specific vector length, mask and ABI. It is not an alias for a convenient fixed width or an automatic member of the fixed-vector family. Scalable-value storage, suspension and calling restrictions must be validated for each target.
