# Literal construction and registration

This chapter owns contextual literal construction, literal-registration
authority and signatures, parser inputs, and mandatory primitive results.
Its logical rule set is `literal-construction/1`. The
[lexical grammar](../lexical-grammar.md#numeric-tokens) owns token boundaries;
[calls and inference](calls-and-inference.md) owns selection before value
acceptance; [constant execution](proof-and-constant-evaluation.md) owns
execution, accounting and frozen-value admission.

No default numeric, character, string, collection or reflection-object type
is introduced. [Handler type recipes](construction-handlers.md), queries and
[constant-formal eligibility](constant-generics.md) have their own contracts;
a literal registration does not grant those capabilities.
This logical rule set assigns no metadata row, feature bit or opcode.

## Target selection

A contextual literal supplies a kind and source input, not an already typed
value. An established expected complete type selects its construction
contract. `T(literal)` supplies that target explicitly. Grouping a literal
retains its contextual category and does not supply a type:
`int8((-128))` constructs the signed literal directly, while
`var value = (-128);` still lacks a target.

Names and callable groups are resolved before applicability. Literal-kind
coverage may filter a candidate's static shape, but neither parsing, range,
rounding, nominal validity nor execution budget selects an overload.
`Choose(int8)` and `Choose(int64)` therefore leave `Choose(300)` ambiguous.
After a unique binding, construction failure diagnoses without retrying
another target, conversion, constructor, name or runtime parser.

An already typed value retains its complete type. `T(typedExpression)` is
an ordinary checked conversion, not literal re-parsing; its private constant
value, original source spelling or optimizer folding does not restore an
untyped literal category. A nominal type without the matching literal
registration does not acquire one through equal layout or a conversion.

A mandatory primitive target call supplies its expected data-result context
to an otherwise untyped primitive expression under the
[operand-context rules](primitive-operators.md#operand-contexts-and-evaluation).
It does not retarget typed data evidence or parse the whole expression as a
literal. Thus `int8(-(128))` requires construction of positive `128` as
`int8` before ordinary negation and rejects that construction, whereas
`int8(-128)` constructs the signed minimum directly.

## Registration authority and identity

The target-defining module owns its registrations. The module may register
its own nominal target, including a checked generic target pattern, but
cannot add, override or withdraw a registration for an unrelated definition.
Primitive targets use their reserved language/Core authority. Import order,
extension lookup, accessibility filtering and a same-named method cannot
change that authority. An owned adapter is the extension route.

The authoritative key is the normalized target pattern and literal kind.
Transparent aliases normalize; nominal definitions and complete argument,
access and representation terms remain distinct. A generic pattern contains
only its owning target definition and that definition's declared formals.
It is checked for every admitted substitution under its declared constraints;
an unconstrained method formal is not a registration for arbitrary `T`.
Matching is finite forward substitution, not inverse family inference.

Imports of the same record identity deduplicate. Distinct records claiming
the same key reject even when they refer to the same method or look equivalent.
Duplicate claims in one source definition also reject. The minimum supplies
no conditional-record, priority or import-order selector. A separately
admitted conditional-registration extension would also have to establish
nonoverlap; ordinary constraints are not permission to select a competing
record by testing its parser.
One method can implement several kinds through separate nonconflicting
records. Lookup requires the uniquely selected target's supported record,
not a search for a parser that happens to accept the input.

### Checked literal record

The compiler-recognized `Literal` attribute and `LiteralKind` cases are
resolved Core schema identities, not privileged spellings of ordinary
attributes. A matching declaration uses `[Literal(LiteralKind.Integer)]`,
for example. That schema permits repetition for different kinds only and
applies to the target's primary-authored static callable contract.
`[ConstEval]` remains a separate checked capability.

| Field | Required meaning |
|---|---|
| Rule identity | `literal-construction/1`, and the exact resolved attribute/kind schema identities |
| Owner | Target-defining module and target definition identity |
| Target | Complete target or eligible owning-definition pattern |
| Kind | Exactly one of the four cases below |
| Parser | Exact callable identity, substituted signature and access |
| Input recipe | The kind's fixed transformation and immutable text-view contract |
| Output contract | Boolean success publishes one fully initialized valid target; false leaves it vacant |
| Execution | Eligible constant body or a separately admitted intrinsic with exact semantic/cost recipe |
| Value admission | Target validity, frozen-value shape and encoding, with their rule dependencies |

| LiteralKind case | Source forms |
|---|---|
| `Integer` | Decimal, hexadecimal and binary integer number tokens, including an attached leading sign |
| `Floating` | Decimal fraction/exponent and hexadecimal floating number tokens, including an attached leading sign |
| `Character` | Single-quoted text decoding to exactly one Unicode scalar |
| `Text` | Noninterpolated ordinary or raw text after its defined decoding/transforms |

A decimal exponent makes `1e0` a `Floating` token even though its mathematical
value is integral. Hexadecimal `0x1f32` is an `Integer` token, not a suffixed
floating token. Leading signs do not add a fifth kind or change radix.
Interpolated text and collection expressions use construction handlers,
not the `Text` literal parser. Boolean, unit and optional-null notation
retain their intrinsic source contracts and cannot be redirected by these
four registration kinds.

### Signature and eligibility

The following is a signature-only schema example, not a complete function
definition:

```terrum
[Literal(LiteralKind.Integer)] [ConstEval]
public static bool TryParse(TextSpan text, init ref<Port> value);
```

The method name and parameter labels are arbitrary; their exact identities
and ordered roles are recorded. The parser is an accessible static ordinary
member of its target, with no method generic formals, receiver, defaults,
`params`, extra inputs or additional outputs. Its containing target formals
may substitute forward. The first parameter is the readonly by-value
encoding-erased `TextSpan<void>` member; bare `TextSpan` has exactly that
meaning. The second is the exact `init ref<Target>` mode, without writable,
optional-target or representation substitutions. The result is exact `bool`.
An `out` parameter, returned optional, arbitrary delegate or same-layout
carrier does not satisfy this signature.

Registration requires the Boolean outcome contract: true publishes a complete
valid `Target`, false leaves the destination vacant. It does not require
an additional `Pure` attribute: `ConstEval` already implies checked pure-form
support. Parse refusal is a normal Boolean result, not an escaping exception.
Ordinary-form `NoThrow`, totality and successful parsing of every token are
not implied. The initialization contract applies to all normal paths;
ordinary failure and partial construction retain their cleanup rules.
A claimed selected pure form cannot escape a language failure on an admitted
input. Private temporary mutation and construction into the vacant result are
permitted; unguarded external effects violate that promise, and the attribute
cannot make an unsupported operation constant-evaluable.

A literal target must supply its Blittable layout commitment, an admitted
frozen Copy value and complete value-ABI/validity recipe. A parser can support ordinary runtime values
without making identity-bearing objects, borrowed source views or native
resources serializable constants. The kind alone grants no constant-formal
eligibility, ordinary conversion, numeric operators or interface witness.
For an external use the method and its target contract must be accessible;
an inaccessible competing claim is still a registration conflict.

The ordinary parser can be registered directly, or the target can register a
thin source-literal adapter over its parsing engine. Ordinary runtime grammar
need not accept Terrum's separators or based-bit rules. Only the registered
entry point receives the source recipe below. No compiler lookup is performed
through an arbitrary `IParsable` witness or a member named `TryParse`.

## Parser input

Each required construction supplies the complete selected input once, with
no leading/trailing trivia, quoting, implicit terminator or locale conversion.
Success means acceptance of that whole input, not a prefix parse. This is a
contract of the registered operation; a prefix parser must be wrapped in a
whole-input adapter rather than registered with a false whole-input promise.

The borrowed input is immutable validated text with recorded actual UTF-16
encoding and backing lifetime. It does not make UTF-16 the default runtime
text encoding. The descriptor and backing are available for the request and
cannot escape in the frozen result. A parser may make checked typed
projections but cannot obtain writable backing or fabricate source addresses.
The input descriptor/backing need not themselves be emitted constants;
the frozen target result has its own layout/value-codec admission.

### Numeric input

Pass the exact ASCII number-token spelling, including its leading sign if
present, radix prefix, digit separators, decimal point and exponent spelling.
Do not delete separators, lowercase, insert a plus, pad to the target width,
reinterpret an initial sign or synthesize a decimal intermediate.
`0x80`, `+0x80` and `-0x80` remain three different inputs.
Grouping, call punctuation and labels are not part of the input:
`Port((-1))` supplies `-1`, not `(-1)`.

A symbolic prefix outside the number token is an ordinary operator.
`Port(-(1))` does not supply `-1` to a parser; it needs the selected unary
operator and construction of its operand under that operator's context.
The compiler does not strip parentheses and fold that operator into an
untyped literal. A signed literal invokes no user-defined unary `+` or `-`.
A source formatter must preserve this distinction: removing grouping from
`-(1)` to emit `-1` can remove a user-defined operator and change construction
or failure. Arithmetic policy selection does not turn an out-of-range
literal into wrapping construction.

### Text and character input

Ordinary text input is the decoded content after escape interpretation;
raw text input is the content after its explicit fence/framing, indentation
and requested newline transformations. Validate the resulting UTF-16 sequence
as Unicode scalars before exposing `TextSpan`. A surrogate pair produced by
two `\u` escapes is one scalar; an unpaired surrogate result cannot enter
this parser. No additional normalization, case folding, newline conversion,
whitespace trimming or terminator is applied. U+0000 is valid input text;
the target decides whether its own representation permits it.

A character input uses the same immutable view recipe but contains exactly
one decoded scalar. Empty, multiscalar, surrogate and out-of-range character
forms reject before invocation. The selected target checks its own
representability. This is not a numeric conversion of a code point.
Direct character fragments in a registered text handler instead receive
the already selected `rune` context and writer contract.

Different input spellings can therefore give equal text input, such as
`"a"` and `"\u0061"`. Parser diagnostics still refer to the original source
token and its UTF-16 extent, not offsets in a newly invented source file.
Decoded-content offsets and original source offsets are not interchangeable.

## Mandatory primitive coverage

Primitive construction is fixed by this chapter regardless of how Core
implements its registered entry points. An ordinary package cannot replace
these results with a parser of its own.

| Target set | Registered kinds and validation |
|---|---|
| Fixed/native signed and unsigned integers | `Integer`; exact mathematical range or the signless based-bit rule |
| Binary and decimal floating formats | `Integer` and `Floating`; exact magnitude then direct target-format rounding |
| Raw `char8`, `char16`, `char32` | `Integer` within the unsigned code-unit width; `Character` within standalone scalar representability |
| Validated `rune` | `Integer` within the Unicode scalar domain; `Character` containing any scalar |
| `bool`, `void` | Intrinsic Boolean/unit notation, not these registered kinds |

Raw code-unit numeric construction can admit a UTF-16 surrogate or a
non-ASCII UTF-8 unit: `char16(0xD800)` and `char8(233)` are raw units.
Character notation cannot claim they are standalone scalars:
`char16('\uD800')` and `char8('\u00E9')` reject.
`rune(0xD800)` rejects under rune validity, not integer overflow.
The code-unit kinds do not acquire arithmetic from numeric literal input.
`char8(-0)` constructs the ordinary raw unit zero; `-char8(0)` still has no
built-in unary-minus operation.

### Integers and based bit patterns

Decimal integer tokens always denote their exact mathematical signed
magnitude. In a hexadecimal or binary integer token, distinguish the
leading sign from any later spelling:

| Leading sign | Integer-target interpretation |
|---|---|
| Unspecified | Parse the unsigned bits; reject a nonzero bit beyond the target width, then interpret those bits in the target's signed/unsigned representation |
| `+` | Parse the exact nonnegative mathematical magnitude and check the target's numeric range |
| `-` | Parse the exact negative mathematical magnitude and check the target's numeric range directly |

Do not first construct an overflowing positive magnitude in the selected
target. This applies to every signed integer minimum, not just decimal.
No modulo reduction or arithmetic-negation detour applies to an explicitly
signed token. Leading zeroes and `_` separators contribute no significant
bits. Short bit patterns are zero-extended to the target width before signed
interpretation; the top written digit is not automatically a sign bit.
Native targets use the selected target width, never the compiler host width.
Integer negative zero is the ordinary value zero; it is valid in an unsigned
target because its mathematical value is zero.

```terrum
int8 decimalMin = -128;
int8 basedMin = -0x80;
int8 signBit = 0x80;
int8 allBits = 0xFF;
int16 widerBits = 0xFF;
int8 explicitPositive = +0x80;   // Error: +128 is outside int8.
int8 negativeMagnitude = -0xFF;  // Error: -255 is outside int8.
int8 groupedPositive = -(128);   // Error: the int8 operand cannot be +128.
int8 typedNegation = -int8(0xFF);
```

The successful values are respectively -128, -128, -128, -1, 255 and,
for the last declaration, 1. `-(0x80)` instead constructs the signless
int8 operand -128 and then performs checked negation, which fails.
That is an arithmetic failure, not failure to construct `-0x80`.
The ordinary proven-failure warning/static-constant rules still apply.

### Floating magnitudes and formats

Every numeric token in a floating target denotes a mathematical magnitude,
not target-format bits, including signless based integers.
`float32(0xFF)` is 255, and `float32(0x3F800000)` is 1065353216,
not 1. Representation construction requires an explicit bit operation.

After removing separators for the mathematical calculation only, a decimal
token gives coefficient `c` and exponent `q = e - f`, where `e` is the
written exponent or zero and `f` counts fractional decimal digits.
A hexadecimal floating token gives its hexadecimal coefficient multiplied
by `2^(e - 4*f)`; its written `p` exponent is decimal and its fractional
digits are hexadecimal. Binary/hexadecimal integers give an exact integer.
Apply the leading sign, retaining an explicit negative sign for zero.
The parser input itself remains the untouched token.

Round once to the selected target with nearest, ties to even. No
`int32`, `float64`, host decimal or intermediate-format rounding is permitted.
Subnormals are retained. Finite overflow produces signed infinity; tiny
values may produce a subnormal or signed zero. Inexactness, overflow and
underflow are successful floating construction, not parser false.
Malformed syntax and invalid nominal target values still reject.
There are no source numeric tokens for NaN or infinity names.

| Binary format | Precision in bits | Minimum normal exponent | Maximum normal exponent |
|---|---:|---:|---:|
| `float16` | 11 | -14 | 15 |
| `bfloat16` | 8 | -126 | 127 |
| `float32` | 24 | -126 | 127 |
| `float64` | 53 | -1022 | 1023 |
| `float128` | 113 | -16382 | 16383 |

The least positive subnormal is `2^(minimum normal exponent - precision + 1)`.
Zero, finite values and infinity retain the sign of the exact literal.
This does not change the separate arithmetic NaN or bit-copy contracts.

### Decimal preferred cohorts

Decimal construction follows the decimal character-conversion
preferred-exponent model, not an arbitrary equal-value normalization.
For a decimal token the preferred exponent is its written `e - f`;
leading zeroes and separators do not affect it. Integer tokens, including
based integers, prefer exponent zero.

For a hexadecimal floating token there is no decimal spelling scale.
Its Terrum input mapping chooses the shortest exact decimal coefficient:
convert its exact binary rational to a decimal coefficient/exponent, remove
coefficient trailing zeroes, and increase the exponent accordingly.
For exact zero that mapping chooses exponent zero. This mapping is a source
extension rule, not a claim that IEEE defines Terrum's hexadecimal syntax.

| Decimal format | Precision in digits | Minimum adjusted exponent | Maximum adjusted exponent | Quantum exponent range |
|---|---:|---:|---:|---|
| `decimal32` | 7 | -95 | 96 | -101 through 90 |
| `decimal64` | 16 | -383 | 384 | -398 through 369 |
| `decimal128` | 34 | -6143 | 6144 | -6176 through 6111 |

For a nonzero exact magnitude, let `a` be its adjusted decimal exponent:
the integer with `10^a <= magnitude < 10^(a + 1)`. If `a` is above the
format's maximum adjusted exponent, return signed infinity. Otherwise choose
the greater of the preferred exponent and `a - precision + 1`, then clamp
that exponent into the format's quantum range. Round the exact magnitude
divided by `10^q` to an integer coefficient, nearest/ties-even, once.
If it carries to `10^precision`, increase `q` by one and divide that
coefficient by ten; if `q` would exceed the maximum quantum exponent,
the result is signed infinity.

For exact zero, clamp the preferred exponent to the quantum range and retain
the sign. An underflow-rounded zero retains the chosen exponent and sign.
Do not strip trailing zeroes from an otherwise valid result or pad an exact
result to full precision. Increasing an exponent to fit precision, lowering
it to fit the maximum quantum exponent, and rounding at the minimum quantum
are format requirements, not permission to choose another observable cohort.

```terrum
decimal32 first = 1.20;       // Coefficient 120, exponent -2.
decimal32 second = 1.2;       // Coefficient 12, exponent -1.
decimal32 zero = -0.00;       // Negative zero, exponent -2.
decimal32 adjusted = 1e96;    // Coefficient 1000000, exponent 90.
decimal32 subnormal = 1e-101; // Coefficient 1, exponent -101.
```

`first` and `second` compare equal but do not have the same exponent.
The [decimal arithmetic reference conversion](https://speleotrove.com/decimal/daconvs.html#reftonum)
illustrates coefficient/exponent formation from decimal text, including
trailing zeroes and signed zero. Terrum's rules above import no locale,
special-value spelling or trap behavior from that reference.
Physical BID/DPD encoding remains a separate target/value-ABI decision.

## Required execution, publication and dependencies

After unique binding and input formation, perform one required
constant-evaluation request for the selected parser with a vacant destination.
Invocation follows ordinary argument, initialization and cleanup contracts.
Source literal formation is required even inside an unexecuted branch;
it is not runtime code that disappears with that branch.
No result cache, private parser name or unavailable body supplies an
unrecorded intrinsic or reduces semantic work.

On true, independently validate complete initialization, target validity,
frozen Copy ownership and exact result encoding before publishing a constant.
A well-typed output is not permission to persist an invalid rune, invalid
wrapper, borrowed input descriptor or unsupported representation.
On false, diagnose literal rejection without reading the vacant output,
manufacturing zero/null, retrying another registration or invoking runtime
parsing. An escaping parser/check/cleanup failure or exhausted resource
envelope diagnoses the required request under the constant-execution rules.
No separate parser transaction rolls back completed side effects.

Input backing and private evaluator storage remain subject to the same
work, depth, live-state and encoded-result accounting as other required
requests. A separately admitted bulk parser intrinsic needs an exact
versioned semantic and resource recipe; registering its native symbol or
method signature is insufficient. All mandatory primitive formats must have
a supported construction path under their selected target/value semantics.

Record the resolved target, literal kind, registration and method identity,
input recipe, source rule version, reached body/intrinsic dependencies,
selected target and value ABI, actual resource envelope and successful
frozen-value encoding. Same text under a different target, leading sign,
decoder recipe or dependency is not the same request. Replacement must
preserve published registration/output/validity promises; new evaluation
needs its actual body dependencies. Compiler-time parser dependencies do not
automatically become linked runtime dependencies.

Physical registration, constant-program and value codecs are admitted
separately. Existing generic attribute rows or recognized atomic types do not
activate an otherwise unassigned carrier. Missing required source execution
or persisted support is an explicit unsupported requirement before use.
