# Literal construction and numeric conversion

The target language has one explicit conversion spelling: `T(expression)`.
Syntax-14 implements checked numeric type-call conversions for `int32`,
`uint32`, `int64`, `uint64` and `char16`; prefix casts are no longer parsed,
and attached width suffixes diagnose as `TRM0006`. Bare integer literals remain
contextual and have no fallback type. The bounded executable profile currently
constructs decimal integer literals and does not bind hexadecimal/floating
literals, custom literal parsers or `[ConstEval]`. Numeric operators are
checked by default in every build.

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
Transparent ABI equivalence likewise does not make nominal wrapper types
implicitly convertible in Terrum source.

## Context before construction

A numeric literal has no initial `int32` or `float64` type. An unambiguous
expected type constructs and validates its value directly in that format,
including target-format rounding; decimal literals do not pass through binary64.
An explicit target call uses the same registered parser. Already-typed numeric
values do not implicitly widen or narrow, even when their current value happens
to fit.

```terrum
int32 count = 1234;                       // Context selects the parser.
int32 same = int32(1234);                 // Explicit target, direct parsing.
uint32 unsignedCount = uint32(count);     // Checked typed-value conversion.
int64 wide = int64(count);
uint64 wideUnsigned = uint64(unsignedCount);
char16 codeUnit = char16(count);          // Range checked; not scalar validation.
uint64 mask = uint64(0xFFFFFFFF);         // Target example: hex literal binding is unsupported.
float32 fraction = float32(0.1);          // Target example: floating literals are unsupported.
float64 converted = float64(fraction);    // Target example: floating conversions are unsupported.
var unknown = 1;                          // Error: no type context.
```

The decimal integer and checked type-call examples above are executable; the
hexadecimal and floating-point examples are target-only.

In the target literal design, `T(untypedLiteral)` parses the original token
directly as `T`; it does not construct an implicit `int32`/`float64` first.
`T(typedExpression)` selects a checked conversion and may fail according to
that conversion's range/representation contract. The bounded compiler
implements built-in numeric type-call conversions for `int32`, `uint32`,
`int64`, `uint64` and `char16`; custom conversions still require an explicitly
declared conversion operator or witness, not arbitrary castability. Type-call
binding preserves existing source-function/local shadowing and call
precedence. `new T(arguments)` is distinct nominal construction; there is no
added primitive `new int32(value)` form.

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

## Registered literal parsing and constant evaluation

Literal targets register their ordinary `IParsable<TSelf>`-style parser rather
than using a separate literal-carrier API:

```terrum
[Literal(LiteralKind.Integer), ConstEval]
public static bool TryParse(TextSpan text, init ref<Port> value);
```

The parser consumes the complete unquoted numeric source spelling from a
borrowed immutable `TextSpan`, validates it in the selected target format and
fully initializes on success; `false` leaves the destination vacant. Its
method name is not what registers it. Literal parsing always runs through the
registered compile-time path, including in ordinary runtime code; there is no
runtime-parse fallback for a malformed or out-of-range literal. An ordinary
call to the parser remains available for runtime text. This registration does
not add suffixes, a custom lexer or implicit search through constructor and
conversion intermediates to guess a custom literal target.
The registration may use the ordinary parser directly or a thin source-literal
grammar adapter over its parsing implementation.

`[ConstEval]` permits a declaration to execute in the deterministic,
resource-bounded compile-time evaluator. Constant requirements are distinct:
`const T a = F(...)` requires a constant declaration value, while
`T b = const F(...)` requires only that expression to be constant; `b` is not
thereby a constant declaration. `const(expr)` is just the prefix followed by
grouping. There is no `runtime(...)` opt-out or separate consteval-only
function category. The evaluator has no ambient I/O; bounded local mutation
and temporaries are permitted when checked. It preserves normal target numeric
semantics, validates the emitted constant representation/ownership and records
the producer, evaluator-rule and value-ABI dependencies. A constant requirement
does not alter type checking/overload resolution or prohibit ordinary
optimizer folding.

## Conversion policies

`TDestination(value)` selects the pair's checked numeric conversion, not
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

Character literals remain design-only. The implemented `char16(value)`
conversion checks the code-unit range, not Unicode scalar validity; surrogate
code units are representable and must be validated when decoding text.
Narrowing and signed/unsigned range failures terminate under the fail-fast
policy or propagate under the bounded checked-exceptions policy. These
compiler-generated failures do not imply a general exception-object ABI.

String targets use registered text parsers too. Call forms such as
`utf8z("Terrum")` and `utf16z("Terrum")` illustrate target parsing; they are not
lexer prefixes, and the names are illustrative unless the target types are
defined. A target validates encoding, terminator and embedded-zero policy;
that does not grant pointer lifetime or storage ownership. Literal parsing
borrows immutable source text and does not require an allocated `String`.

Family declarations, generic inference, registration grammar and general
user-defined conversion witnesses remain future binder work. Boolean `require`
and legacy conditional bool/out publication are implemented for the bounded
native value profile. The target `init ref<T>` initialization-loan form
requires a coordinated declaration and call-site migration. The bounded
checked-exceptions policy already supports requirement failures and compact
owning-output cleanup; general exception construction remains unsupported. The parser's
rejection of repeated optional modifiers is independent of those later
conversion protocols.
