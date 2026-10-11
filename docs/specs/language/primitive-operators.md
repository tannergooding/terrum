# Built-in scalar operators

This chapter owns the mandatory scalar operator signatures, operand contexts,
failure domains, and updates. [Complete types](types-and-normalization.md)
supplies normalized identity; [values and execution](expressions-and-statements.md)
supplies categories and result use; [call sequencing](../language-contracts.md#13-evaluation-and-loan-timing)
supplies evaluation and access timing. These are language contracts, not a
particular compiler's operation inventory.

The [Core scalar failure catalog](scalar-failures.md) fixes exact checked
payloads without changing these value domains: OverflowException for
unrepresentable checked numeric results, DivideByZeroException for integer
zero divisors and ArgumentOutOfRangeException for invalid source shift counts.
Floating IEEE results, fail-fast and unsupported physical support remain
distinct.

The [26-scalar catalog](../overview/conformance.md#mandatory-scalar-catalog)
is partitioned below. A letter in a signature ranges over exactly its listed
identities, not an inferred common carrier, user interface, or implementation
family. Every admitted instance obeys the rule, including software arithmetic.

| Set | Identities |
|---|---|
| `I`, integers | `int8`, `int16`, `int32`, `int64`, `int128`, `uint8`, `uint16`, `uint32`, `uint64`, `uint128`, `nint`, `nuint` |
| `F`, floating point | `float16`, `bfloat16`, `float32`, `float64`, `float128`, `decimal32`, `decimal64`, `decimal128` |
| `C`, code units/scalar | `char8`, `char16`, `char32`, `rune` |
| Other scalars | `bool`, `void` |

Fixed integer widths are their named widths, with two's-complement signed
values. Native integer width and signed range come from the selected target,
not the compiler host or the byte size of a reference descriptor. `nint`
does not become `int32`/`int64` when their widths coincide.
The five binary and three IEEE decimal formats retain their own precision,
exponent, subnormal, zero, infinity and NaN contracts. IEEE decimal is not
CLR `System.Decimal`. Storage encoding and procedure-call carriers belong
to the [target ABI](../data-layout-and-abi.md), not overload inference.

## Signatures and availability

`T` in a row is one complete scalar identity from its stated set. Repeated
`T` means the same normalized identity. Value operands must be initialized;
their ordinary reads and effects occur before the operation.

| Operator | Operand contract | Result |
|---|---|---|
| Unary `+` | One `T` in `I` or `F` | Same `T`; identity under ordinary copy/representation rules |
| Unary `-` | One `T` in `I` or `F` | Same `T`; checked integer negation or floating sign negation |
| Unary `~` | One `T` in `I` | Same `T`; width-limited bitwise complement |
| Unary `!` | One `bool` | `bool`; logical complement |
| Binary `+`, `-`, `*`, `/`, `%` | Two values of the same `T` in `I` or `F` | Same `T` |
| Binary `&`, `\|`, `^` | Two values of the same `T` in `I`, or two `bool` values | Same `T`, or `bool` |
| `<<`, `>>`, `>>>` | Data `T` in `I`; integer control operand under the count rule below | Same data `T` |
| `==`, `!=` | Two values of the same one of the 26 scalar identities | `bool` |
| `<`, `<=`, `>`, `>=` | Two values of the same `T` in `I`, `F`, or `C` | `bool` |
| `&&`, `\|\|` | Two `bool` operands, with conditional evaluation | `bool` |
| Prefix/postfix `++`, `--` | Eligible initialized update target of `T` in `I` or `F` | Same `T`; new value for prefix, saved old value for postfix |
| `+=`, `-=`, `*=`, `/=`, `%=` | Eligible initialized update target and RHS of the same `T` in `I` or `F` | Statement completion with logical `void` |
| `&=`, `\|=`, `^=` | Eligible initialized update target and RHS of the same `T` in `I`, or `bool` | Statement completion with logical `void` |
| `<<=`, `>>=`, `>>>=` | Eligible initialized integer update target; integer control operand | Statement completion with logical `void` |

There is no numeric promotion, heterogeneous data arithmetic/comparison,
implicit character arithmetic, Boolean ordering, or ordering/arithmetic on
unit. Small-integer arithmetic stays small: `int8 + int8` produces `int8`,
not `int32`. Equal representation width does not admit `nint + int64`,
`float32 == decimal32`, or a code-unit/integer pair.

Code units and `rune` have equality and ordering, not these arithmetic,
bitwise, shift, or increment operations. An explicit numeric conversion
does not make its source type numeric. Optional values, nominal wrappers,
aggregates, SIMD and pointers need their separate admitted operation
contracts; this table grants no implicit optional lifting or inner covariance.
A generic formal needs its declared operation evidence, not merely Copy or
a primitive specialization chosen by a caller.

Nominal operators retain their declared operand/result and mutation
contracts. A same-spelled operator need not return its operand type or
use these count contexts. Primitive operations cannot be redefined by
ordinary packages or inferred from a Core member's name.

## Operand contexts and evaluation

Typed data evidence establishes the primitive `T`. Other typed data
operands must have that same identity; a surrounding result context cannot
retarget them. A literal can then be constructed directly in that `T`.
With no typed data evidence, an available expected data-result type can
supply `T`; otherwise diagnose the missing type.

```terrum
int64 product = 50000 * 50000;
int32 small = 50000;
int64 checkedLater = int64(small * small);
```

The first product has `int64` operands. The second product remains `int32`
and can overflow before its explicit result conversion. Neither example
uses an implicit numeric promotion. `var sum = 1 + 2` still has no width.
A comparison's Boolean result supplies no numeric data type for `1 < 2`.
The shift control operand does not infer the data operand's type.

Complete target binding precedes literal value acceptance. Parsers and
representable values do not choose between primitive identities.
[Signed number tokens](literal-construction.md#integers-and-based-bit-patterns)
construct their mathematical magnitude directly, including signed minima;
signless based integers retain target-width bit interpretation.
An operator before a grouped or already typed operand is the ordinary
operator below. Thus `int8(-0x80)` constructs -128, while
`-int8(0x80)` performs checked negation of -128 and fails.

Eager operators evaluate the left operand, then the right operand, once,
then perform the operation. An operand failure prevents later operands
and the operation. Boolean `&&` evaluates its RHS only when the LHS is
true; `||` only when the LHS is false. Boolean `&`, `|` and `^` are eager.
Reassociation, removed effects, replay, and introduced implicit transfers
are not permitted.

## Integer arithmetic and bit operations

Default integer addition, subtraction, multiplication, negation and updates
compute the mathematical result and fail if it is outside that same `T`'s
range. Failure publishes no result. Signed negation therefore fails at the
minimum. Unsigned negation is checked `0 - x`: zero produces zero, and
every nonzero operand fails. It neither promotes to a signed type nor wraps.

The separately adopted [Abs member](absolute-value.md) uses that same
signed-minimum boundary: a same-type nonnegative magnitude or checked
overflow with no result. Unsigned Abs is identity, not unsigned negation.
Its ordinary call/signature and explicit-variant requirements do not add
a primitive opcode or an ambient unchecked mode.

Integer division truncates its exact quotient toward zero. A zero divisor
fails. Signed minimum divided by `-1` fails for quotient overflow.
Remainder instead uses the truncating mathematical quotient to obtain
`dividend - quotient * divisor`; it has the dividend's sign when nonzero.
This is a mathematical definition, not a sequence of checked `T` operations:
signed minimum `% -1` is zero without an overflowing quotient.
A combined division/remainder that requires both results may therefore
fail where remainder alone succeeds. Fusion must preserve this distinction.

```terrum
int8 quotient = int8(-7) / int8(3);  // -2
int8 remainder = int8(-7) % int8(3); // -1
uint8 zero = -uint8(0);
```

Bitwise operations use exactly the type's bit width. Signed results
interpret those resulting bits in two's complement; they are not checked
numeric conversion or arithmetic overflow. Complementing `int8(0x0F)`
produces `int8(0xF0)`, which is `-16`, without promoting the operand.

Extent arithmetic using these integers stays checked. A declared bounded
extent wrapper can impose additional validity but does not inherit
primitive operations solely from its carrier.
Explicit wrapping/saturating APIs and admitted policy variants retain
their [separate selection contracts](../conversions.md#conversion-policies).
This default table assigns no variant names or metadata and introduces
no ambient uncheckedness.

## Shift controls and exact width

The data operand has one established integer type `T`. A typed count may
have any type in `I`, retaining that type. An otherwise contextual count
expression receives `int32` context from the built-in control slot.
This is not a general default numeric width or an implicit conversion
between typed counts. A declared nominal shift uses its own signature.

After evaluating both operands, require the exact count value to be
nonnegative and strictly less than the data type's declared bit width.
Failure is an invalid-shift-count failure, not masked execution, modular
conversion, undefined behavior, or a clamp.
The check does not first convert the width into a too-small count type.
For 128-bit data, a typed nonnegative `int8` count is valid even though
`int8` cannot represent the width value 128.

| Operation | Bit/value rule after a valid count |
|---|---|
| `<<` | Shift left, fill low bits with zero, discard bits above the data width, and interpret the remaining bits in `T` |
| `>>` on signed data | Shift right with sign fill; negative odd values round toward negative infinity, not division's truncation toward zero |
| `>>` on unsigned data | Shift right with zero fill |
| `>>>` | Shift right with zero fill, including signed data; retain the original signed/unsigned result type |

Count zero preserves the data value. Negative signed data is valid input
to left shift. Shifted-out bits do not cause arithmetic overflow:
`uint8(0x80) << 1` is zero, and `int8(64) << 1` is `-128`.
`int8(-3) >> 1` is `-2`, while `int8(-3) / int8(2)` is `-1`.
`int8(-3) >>> 1` is `126`.

### Source counts and the scalar IL bridge

The currently assigned [scalar Body shifts](../il-instruction-set.md#operation-encoding-and-normalized-effects)
take `uint32` counts for their supported 32/64-bit integer data. They
do not impose a `uint32` source parameter. Validate the source count
domain first; every valid count in that profile then fits `uint32` and
can be represented exactly. Its conversion must not introduce a different
failure or erase a source count failure.

A 32/64-bit [private native profile](../../implementation/native.md) using `int32` control
operands needs the same source-domain check followed by exact successful-path
representation. Its narrower current source coverage does not restrict the
language count domain or establish that the complete bridge is implemented.

Wrapping a `uint64` count such as `0x1_0000_0001` into `uint32` before
checking would turn an invalid count into 1 and is not a valid lowering.
Checking a negative count through a throwing numeric conversion first
would likewise change the failure category. Operands and checks keep
their source order.

The signed `>>>` operation can use an admitted same-width bit-preserving
unsigned view, zero-filling shift, and bit-preserving result view, if the
selected physical profile supports all of them. Checked unsigned negation
can use typed-zero checked subtraction; the signed-only `CheckedNegate`
opcode does not acquire unsigned semantics.
Other widths/formats need supported representations and execution paths.
Recognition of an atomic type code is not that support. No opcode/kind,
escape, feature ID, or generic numeric witness is allocated by this chapter.

## Floating arithmetic and comparisons

Each arithmetic operation rounds directly to its selected `F` under
round-to-nearest, ties-to-even and that format's IEEE rules. No implicit
binary64 intermediary, extended-precision expression evaluation, FMA,
reassociation, ambient rounding, or flush-to-zero behavior changes its
result. Subnormal values remain part of the format.
Decimal operations obey their IEEE value/exponent rules, not CLR decimal
overflow or scale rules. The selected [Windows x64 storage](../targets/windows-x64-data-layout.md#3-ieee-decimal-bid)
uses BID; physical encoding remains a target rule, not overload inference
or permission to change an observable cohort.
For finite decimal addition/subtraction, the preferred exponent is the
smaller operand exponent; multiplication uses their sum, and division their
difference. IEEE precision/exponent limits determine adjustment and rounding
when that preferred cohort cannot represent the result. An equal-valued
cohort is not permission to select arbitrary observable bits.

Finite overflow can produce infinity; underflow can produce a subnormal or
signed zero. Inexactness, infinity, NaN and floating division by zero do
not raise language arithmetic failures or floating traps.
For example, positive finite 1 divided by negative zero is negative
infinity; zero divided by zero, infinity divided by infinity, and zero
times infinity produce NaN.
Alternate/fused/approximate operations require explicit adopted APIs.

Unary plus is identity under the copy contract. Floating negation toggles
only the representation sign, preserving all other bits, decimal quantum,
NaN payload and quiet/signaling state. It is not subtraction from zero.
The [floating-environment contract](floating-environment.md) requires quiet
NaN results for ordinary two-operand arithmetic, including remainder and the
arithmetic underlying increment/decrement. Runtime sign/payload and the
propagated input choice are unspecified. Admitted constant execution chooses
positive quiet zero-payload results at those NaN-processing operations,
not at copies, identity, sign-only negation or Abs.
There is no ambient language status-flag facility; incidental hardware flags
do not alter the value contract or weaken actual target control-state duties.

Floating equality compares numeric values: positive and negative zero are
equal, and equal-valued decimal cohorts are equal. Any NaN operand makes
`==`, `<`, `<=`, `>` and `>=` false, and `!=` true. Ordered operators
are not an IEEE total-order/bit-order operation.
The [numeric key/hash policies](../libraries.md) may equate NaNs or normalize
cohorts; they do not replace operator equality or allow a compiler to erase
the observed sign of zero.

### Truncating-quotient floating remainder

For finite dividend and finite nonzero divisor, `%` uses the exact
mathematical quotient truncated toward zero and the exact residual
`dividend - quotient * divisor`, represented in the same `F`.
It does not use a rounded floating division or intermediate product.
The nonzero result has the dividend's sign and magnitude below the
divisor's magnitude; a zero result has the dividend's sign.
For a finite decimal pair, use the smaller operand exponent for the
residual's cohort, including zero. Aligning the input coefficients to that
exponent and taking the exact integer remainder gives a coefficient that
fits the format: if the divisor exponent is smaller, the residual is
bounded by its original coefficient; otherwise it is bounded by the
dividend's original coefficient. Neither input exponent is outside the
format. This preferred cohort therefore needs no invented promotion,
arbitrary normalization, or quotient-overflow failure.
For example, coefficients/exponents `(550, -2)` and `(20, -1)` give
`(150, -2)`: numeric `5.50 % 2.0` retains the cohort `1.50`.

| Operand classes | Result |
|---|---|
| Either operand NaN | NaN under the arithmetic NaN policy |
| Infinite dividend, or zero divisor of either sign | NaN, not a language division-by-zero failure |
| Finite dividend and infinite divisor | The dividend, including its signed zero |
| Zero dividend and finite nonzero divisor | Zero with the dividend's sign |
| Other finite pair | The truncating-quotient residual above |

Thus `5.5 % 2` gives `1.5` and `-5.5 % 2` gives `-1.5` in a selected
format that represents those values. This is not the nearest-integer
quotient of IEEE remainder; that separately named API would give `-0.5`
and `0.5` in these examples. A finite huge quotient does not make `%`
overflow merely because ordinary floating division would produce infinity.

## Boolean, code-unit, scalar and unit observations

Boolean `!`, eager `&`/`|`/`^`, conditional `&&`/`||`, and equality use
ordinary two-valued truth tables. No Boolean arithmetic, order or
increment is supplied. Nullable Boolean participation needs its separately
supported presence/operation contract; it is not absent-as-false.

Raw code-unit equality/order uses the unsigned unit value, without decoding,
normalization or culture. Raw surrogate `char16` values and non-scalar
`char32` units remain comparable as raw units.
`rune` equality/order instead uses its validated Unicode scalar value.
Different code-unit widths and `rune` do not implicitly compare with each
other or an integer, even when their current numeric values coincide.

Unit equality is true and inequality false after both live unit-producing
expressions have evaluated in order. Zero data does not remove effects,
initialization checks or failures. There is no unit ordering or arithmetic.

## Built-in updates and accessor targets

A scalar update target is either an ordinary writable initialized place,
or a property/indexer with an eligible value getter and ordinary setter
of that same complete scalar type. Getter/setter use does not expose the
getter's private storage or create a reference to its returned value.
A missing, inaccessible, initialization-only or otherwise inapplicable
setter does not supply this update contract. The getter must likewise be
eligible for the actual receiver and indices.

For a compound assignment:

1. Evaluate/capture the target receiver and indices once, retaining ordinary
   access, bounds, backing and root obligations.
2. Read the old initialized value, or invoke the getter once.
3. Evaluate the RHS once, with the destination data type or shift-control
   context as appropriate.
4. Perform the selected primitive operation and its checks.
5. On success, assign the replacement to that same place or invoke its setter
   once. On failure, perform no replacement/setter call.

For a named scalar, `value += RHS()` therefore has this ordinary expansion:

```terrum
int32 old = value;
int32 right = RHS();
value = old + right;
```

The temporary names are explanatory; lowering does not add user-visible
declarations or grant additional target authority.
An eligible property instead captures its receiver/indices and uses its
ordinary getter and setter at those read/store points.
RHS alias effects remain effects: if it changes the target, the saved old
value still supplies the arithmetic operand. An arithmetic failure does
not roll those effects back. A setter's own failure follows its published
contract; there is no universal accessor transaction.

Prefix/postfix increment and decrement use the same target/read/store
sequence, with exact typed 1 and no written RHS. They store only after
checked arithmetic succeeds. Prefix yields the new value and postfix the
saved old value, both in the target's `T`. Floating rounding can make the
replacement equal the old value without deleting an observable accessor
call or otherwise required store.
Bare updates producing a non-unit value still need ordinary result use
or explicit discard; compound assignments remain statement-level updates.

These are not atomic read-modify-write operations, exclusive loans, or a
permission to bypass guards. Concurrency needs its explicit protocol.
Nominal compound operators are not derived from binary operators: a
`write ref<T>` destination still needs an actual eligible place, not a
temporary getter/setter reference adapter. Their declared effects and
invocation timing remain independent of this built-in scalar expansion.

[Conformance cases](../conformance/source-primitive-operators.md) cover the
matrix, exact edge values, count bridge, floating observations and updates.
For comparison, [C# arithmetic](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/arithmetic-operators)
uses promotions and a distinct unsigned unary-minus policy; its floating
`%` likewise differs from nearest-integer IEEE remainder. Those comparisons
do not import C# promotion, count masking, nullable lifting or nominal
instance-operator machinery into Terrum.
