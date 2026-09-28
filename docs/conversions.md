# Literal construction and numeric conversion

The target language has one explicit conversion spelling: `T(expression)`.
Syntax-14 implemented checked numeric type-call conversions for `int32`,
`uint32`, `int64`, `uint64` and `char16`; prefix casts are no longer parsed,
and attached width suffixes diagnose as `TRM0006`. Syntax-15 adds contextual
hexadecimal integer literals; syntax-16 adds binary literals and digit
separators. Missing hexadecimal and binary digits diagnose as `TRM0007` and
`TRM0008`; malformed separators diagnose as `TRM0009`. Bare integer literals
remain contextual and have no fallback type. The bounded executable profile
constructs decimal, hexadecimal and binary integer literals; floating
literals, custom literal parsers and `[ConstEval]` remain unsupported. Numeric
operators are checked by default in every build.

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
uint64 mask = uint64(0xFFFF_FFFF);        // Hexadecimal integer parses directly as uint64.
int32 hexValue = int32(0x1f32);           // The f32-looking tail is part of the hex value.
int32 signBit = int32(0x8000_0000);       // Based literals construct target-width bits.
int32 allBits = int32(0x__FFFF_FFFF);     // Signed interpretation: -1.
int64 unsigned32 = int64(0xFFFF_FFFF);    // 64-bit interpretation: +4294967295.
int32 binaryBits = int32(0b_11111111_11111111_11111111_11111111); // -1.
int32 groupedDecimal = 1__000;            // Repeated separators between digits are valid.
int32 decimalMin = -2147483648;           // Decimal signed-minimum exception is unchanged.
int32 negatedPattern = -0xFFFF_FFFF;      // Unary minus is checked arithmetic: +1.
float32 fraction = float32(0.1);          // Target example: floating literals are unsupported.
float64 converted = float64(fraction);    // Target example: floating conversions are unsupported.
var unknown = 1;                          // Error: no type context.
```

The decimal, hexadecimal, binary, and checked type-call examples above are
executable; the floating-point examples are target-only. A value-based
conversion still range-checks its source: `int32(uint32(0xFFFF_FFFF))` fails,
while `int64(int32(0xFFFF_FFFF))` sign-extends the typed value to `-1`.
Leading zeroes and separators do not increase a based literal's significant
width; a nonzero bit beyond the destination width is rejected. For example,
`int32(0x1_0000_0000)` is out of range.

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
Negative decimal integer literal construction must accommodate the signed minimum;
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

These constructor-inference rules are target direction, not current general
compiler support. Decimal, hexadecimal and binary integer tokenization and
contextual literal binding are implemented. The bounded binder infers multiple
generic type parameters for eligible top-level function templates over `Copy`
structs, `bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`, and existing
source-class/`ManagedNode` handles in their supported readonly/write and
optional forms. Typed arguments and typed existing `out` destinations establish
their corresponding parameters before expected-result context can fill an
unresolved parameter. Explicit type arguments remain supported. For a Copy-only
`T Identity<T>(T value)`, `int64 result = Identity(42)` establishes `T` from
the expected result before binding the literal. `var result = Identity(42)`
has no such evidence and rejects. `Identity(int32(42))` establishes `int32`
regardless of an outer result context. Result context cannot manufacture a
struct from a numeric literal. The template's definition-site `Copy` and
operator constraints and declared
effects still govern specialization; inference cannot borrow stronger caller
constraints. A typed `write C` argument fixes `T` before result fallback, so an
ordinary permitted result weakening to `C` or supported optional widening can
accept the result without retargeting `T`. Conditional outputs retain existing
exact type and qualification rules. Copying a handle does not grant field,
null-test or operator operations. Scalar specializations support Copy-only
templates, not nominal primitive operator conformance. This does not infer
unique/owning arguments, views, generic classes/structs, managed fields inside
user structs, constructor or method type arguments, ordinary overloads, or
partial `_` holes. The current binder
already uses typed operands before expected-result context and
propagates expected `int32`, `uint32`, `int64`, or `uint64` types into untyped
nested arithmetic, so `int64 x = 50000 * 50000` is supported. It rejects
retargeting already-typed `int32` operands and leaves a literal without type
context unresolved.
Ordinary overloaded declarations are rejected with `TRM3003`. Inferred
specializations remain compilation-local and expose no public generic ABI. Generic
signature-based result-type lookup remains distinct from type-argument
inference. General argument/result inference, partial holes and constructor
inference remain unsupported.

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
`FromInt32`/`FromUInt32` method families. Checked integer-to-integer
conversions preserve the value or fail on destination-range overflow.
Floating-to-floating conversion follows IEEE-754 rather than turning
inexactness, overflow to infinity, underflow, NaN or infinity into a language
failure. Numeric conversion is distinct from bit reinterpretation.

| Operation | Accepted direction |
|---|---|
| Integer to integer, checked | Preserve the value or throw when it is outside the destination range. |
| Integer to integer, unchecked | Reduce modulo 2^N for an N-bit destination (retain the low N bits), then interpret the result in two's complement for signed destinations. This applies across width and signedness changes, including widening conversions; it is not checked conversion. |
| Integer to integer, saturating | An explicitly selected API clamps to the destination range; saturation is not implicit. |
| Floating to integer, checked | Truncate toward zero, then range-check; throw if the truncated result is out of range. NaN has no integer representation and throws; infinities are out of range. |
| Floating to integer, unchecked or explicitly saturating | Truncate toward zero, then saturate out-of-range results to the destination endpoints, including infinities. NaN converts to zero. This is **not** modulo narrowing. |
| Integer/floating to floating | Follow IEEE-754 target-format conversion with default round-to-nearest, ties-to-even. Inexact results, finite overflow to infinity, and underflow to subnormal/zero do not fail; NaN and infinities are ordinary floating values. No floating-point exception traps are enabled. Other rounding modes require explicit APIs/adapters; there is no global floating-point control-word API. |
| Bit reinterpretation | `FromBits`/`ToBits` preserve the specified representation, including payload bits; this is not numeric conversion. Width, validity and representation must match the declared operation. |

Floating-to-floating numeric conversion does not promise fixed NaN payload
preservation or cross-format exactness; bit-preserving `FromBits`/`ToBits`
operations remain distinct. These policies do not grant arbitrary user-defined
conversions or choose witness lookup/declaration grammar. Exact conversion and
other per-pair APIs remain specification work.

Granular `ICheckedConversion<TSelf, TSource>`,
`ISaturatingConversion<...>` and `ITruncatingConversion<...>` sketches in
[numerics](../library/numerics.api.terrum) let generic algorithms request only a
supported policy for a source/destination pair. Neither a broad number marker
nor the existence of a checked conversion promises the other two. Final
source/destination witness lookup and user-defined operator declaration grammar
remain to be specified; consumers must bind to a published contract, never
inspect a body or silently switch to another policy.

Named wrapping operations remain sufficient for the current sketches. When
implemented, lexical `unchecked` selects a declared unchecked variant of an
operator, conversion, or arithmetic function at the use site; `AtomicAdd` is an
example of a function family whose arithmetic policy may be selected
explicitly. Integer-to-integer conversion wraps modulo 2^N for an N-bit destination;
floating-to-integer conversion truncates toward zero and saturates, with NaN
converting to zero. This is not ambient checkedness flowing into arbitrary
callees. Prefer paired checked and unchecked declarations; exact syntax and
metadata remain open, and a compile-time policy argument is one possible
implementation. No unchecked block/parser form is established here. A missing
requested variant does not fall back to checked behavior or another overload.
Only arithmetic overflow behavior and the selected conversion policy change;
bounds, validity, lifetime and other safety checks remain checked. Named
`AddWrapping` and `Try...` APIs such as `TryAdd` remain explicit library
alternatives.
General saturating-expression/block syntax remains open. Random counter
wrapping and numeric-key bit hashing intentionally keep their explicit
`AddWrapping` and `FromBits`/`ToBits` calls. See the
[memory-model arithmetic policy](memory-model.md#arithmetic-policy).

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

Family declarations, general generic inference, registration grammar and general
user-defined conversion witnesses remain future binder work. Boolean `require`
and legacy conditional bool/out publication are implemented for the bounded
native value profile. The target `init ref<T>` initialization-loan form
requires a coordinated declaration and call-site migration. The bounded
checked-exceptions policy already supports requirement failures and compact
owning-output cleanup; general exception construction remains unsupported. The parser's
rejection of repeated optional modifiers is independent of those later
conversion protocols.
