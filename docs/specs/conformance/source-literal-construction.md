# Literal construction and registration conformance cases

These cases apply [literal construction](../language/literal-construction.md),
[numeric token boundaries](../lexical-grammar.md#numeric-tokens),
[call selection](../language/calls-and-inference.md), and
[constant execution](../language/proof-and-constant-evaluation.md).
They are specification obligations, not executed compiler tests.

Unless testing admission, assume accessible target-owned records, eligible
pure/constant forms, sufficient resources and Blittable/frozen Copy value-codec admission.
Text-input cases use a recording parser returning an eligible inline value,
not an assumed allocating string or persistable borrowed view.
Hexadecimal floating results below describe value bits only for the named
binary format; decimal results use sign/coefficient/exponent triples,
independently of an unselected BID/DPD physical codec.

## Ownership, records and signatures

| Case | Situation | Required result |
|---|---|---|
| LREG-01 | Target owner registers an exact accessible static bool/TextSpan/init-ref-target method | Admit its unique kind record |
| LREG-02 | Another package registers an unrelated target | Reject unauthorized ownership |
| LREG-03 | Package owns an adapter with its own parser | Permit the distinct adapter registration |
| LREG-04 | A member is named TryParse without the resolved Literal schema | No literal capability |
| LREG-05 | A same-spelled user Literal attribute is applied | No compiler registration |
| LREG-06 | One source target claims the same kind twice | Reject duplicate claims |
| LREG-07 | Same record identity arrives through two import paths | Deduplicate, independent of import order |
| LREG-08 | Distinct imported records claim the same key/method | Reject conflict, not deduplication by contents |
| LREG-09 | A conflicting record is inaccessible at the use site | Conflict remains; access does not choose a winner |
| LREG-10 | An alias names the same target for another same-kind claim | Normalize the alias and reject duplication |
| LREG-11 | Two nominal wrappers share an integer layout | Keep separate target keys and validity |
| LREG-12 | One method has Integer and Floating records | Permit the distinct kinds |
| LREG-13 | A record uses ordinary out instead of init ref | Reject signature |
| LREG-14 | Parser returns Target or Target? instead of bool | Reject signature |
| LREG-15 | Input is a writable view or raw Span<char16> | Reject input signature |
| LREG-16 | Output changes complete target, access or representation | Reject substitution |
| LREG-17 | Parser has an extra optional/default/params input | Reject signature |
| LREG-18 | Parser is an instance member | Reject receiver |
| LREG-19 | Parser has method generic formals | Reject unconstrained per-call registration inference |
| LREG-20 | Owning generic target pattern satisfies every admitted substitution | Permit finite forward substitution |
| LREG-21 | Record claims arbitrary T rather than its owning target pattern | Reject authority/pattern |
| LREG-22 | Conditional target records are claimed without a separate schema | Unsupported minimum; do not choose by conditions or import order |
| LREG-23 | Direct target use cannot access its registered method | Diagnose inaccessible construction |
| LREG-24 | Parser is not ConstEval eligible | Reject required registration/execution capability |
| LREG-25 | Target lacks Blittable/frozen Copy validity or value-codec admission | Unsupported literal result, not inferred from field shape |
| LREG-26 | ConstEval parser annotation is assumed to establish ordinary NoThrow | Pure-form support is implied; ordinary NoThrow is not |
| LREG-27 | Parser method has a native name but no admitted body/cost recipe | Unsupported required execution |
| LREG-28 | A secondary partial authors a changed semantic registration | Reject contract-changing secondary authorship |

## Kind eligibility and unique selection

| Case | Situation | Required result |
|---|---|---|
| LSELECT-01 | `var value = 1;` | Diagnose missing target |
| LSELECT-02 | `var value = -1;` | Diagnose missing target; sign supplies no default width |
| LSELECT-03 | `var value = (-1);` | Grouping supplies no target |
| LSELECT-04 | Expected int32 and Integer token | Select int32 before parsing |
| LSELECT-05 | `F<T>(knownInt32, -1)` can establish T from its typed argument | Complete signed literal as int32 afterward |
| LSELECT-06 | Competing int8/int64 overloads receive `300` | Ambiguous before fit; no int64 preference |
| LSELECT-07 | Competing int8/int64 overloads receive `-0xFF` | Ambiguous before signed-magnitude range checking |
| LSELECT-08 | Selected target's parser rejects | Diagnostic without overload/conversion/constructor fallback |
| LSELECT-09 | Targets' registered parsers have different costs | Costs do not select a signature |
| LSELECT-10 | Token `1e0` reaches an Integer-only target | Reject Floating-kind mismatch even though the value is integral |
| LSELECT-11 | Token `0x1f32` reaches an Integer target | Treat the f32 tail as hexadecimal digits |
| LSELECT-12 | Typed int32 constant is passed to an int64 target | Checked value conversion, not literal retargeting |
| LSELECT-13 | A same-layout nominal target has no registration | No literal construction through layout or ordinary conversion |
| LSELECT-14 | True, unit or optional null notation is supplied | Use its intrinsic source contract, not a numeric/Text hook |
| LSELECT-15 | Interpolated text reaches a Text-only parser registration | No handler capability inferred |
| LSELECT-16 | Bare collection or character literal has no target | No invented List/string/rune fallback |

## Attached signs and lexical boundaries

| Case | Source or situation | Required result |
|---|---|---|
| LSIGN-01 | `-128` | One Integer number token, including sign |
| LSIGN-02 | `+128` | One Integer number token, including sign |
| LSIGN-03 | `+0x80` | One explicitly positive based Integer token |
| LSIGN-04 | `-0x1.p-2` | One Floating token; leading and exponent signs retained |
| LSIGN-05 | `(-128)` | Grouped signed literal; parser input -128 |
| LSIGN-06 | `-(128)` | Unary operator and grouped positive literal; no synthetic -128 input |
| LSIGN-07 | `+(128)` | Unary operator and grouped positive literal; no synthetic +128 input |
| LSIGN-08 | `- 128` or `-/*comment*/128` | Reject broken prefix attachment; do not combine |
| LSIGN-09 | `--1` | Decrement token and positive number, not signed literal -1 |
| LSIGN-10 | `++1` | Increment token and positive number, not signed literal +1 |
| LSIGN-11 | `+-1` or `~-1` | Reject unassigned operator-glyph run |
| LSIGN-12 | Expected int8 expression `-(-1)` | Literal -1, then ordinary negation producing 1 |
| LSIGN-13 | Expected int8 expression `~(-1)` | Literal -1, then bitwise complement producing zero |
| LSIGN-14 | `G<-1>` with eligible int32 const formal | Direct signed literal at generic-opener boundary |
| LSIGN-15 | `G<+0x80>` with eligible int8 const formal | Grammar valid; selected numeric range rejects +128 |
| LSIGN-16 | `G<(-(N))>` | Delimited ordinary constant expression, not a signed token |
| LSIGN-17 | `x - 1` | Spaced binary subtraction |
| LSIGN-18 | `x -1` | Reject missing expression/operator boundary |
| LSIGN-19 | `x-1` | Reject, not an alternate subtraction tokenization |
| LSIGN-20 | Registered nominal target receives `-1` and has no unary operator | Literal handler may accept the signed input |
| LSIGN-21 | Same target receives `-(1)` | Need an ordinary applicable unary operator |
| LSIGN-22 | Parser observes `+0X__00_FF` | Exact original spelling, including sign/case/separators |
| LSIGN-23 | Parser observes `-1_2.30e+4` | Exact source spelling, not normalized decimal text |
| LSIGN-24 | `-10i32` | Reject attached width suffix; sign grants no suffix |
| LSIGN-25 | `-0x` or `+0b` | Malformed based number, not fallback to a name |
| LSIGN-26 | U+2212 precedes ASCII digits | Not an ASCII leading sign |
| LSIGN-27 | Numeric token starts at UTF-16 offset three and spells -128 | Token extent [3,7), including leading sign |
| LSIGN-28 | Bare signed literal is used as an ungrouped member receiver | Same required grouping as any numeric literal |
| LSIGN-29 | Formatter removes grouping to change `-(1)` into `-1` | Do not remove the operator/change literal construction semantics |

## Integer magnitude, bits and scalar validity

| Case | Expression or situation | Required result |
|---|---|---|
| LINT-01 | `int8(-128)` | -128 without constructing +128 |
| LINT-02 | `int8(+128)` | Literal range rejection |
| LINT-03 | `int8(-(128))` | Operand literal rejection before ordinary negation |
| LINT-04 | `int8(0x80)` | -128 from target-width bits |
| LINT-05 | `int8(+0x80)` | Literal range rejection for +128 |
| LINT-06 | `int8(-0x80)` | -128 from explicit signed magnitude |
| LINT-07 | `int8(0xFF)` | -1 from bits |
| LINT-08 | `int8(-0xFF)` | Literal rejection for -255 |
| LINT-09 | `-int8(0xFF)` | Ordinary typed negation produces 1 |
| LINT-10 | `-int8(0x80)` | Ordinary arithmetic overflow; not literal rejection |
| LINT-11 | `int8(-(0x80))` | Signless operand -128, then ordinary arithmetic overflow |
| LINT-12 | `int16(0xFF)` | 255; do not sign-extend from the written digit width |
| LINT-13 | `int16(-0xFF)` | -255 |
| LINT-14 | `int8(0b10000000)` and `int8(-0b10000000)` | Both -128 under distinct input rules |
| LINT-15 | `int8(+0b10000000)` | Reject +128 |
| LINT-16 | `uint8(0xFF)` and `uint8(+0xFF)` | Both 255 |
| LINT-17 | `uint8(-0x01)` | Reject mathematical -1, not wrapping |
| LINT-18 | `uint8(-0)` and `uint8(-0x0)` | Ordinary integer zero |
| LINT-19 | `int8(0x__0000_00FF)` | -1; zeroes/separators add no significant bits |
| LINT-20 | `int8(0x100)` | Reject nonzero bit beyond width |
| LINT-21 | Every fixed signed integer receives its directly signed minimum | Accept exact minimum, independently of compiler host width |
| LINT-22 | `nint(0xFFFFFFFF)` on 32-bit/64-bit targets | -1 / 4294967295 |
| LINT-23 | `nint(+0x80000000)` on 32-bit/64-bit targets | Literal rejection / 2147483648 |
| LINT-24 | `char16(0xD800)` | Valid raw UTF-16 unit, not validated scalar |
| LINT-25 | `rune(0xD800)` | Reject scalar validity |
| LINT-26 | `rune(0x10FFFF)` and `rune(0x110000)` | Valid scalar / invalid scalar |
| LINT-27 | `char8(233)` and `char32(0xFFFFFFFF)` | Valid raw units without implied text validity |
| LINT-28 | `bool(1)` | No Integer literal capability or implicit numeric default |
| LINT-29 | `char8(-0)` and `-char8(0)` | Valid raw unit zero / no built-in unary-minus operation |
| LINT-30 | `policy<Wrap>` encloses an out-of-range explicitly signed literal | Still reject construction; selecting arithmetic variants is not literal modulo reduction |

## Direct floating rounding

| Case | Expression or situation | Required result |
|---|---|---|
| LFLOAT-01 | `float32(0xFF)` | 255, not signed int8 -1 |
| LFLOAT-02 | `float32(+0xFF)` and `float32(-0xFF)` | +255 / -255 |
| LFLOAT-03 | `float32(0x3F800000)` | 1065353216, not bitcast 1 |
| LFLOAT-04 | `float16(0x7C00)` | 31744, not infinity bits |
| LFLOAT-05 | `float32(0x1.fp+2)` | 7.75 |
| LFLOAT-06 | `float32(-0x1.fp+2)` | -7.75 |
| LFLOAT-07 | `float32(-0)` and `float32(-0x0)` | Negative zero, bits 80000000 |
| LFLOAT-08 | `float32(+0)` and `float32(0)` | Positive zero, bits 00000000 |
| LFLOAT-09 | `float32(0.1)` | Bits 3DCCCCCD, direct target rounding |
| LFLOAT-10 | `float64(0.1)` | Bits 3FB999999999999A |
| LFLOAT-11 | `float32(1.0000000596046448)` | Bits 3F800001; binary64-first rounding incorrectly yields 3F800000 |
| LFLOAT-12 | `float32(0x1.000001p0)` | Exact midpoint rounds to even, bits 3F800000 |
| LFLOAT-13 | `float32(0x1.000003p0)` | Exact midpoint rounds to even, bits 3F800002 |
| LFLOAT-14 | `float16(65536)` | Positive infinity, bits 7C00; successful parse |
| LFLOAT-15 | `float16(-65536)` | Negative infinity, bits FC00; successful parse |
| LFLOAT-16 | `float16(0x1p-24)` | Least positive subnormal, bits 0001 |
| LFLOAT-17 | `float16(-0x1p-25)` | Negative midpoint underflow zero, bits 8000 |
| LFLOAT-18 | `float16(0x3p-25)` | Even subnormal coefficient two, bits 0002 |
| LFLOAT-19 | `float16(65520)` | Midpoint overflow to positive infinity, bits 7C00 |
| LFLOAT-20 | `float16(65519)` | Maximum finite, bits 7BFF |
| LFLOAT-21 | `float128(0x1p-16494)` | Least positive subnormal, representation value bit one |
| LFLOAT-22 | Each binary format rounds half its least positive subnormal | Positive zero for positive input; negative zero for negative input |
| LFLOAT-23 | `bfloat16(0x1.01p0)` | Even midpoint, bits 3F80 |
| LFLOAT-24 | `bfloat16(0x1.03p0)` | Even midpoint, bits 3F82 |
| LFLOAT-25 | Every binary format receives an exact normal power of two | Exact format value; no software/hardware semantic difference |
| LFLOAT-26 | Parser encounters malformed fractional/exponent input | Reject malformed literal; no infinity/zero success fallback |
| LFLOAT-27 | Identifier NaN or Infinity is written | Ordinary name rules; not a numeric-token spelling |
| LFLOAT-28 | Host lacks native float128 or decimal arithmetic | Supply semantics-preserving construction or reject missing required support; do not choose another format |

## Decimal cohorts and format bounds

| Case | Expression or situation | Required result |
|---|---|---|
| LDEC-01 | `decimal32(1.20)` | Positive coefficient 120, exponent -2 |
| LDEC-02 | `decimal32(1.2)` | Positive coefficient 12, exponent -1 |
| LDEC-03 | Values from LDEC-01/02 compare or are bit-observed | Numerically equal; retain distinct quantum exponents |
| LDEC-04 | `decimal32(-0.00)` | Negative zero, exponent -2 |
| LDEC-05 | `decimal32(0.00)` | Positive zero, exponent -2 |
| LDEC-06 | `decimal32(1.20e+3)` | Positive coefficient 120, exponent 1 |
| LDEC-07 | `decimal32(001__2.00)` | Positive coefficient 1200, exponent -2 |
| LDEC-08 | `decimal32(1234567.5)` | Positive coefficient 1234568, exponent 0 |
| LDEC-09 | `decimal32(1234566.5)` | Positive coefficient 1234566, exponent 0 |
| LDEC-10 | `decimal32(12345678)` | Positive coefficient 1234568, exponent 1 |
| LDEC-11 | `decimal32(1.0000000)` | Positive coefficient 1000000, exponent -6; shorten only to fit precision |
| LDEC-12 | `decimal32(1.230000)` | Positive coefficient 1230000, exponent -6; no arbitrary normalization |
| LDEC-13 | `decimal32(1e96)` | Positive coefficient 1000000, exponent 90 |
| LDEC-14 | `decimal32(1e97)` | Positive infinity, not parser rejection |
| LDEC-15 | `decimal32(-1e97)` | Negative infinity, not parser rejection |
| LDEC-16 | `decimal32(1e-101)` | Positive subnormal coefficient 1, exponent -101 |
| LDEC-17 | `decimal32(5e-102)` | Positive zero, exponent -101, ties to even |
| LDEC-18 | `decimal32(-5e-102)` | Negative zero, exponent -101 |
| LDEC-19 | `decimal32(15e-102)` | Positive coefficient 2, exponent -101, ties to even |
| LDEC-20 | `decimal32(0e1000)` | Positive zero, exponent clamped to 90 |
| LDEC-21 | `decimal32(-0e-1000)` | Negative zero, exponent clamped to -101 |
| LDEC-22 | `decimal32(0x1.8p0)` | Positive coefficient 15, exponent -1 |
| LDEC-23 | `decimal32(0x1.00p0)` | Positive coefficient 1, exponent 0 |
| LDEC-24 | `decimal32(0x64p0)` and `decimal32(0x64)` | Coefficient/exponent (1,2) / (100,0); Floating/Integer input preference differs |
| LDEC-25 | `decimal32(-0x0p-10)` | Negative zero, exponent zero |
| LDEC-26 | Decimal64/128 inputs preserve representable written scale | Apply their 16/34-digit precision and quantum bounds, not decimal32 or CLR decimal |
| LDEC-27 | Rounded subnormal or carry could be double-rounded | Round original exact magnitude once at the selected final quantum |
| LDEC-28 | Constant is copied, persisted or consumed in decimal remainder | Retain its admitted sign/cohort; numeric equality does not authorize normalization |

## Validated text and scalar input

| Case | Source or situation | Required result |
|---|---|---|
| LTEXT-01 | `"a"` and `"\u0061"` reach a Text recording parser | Same one-scalar decoded input |
| LTEXT-02 | `"\n"` | One LF scalar, not backslash/n source spelling |
| LTEXT-03 | `"\uD83D\uDE00"` | Valid UTF-16 pair, scalar U+1F600 |
| LTEXT-04 | `"\uD800"` | Reject invalid decoded text before parser invocation |
| LTEXT-05 | `"\uDC00"` | Reject invalid decoded text before parser invocation |
| LTEXT-06 | `"\uD800x"` | Reject unpaired surrogate; no replacement |
| LTEXT-07 | `"\0"` | One U+0000 scalar, no appended terminator |
| LTEXT-08 | `" a "` | Preserve both spaces; no trimming |
| LTEXT-09 | Canonically equivalent composed/decomposed strings | Distinct scalar input; no normalization |
| LTEXT-10 | Source CRLF occurs in raw content without a directive | Preserve its selected raw content breaks |
| LTEXT-11 | Raw framing and indentation remove their defined parts | Supply transformed content, not the quote fence/source indentation |
| LTEXT-12 | Raw source requests a newline transform | Apply exactly the defined directive; no extra transform |
| LTEXT-13 | Raw single-line content contains backslash/n | Keep literal backslash/n, not escape decoding |
| LTEXT-14 | Parser receives decoded text | Record actual UTF-16 encoding, not runtime default encoding |
| LTEXT-15 | Parser projects input to char8 | Checked projection fails; do not transcode implicitly |
| LTEXT-16 | Parser attempts writable access to input | Reject authority |
| LTEXT-17 | Input result is empty | Valid empty text view; target may accept/reject it |
| LTEXT-18 | Embedded zero reaches a z-terminated target adapter | Target's explicit validity decides; no automatic prefix truncation |
| LTEXT-19 | `'a'` reaches a Character recording parser | Exactly one scalar input |
| LTEXT-20 | Empty or multiscalar character form | Reject before invocation |
| LTEXT-21 | Surrogate escape is used as one character | Reject before invocation |
| LTEXT-22 | `char8('\u00E9')` | Reject non-ASCII standalone code-unit representability |
| LTEXT-23 | `char16('\U0001F600')` | Reject two-code-unit scalar representability |
| LTEXT-24 | `rune('\U0001F600')` and `char32('\U0001F600')` | Valid scalar |
| LTEXT-25 | A noncharacter scalar reaches validated text/rune | Valid scalar; do not invent a decoder/character ban |
| LTEXT-26 | Direct character fragment appears in text construction | Handler supplies rune context and forwards the scalar |
| LTEXT-27 | A parser reports an error inside decoded content | Preserve original-token source extent; no invented offset equivalence |
| LTEXT-28 | A text literal has only numeric kind registration | Reject kind mismatch, not numeric parsing of decoded text |

## Execution, output validity and admission

| Case | Situation | Required result |
|---|---|---|
| LEVAL-01 | Parser succeeds and completely initializes valid inline target | Validate and publish its frozen constant |
| LEVAL-02 | Parser returns false and leaves output vacant | Literal diagnostic; do not read/serialize/default output |
| LEVAL-03 | Parser returns false with live output under required contract | Reject violated publication contract |
| LEVAL-04 | Parser returns true without complete initialization | Reject violated publication contract |
| LEVAL-05 | Typed result fails nominal validity | Reject before constant publication |
| LEVAL-06 | Parser tries to return borrowed input backing | Reject unsupported escaping dependency |
| LEVAL-07 | Parser returns identity-bearing native/managed resource | Unsupported initial frozen value domain |
| LEVAL-08 | Selected literal is inside an unused runtime branch | Required formation still checks normally |
| LEVAL-09 | Checked parser reaches a constant-eligible path and skips mode-guarded ordinary effects | Admit selected path with inherited constant mode |
| LEVAL-10 | Claimed parser form reaches ambient I/O or runtime static initialization | Reject unsupported/violated form before effects; no runtime fallback |
| LEVAL-11 | Parser throws or required validity/cleanup fails | Required constant diagnostic; preserve failure/cleanup contract |
| LEVAL-12 | Work/depth/state/result limit is exhausted | Exact required-resource result, not parser false or overload fallback |
| LEVAL-13 | Result is cached or parser is inlined | Charge same semantic request; no budget discount |
| LEVAL-14 | Literal parser accepts only a source prefix | Registration violates whole-input contract; require a whole-input adapter |
| LEVAL-15 | Ordinary runtime parser grammar differs from source grammar | Register an explicit source adapter; no compiler-synthesized fallback |
| LEVAL-16 | Two equal literal values were constructed under different targets | Retain their distinct target/ABI identities |
| LEVAL-17 | Same magnitude arrives with unspecified/positive/negative leading sign | Retain actual input spelling and semantics in dependencies |
| LEVAL-18 | Same decoded text comes from two escaped spellings | Input recipe may yield equal values; original source extents remain distinct |
| LEVAL-19 | Registration/body/input recipe/value ABI changes | Revalidate new execution and affected retained contracts |
| LEVAL-20 | Compiler-time parser dependency is unused by runtime program | No automatic runtime linking requirement |
| LEVAL-21 | Source format is supported but its physical constant carrier is unassigned | Explicit unsupported persisted/emission requirement; do not allocate by inference |
| LEVAL-22 | Generic attribute row names a Literal without the admitted semantic schema | No activation of unknown physical semantics |
| LEVAL-23 | Imported registered method has no available constant body | Unsupported required new evaluation |
| LEVAL-24 | Body recipe/cost version is unknown | Reject before required execution |
| LEVAL-25 | Repeated source site evaluates a previously formed literal at runtime | No parser replay; ordinary value/slot costs remain |
| LEVAL-26 | Literal Copy target is treated as eligible constant generic domain | Require separate explicit domain capability |
| LEVAL-27 | Primitive parser is changed by an unrelated import | Reject unauthorized replacement; mandatory results remain fixed |
| LEVAL-28 | Frozen output encoding loses negative zero or required decimal cohort | Reject invalid encoding, not equal-value substitution |
