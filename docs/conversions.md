# Literal construction and numeric conversion

The native profile implements contextual literals and checked casts between
`int32`, `uint32`, `int64`, `uint64` and `char16`. The broader binding/library contracts below
remain design direction, not implemented floating-point or generic execution.
Cast spelling follows C#; conversion-protocol names and declaration lookup
remain provisional. Numeric operators are checked by default in every build.

Representation and access are separate from numeric conversion. The canonical
forms are `ref<T>`, `ptr<T>`, `box<T>`, `val<T>` and `own<T>`; `init ref<T>`
denotes initialization of vacant storage, not general writable access. `write`
qualifies access through a reference/view and neither creates authority nor
changes immutable fields or nominal restrictions. `mut` applies to a place or
receiver and is not a generic type-argument qualifier. A value with `write`
access may be implicitly viewed through the corresponding readonly access path
where that projection preserves representation, provenance, and lifetime; this
is capability attenuation, not a type-wide rewrite. Readonly access never
implicitly upgrades to `write`. Attenuation does not recursively rewrite
nested type arguments or convert writable containers to differently qualified
element types: mutable-storage contracts remain invariant. Other qualifier
changes require an explicitly checked representation and lifetime contract.

## Context before construction

A numeric literal has no initial `int32` or `float64` type. An unambiguous
expected type constructs and validates its value directly in that format,
including target-format rounding; decimal literals do not pass through binary64.
An explicit tag commits the type. Already-typed numeric values do not implicitly
widen or narrow, even when their current value happens to fit.

```terrum
float32 fraction = 0.1;       // Construct directly as float32.
uint32 seed = 0;             // Construct directly as uint32.
var exactWidth = 1i32;       // Tag supplies the type.
var unknown = 1;             // Error: no type context.
float64 widened = exactWidth; // Error: typed numeric conversion must be explicit.
float64 converted = (float64)exactWidth;
float64 tagged = 0.1f32;     // Error: a target cannot retag an existing float32.
```

Resolve known parameter/operand/target types before unresolved literals when
that yields a unique answer. For `F<T>(T left, T right)`, `F(knownInt32, 1)` can
infer `T = int32`; unconstrained `F(1, 2)` cannot guess it. Competing viable
numeric overloads without a unique contextual solution diagnose, rather than
using a default width or an arbitrary ranking based on representable values.
Negative integer literal construction must accommodate the signed minimum;
it must not first overflow by constructing its positive magnitude in that type.

The accepted generic inference order is: explicit type arguments pin their
positions; typed arguments establish generic types; expected-result context
fills only positions still unresolved; then contextual literals and lambdas
are checked. Context cannot retarget typed numeric operands or replace an
established generic type. Thus `int64 x = 50000 * 50000` constructs `int64`
operands, while `int32 a = 50000; int64 y = a * a` requires explicit conversion.

For a schematic `U Convert<T, U>(T value)`, a typed argument establishes `T`;
an expected result can establish `U`, or `Convert<_, int32>(typedValue)` can
pin `U` while inferring `T`. A unique method signature may fill a local
callable hole, as in `func<_, int32> f = ToInt32`. `_` is illustrative, not
settled grammar. Every hole must resolve at the local binding; exported
fields, aliases and parameters cannot retain unresolved holes. Holes are not
wildcards, existential values or higher-kinded types, and inference does not
invert non-injective `T?`, `box<T>` or associated-type projections. Ambiguity
is diagnosed rather than resolved by a constraint choosing an arbitrary
implementer.

Ordinary constructor inference follows the same target rule. In
`new DifferentRandom(new CounterRandom(0), 0xA5A5A5A5)`, the first argument
establishes `TRandom = CounterRandom`; the declared `uint32` seed/mask
parameters give the literals their context. This neither captures
primary-constructor fields nor infers a concrete type from a constraint.
Copy/move rules still apply.

These are target binding rules, not current general compiler support. The
current binder already uses typed operands before expected-result context and
propagates expected `int32`, `uint32`, `int64`, or `uint64` types into untyped
nested arithmetic, so `int64 x = 50000 * 50000` is supported. It rejects
retargeting already-typed `int32` operands and leaves a literal without type
context unresolved.
Ordinary overloaded declarations are rejected with `TRM3003`; generic source
calls require explicit type arguments. Generic signature-based result-type
lookup is implemented, but it does not infer type arguments from arguments or
expected results. General argument/result inference, partial holes and
constructor inference remain unsupported.

## Conversion policies

`(TDestination)value` selects the pair's checked numeric conversion, not
`FromInt32`/`FromUInt32` method families. Checked means validated range and
representation, **not exactness**: `int32` to `float32` may round. Fractional
rounding, range handling and bit reinterpretation are separate decisions.

| Operation | Proposed primitive policy |
|---|---|
| Checked integer to integer | Preserve value or throw on range failure. |
| Checked finite floating to integer | Round fraction toward zero, then check the integer result's range; throw on failure. NaN and infinities throw. |
| Checked integer/floating to floating | Round to nearest, ties to even. A finite value that would round to infinity throws; inexact finite results and underflow to subnormal/zero are allowed. Existing infinities remain infinities; NaNs follow the destination's numeric NaN policy. |
| Saturating to integer | Round a finite fraction toward zero, then clamp to the destination range; infinities clamp to endpoints. NaN throws because it has no ordered endpoint. |
| Truncating to integer | For finite floating input, first remove the fraction toward zero; then reduce the mathematical integer modulo the destination width. Signed destinations interpret those bits in two's complement. NaN and infinities throw. |
| Bit reinterpretation | `FromBits`/`ToBits` preserve the specified representation, including payload bits; this is not numeric conversion. Width, validity and representation must match the declared operation. |

The proposed primitive policies above are distinct from IEEE arithmetic
exceptions and do not grant arbitrary user-defined conversions. Binary/decimal
cross-format conversions need a specified direct rounding implementation;
floating-destination saturating/truncating policies are **open**, not aliases
for integer modulo or blanket promises on every numeric type. Exact conversions,
if exposed, need their own contract.

Granular `ICheckedConversion<TSelf, TSource>`,
`ISaturatingConversion<...>` and `ITruncatingConversion<...>` sketches in
[numerics](../library/numerics.api.terrum) let generic algorithms request only a
supported policy for a source/destination pair. Neither a broad number marker
nor the existence of a checked conversion promises the other two. Final
source/destination witness lookup and user-defined operator declaration grammar
remain open; consumers must bind to a published contract, never inspect a body
or silently switch to another policy.

Named wrapping operations remain sufficient for the current sketches.
If `unchecked` is added, it should select a declared wrapping **operator or
conversion lexically**. It must not be dynamically ambient, flow into callees,
disable bounds/niche/lifetime checks, or fall back for a user operator that lacks
the required contract. General saturating-expression/block syntax remains open.
Random counter wrapping and numeric-key bit hashing intentionally keep their
explicit `AddWrapping` and `FromBits`/`ToBits` calls.

## Character intent

The design-only `'x'` notation denotes one Unicode scalar after escape decoding,
not an untyped integer or a UTF-16 default. An expected `rune`/`char32` accepts
any scalar; `char16` accepts a scalar representable by one nonsurrogate code
unit; `char8` accepts only ASCII as one standalone UTF-8 unit. Multicode-unit
characters need text, not a falsely validated standalone code unit. Empty,
multiple-scalar and surrogate character literals diagnose. Without an expected
type, a character literal has no fallback type.

Text-target registration supplies `rune` context for direct character fragments,
including character-valued conditional branches. The typed formatter forwards
to `TryWriteScalar(rune)` on the same writer; callers need not construct `Text`.
This does not make arbitrary raw `char8`/`char16` values valid scalars.
`EqualsOrdinal(rune)` compares the entire text view with that one scalar, so
`'+'` accepts ASCII plus only, not fullwidth plus or a longer operator string.
Changing quotes alone is not evidence of an allocation or throughput improvement.

Character literals remain design-only. The executable cast `(char16)value`
checks the code-unit range, not Unicode scalar validity; surrogate code units
are representable and must be validated when decoding text. Narrowing and
signed/unsigned range failures use the explicitly selected native fail-fast
policy, not catchable conversion exceptions.

Family declarations, generic inference and user-defined conversions remain
future binder work. Boolean `require` and legacy conditional bool/out
publication are implemented for the bounded native value profile. The target
`init ref<T>` initialization-loan form requires a coordinated declaration and
call-site migration; catchable requirement
exceptions and owning-output cleanup are not. The parser's rejection of
repeated optional modifiers is independent of those later conversion protocols.
