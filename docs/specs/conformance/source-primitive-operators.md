# Built-in scalar operator conformance cases

These cases apply [primitive operators](../language/primitive-operators.md),
[complete types](../language/types-and-normalization.md), and
[ordinary execution](../language/expressions-and-statements.md).
They are specification obligations, not executed compiler tests.
`I`, `F` and `C` range over every identity in the chapter's declared sets;
parametric cases apply to every eligible instance and its selected target.
Assume live values, valid access, checked literal construction and admitted
formats unless a case tests that requirement. NaNs, signed zeros,
infinities and subnormals are ordinary typed fixture values, not
name-based intrinsic members.

## Binding and the mandatory matrix

| Case | Situation | Required result |
|---|---|---|
| PFORM-01 | Same-typed `I` operands use numeric or bitwise operations | Retain that exact type, including narrow/native identity; check its own width/range |
| PFORM-02 | Same-typed `F` operands use numeric operations | Retain that format, including bfloat16 and IEEE decimal; round at each operation |
| PFORM-03 | `int8(1) + int8(2)` | `int8(3)`, not int32 |
| PFORM-04 | Typed `int8` and `int16` data operands meet `+` or comparison | Reject mismatch; no promotion or always-false equality rewrite |
| PFORM-05 | Native integer and equal-width fixed integer meet arithmetic/equality | Reject different identities despite target width equality |
| PFORM-06 | An alias expands exactly to the same atomic type | Permit that normalized identity without changing its operator result |
| PFORM-07 | A nominal wrapper has the same bits as an integer | No primitive operation without its own admitted contract |
| PFORM-08 | `int64 value = 50000 * 50000;` has no typed operands | Construct int64 operands and produce 2500000000 |
| PFORM-09 | Typed int32 operands meet an int64 outer result context | Keep int32 operation; no result-directed widening |
| PFORM-10 | `var value = 1 + 2;` has no type context | Reject unresolved primitive type |
| PFORM-11 | `bool value = 1 < 2;` has no numeric operand evidence | Reject unresolved data type; Boolean result does not choose a width |
| PFORM-12 | One numeric operand is typed and the other is an untyped literal | Supply that data type to the literal, then check value acceptance |
| PFORM-13 | `var value = 1 << int64(1);` has no data context | Reject missing data type; a typed count is not data evidence |
| PFORM-14 | A floating, Boolean, unit or character data operand is shifted | Reject missing built-in data operation |
| PFORM-15 | An optional scalar is used directly for scalar arithmetic | No implicit unwrap or lifted primitive operation from this matrix |
| PFORM-16 | A generic formal has only Copy evidence | No primitive operator permission inferred from a future specialization |
| PFORM-17 | An ordinary package declares primitive-like names/operators | Do not replace reserved primitive identities or built-in arithmetic |
| PFORM-18 | Equality/inequality uses two same-typed values of any mandatory scalar | Produce bool under that scalar's observation rules |
| PFORM-19 | Boolean/unit/code-unit/scalar values request excluded arithmetic or updates | Reject unavailable operation, not a numeric carrier adaptation |
| PFORM-20 | An atomic metadata code is recognized but execution support is absent | Reject required physical execution before use; source matrix does not allocate a feature/opcode |

## Integer arithmetic and exact bits

| Case | Expression or situation | Required result |
|---|---|---|
| PINT-01 | `int8(127) + int8(1)` | Overflow failure; no result |
| PINT-02 | `int8(-128) - int8(1)` | Overflow failure |
| PINT-03 | `uint8(255) + uint8(1)` | Overflow failure |
| PINT-04 | `uint8(0) - uint8(1)` | Overflow failure, not 255 |
| PINT-05 | `int8(16) * int8(8)` | Overflow failure, not -128 |
| PINT-06 | `uint8(16) * uint8(16)` | Overflow failure, not zero |
| PINT-07 | Minimum signed I divided by same-typed -1 | Quotient overflow failure |
| PINT-08 | Minimum signed I remainder same-typed -1 | Same-typed zero; do not use failing combined quotient/remainder |
| PINT-09 | Any I divided/remainder by same-typed zero | Division-by-zero failure |
| PINT-10 | `int8(-7) / int8(3)` | int8 -2 |
| PINT-11 | `int8(-7) % int8(3)` | int8 -1 |
| PINT-12 | `int8(7) / int8(-3)` | int8 -2 |
| PINT-13 | `int8(7) % int8(-3)` | int8 1 |
| PINT-14 | `int8(-7) / int8(-3)` | int8 2 |
| PINT-15 | `int8(-7) % int8(-3)` | int8 -1 |
| PINT-16 | `uint8(7) / uint8(3)` | uint8 2 |
| PINT-17 | `uint8(7) % uint8(3)` | uint8 1 |
| PINT-18 | Unary negation of zero in each unsigned I | Same-typed zero |
| PINT-19 | Unary negation of any nonzero unsigned I | Overflow failure, not promotion or wrapping |
| PINT-20 | `-int8(-128)` | Overflow failure |
| PINT-21 | `-int8(0xFF)` | int8 1 after typed bit-pattern construction |
| PINT-22 | `~int8(0x0F)` | int8 -16, bits F0 |
| PINT-23 | `int8(0xFF) & int8(0x7F)` | int8 127 |
| PINT-24 | `int8(0x80) \| int8(1)` | int8 -127, bits 81 |
| PINT-25 | `int8(0xAA) ^ int8(0xFF)` | int8 85, bits 55 |
| PINT-26 | `int128(-170141183460469231731687303715884105728) % int128(-1)` | int128 zero; the corresponding quotient fails |
| PINT-27 | Native arithmetic crosses its selected target's signed/unsigned endpoint | Checked failure at that target width, independent of compiler host |
| PINT-28 | Zero dividend in a signed integer divided/remainder by a nonzero integer | Ordinary integer zero; no signed-zero integer state |

## Shift controls and lowering

| Case | Expression or situation | Required result |
|---|---|---|
| PSHIFT-01 | Any typed I count is valid for the established I data width | Accept its control value, retaining source type; result has data type |
| PSHIFT-02 | A built-in count expression has only contextual evidence, such as `value << 1` | Supply int32 context from the control slot, not data type or general literal fallback |
| PSHIFT-03 | A bool, character, rune, unit, floating or pointer count is supplied | Reject; no count conversion from representation or truthiness |
| PSHIFT-04 | A signed count is negative | Invalid-shift-count failure |
| PSHIFT-05 | Count equals the data width | Invalid-shift-count failure |
| PSHIFT-06 | Count exceeds the data width | Invalid-shift-count failure, not masking/clamping |
| PSHIFT-07 | `int32(1) << uint64(0x1_0000_0001)` | Invalid count; narrowing/wrapping into uint32 must not make it 1 |
| PSHIFT-08 | `int32(1) << uint128(79228162514264337593543950337)` | Invalid count before any lossy physical conversion |
| PSHIFT-09 | Negative signed data uses a valid left count | Permit width-limited bit manipulation; no undefined-negative-input rule |
| PSHIFT-10 | `int8(64) << 1` | int8 -128; no arithmetic overflow |
| PSHIFT-11 | `uint8(0x80) << 1` | uint8 zero; no arithmetic overflow |
| PSHIFT-12 | `int8(-3) >> 1` versus `int8(-3) / int8(2)` | -2 versus -1 |
| PSHIFT-13 | `int8(-3) >>> 1` | int8 126, bits 7E |
| PSHIFT-14 | `int8(-128) >>> 7` | int8 1 |
| PSHIFT-15 | `int8(-1) >> 7` versus `int8(-1) >>> 7` | -1 versus 1 |
| PSHIFT-16 | `uint8(0x80) >> 7` versus `uint8(0x80) >>> 7` | Both uint8 1 |
| PSHIFT-17 | A count is zero for any I data | Same value and type |
| PSHIFT-18 | `int128(1) << 127` and `uint128(1) << 127` | Signed minimum and unsigned 170141183460469231731687303715884105728 |
| PSHIFT-19 | `nint(1) << int32(32)` under native-width 32 versus 64 fixtures | Invalid count versus nint 4294967296 |
| PSHIFT-20 | An untyped count literal exceeds int32's range | Literal diagnostic in that context; no other inferred count type |
| PSHIFT-21 | Left and right expressions have effects, then count is invalid | Evaluate left, then right, then fail; no result or update store |
| PSHIFT-22 | Left data evaluation fails | No right evaluation or shift |
| PSHIFT-23 | A negative/too-wide source count meets a uint32-count IL shift | Preserve invalid-count failure before exact successful-path conversion; no conversion-overflow substitution |
| PSHIFT-24 | Count expression first performs checked unsigned negation of nonzero | Operand overflow failure precedes the shift; do not relabel it invalid count |
| PSHIFT-25 | `uint128(1) << int8(127)` | Valid; width checking must not first construct int8(128) |
| PSHIFT-26 | A result-only generic count call needs the built-in control context | Its unresolved result can receive int32; an independently typed count is not retargeted |
| PSHIFT-27 | Signed logical right shift lowers through unsigned bits | Same-width bit-preserving views and zero fill, then original signed result type; no numeric range conversion |
| PSHIFT-28 | Unsigned unary negation lowers into the initial scalar Body | Use checked typed-zero subtraction where supported; do not give signed CheckedNegate new unsigned semantics |

## Floating formats and observable edges

| Case | Expression or situation | Required result |
|---|---|---|
| PFLOAT-01 | Arithmetic in any F instance | Direct same-format rounding at each operation; no host-format substitution |
| PFLOAT-02 | `float16(2048) + float16(1)` | float16 2048, ties to even |
| PFLOAT-03 | `bfloat16(256) + bfloat16(1)` | bfloat16 256, ties to even |
| PFLOAT-04 | `float32(16777216) + float32(1)` | float32 16777216, ties to even |
| PFLOAT-05 | `float64(9007199254740992) + float64(1)` | float64 9007199254740992, ties to even |
| PFLOAT-06 | `float128(10384593717069655257060992658440192) + float128(1)` | The same float128 value, ties to even |
| PFLOAT-07 | `decimal32(1000000) + decimal32(0.5)` | decimal32 numeric value 1000000, not CLR decimal arithmetic |
| PFLOAT-08 | `decimal64(1000000000000000) + decimal64(0.5)` | decimal64 numeric value 1000000000000000 |
| PFLOAT-09 | `decimal128(1000000000000000000000000000000000) + decimal128(0.5)` | The same decimal128 numeric value |
| PFLOAT-10 | `float16(65504) + float16(65504)` | Positive infinity, not an integer/CLR-decimal overflow failure |
| PFLOAT-11 | `float16(0x1.p-24) * float16(0.5)`, with positive/negative operand variants | Positive/negative zero respectively, ties to even with sign retained |
| PFLOAT-12 | `float16(0x1.p-14) / float16(2)` | Exact subnormal 0x1.p-15; no flush to zero |
| PFLOAT-13 | Positive zero plus negative zero; negative zero plus negative zero | Positive zero; negative zero under nearest/ties-even |
| PFLOAT-14 | Unary plus of a copied NaN or finite decimal cohort | Identity/copy representation; no arithmetic canonicalization |
| PFLOAT-15 | Negation of finite F, either zero, or either infinity | Reverse sign, retaining magnitude and finite decimal cohort |
| PFLOAT-16 | Unary negation of quiet/signaling NaN | Toggle only sign; preserve payload and quiet/signaling state |
| PFLOAT-17 | Same-typed positive finite 1 divided by negative zero | Negative infinity, no language failure |
| PFLOAT-18 | Zero divided by zero, or infinity divided by infinity | NaN, no floating trap |
| PFLOAT-19 | Infinity times zero, or opposite infinities added | NaN, no floating trap |
| PFLOAT-20 | Binary arithmetic consumes or produces NaN | Quiet NaN; runtime propagation payload/sign unspecified |
| PFLOAT-21 | NaN participates in comparison with finite value or NaN | Equality/order false, inequality true; no total-order substitution |
| PFLOAT-22 | Positive and negative zero compare equal | True despite distinguishable signs/reciprocals |
| PFLOAT-23 | Equal-valued decimal cohorts compare | Equality true; bit identity/key normalization is separate |
| PFLOAT-24 | Same-typed 5.5 or -5.5 remainder 2 or -2 | 1.5 or -1.5 according to dividend sign, not nearest-integer remainder |
| PFLOAT-25 | A finite dividend is exactly divisible, including a negative dividend/negative zero | Remainder zero retains dividend sign |
| PFLOAT-26 | Finite dividend remainder infinite divisor | Preserve dividend, including decimal cohort and signed zero |
| PFLOAT-27 | Infinite dividend remainder finite value, or any non-NaN dividend remainder either zero | NaN, not language division-by-zero failure |
| PFLOAT-28 | A remainder operand is NaN | NaN under the ordinary arithmetic payload/sign policy |
| PFLOAT-29 | `float32(1) % float32(0x1.p-149)` | Positive zero; an overflowing rounded quotient must not produce NaN |
| PFLOAT-30 | Float32 operands 0x1.000002p0 and 0x1.fffffep-1 are multiplied, then float32 -1 is added | Positive zero after separate rounding; implicit FMA would change the result |
| PFLOAT-31 | `(float16(2048) + float16(1)) + float16(-2048)` | Positive zero, not a widened-expression result of 1 |
| PFLOAT-32 | A separately adopted explicit fused/approximate API is invoked | Apply that API's contract; its existence does not change ordinary arithmetic |
| PFLOAT-33 | Decimal dividend cohort (550,-2) and divisor (20,-1) use remainder | Result coefficient/exponent (150,-2), numeric 1.50; do not normalize to 1.5 |
| PFLOAT-34 | Decimal exact divisibility uses dividend exponent -2 and divisor exponent -1 | Zero exponent -2 with dividend sign, not arbitrary zero cohort |
| PFLOAT-35 | Decimal dividend 5 at exponent 0 and divisor 2.1 at exponent -1 | Remainder 0.8 at exponent -1; dividend-only exponent would lose the exact value |
| PFLOAT-36 | Finite decimal operands have widely separated valid exponents | Exact remainder coefficient at the smaller exponent fits precision; no rounded quotient or excess-coefficient failure |

## Boolean, character and unit observations

| Case | Situation | Required result |
|---|---|---|
| POBS-01 | Boolean `!` consumes true or false | False or true |
| POBS-02 | Boolean `&`, `\|`, `^` consume all true/false pairs | Ordinary two-valued truth table after both operands evaluate in order |
| POBS-03 | `false && RHS()` | False without RHS evaluation |
| POBS-04 | `true \|\| RHS()` | True without RHS evaluation |
| POBS-05 | Short-circuit logic reaches a nullable Boolean RHS | Require supported presence/extraction; no absent-as-false rule |
| POBS-06 | Boolean `&` has false LHS, or `\|` has true LHS | Still evaluate RHS; these are eager, not short-circuit |
| POBS-07 | Eager Boolean RHS fails despite a determining LHS value | Propagate that failure, not an optimized Boolean result |
| POBS-08 | Boolean equality/inequality observes live values | Bool result, with no Boolean numeric order |
| POBS-09 | `bool` requests order, arithmetic or increment | Reject unavailable primitive operation |
| POBS-10 | Code-unit/rune operands request arithmetic, bitwise, shift or increment | Reject unavailable primitive operation |
| POBS-11 | Raw char16 surrogate and FFFF units compare | Unsigned raw-unit order, without scalar decoding |
| POBS-12 | Raw char8 FF and 7F units compare | FF is greater; no signed int8 interpretation |
| POBS-13 | Different character widths or rune/integer compare | Reject differing identities; no carrier promotion |
| POBS-14 | Two effectful unit values compare | Evaluate both once in order; equality true, inequality false |
| POBS-15 | Unit requests order, arithmetic or truthiness | Reject |
| POBS-16 | Rune values U+E000 and U+10000 compare | U+E000 is smaller in scalar order, independent of UTF-16 unit ordering |

## Updates, accessors and failures

Update traces use `R` for captured receiver, `I` for captured indices,
`G` for old-value getter, `H` for RHS, `O` for successful arithmetic,
`S` for setter/store, and `X` for operation failure. Assume no extra
failure/effects unless stated. These events are observations, not
an atomicity or transaction guarantee.

| Case | Situation | Required result |
|---|---|---|
| PUPDATE-01 | Named int32 starts at 1; RHS writes 10 to it then returns 2 for `+=` | Save old 1 before RHS; final value 3, not 12 |
| PUPDATE-02 | Named int8 starts at 127; RHS effects complete then `+= 1` overflows | No final assignment; retain authored RHS effects and propagate overflow |
| PUPDATE-03 | Eligible scalar indexer performs successful compound assignment | `R I G H O S`; capture receiver/indices and invoke accessors once |
| PUPDATE-04 | Getter fails | No RHS, operation or setter; propagate getter failure |
| PUPDATE-05 | RHS fails after getter | No operation or setter; retain earlier effects |
| PUPDATE-06 | Checked arithmetic fails after getter/RHS | `R I G H X`, without S; no implicit rollback |
| PUPDATE-07 | Setter fails after arithmetic succeeds | Apply its ordinary declared failure/effects; no universal restoration |
| PUPDATE-08 | Value getter has no eligible ordinary setter | Reject update, even though the value can be read |
| PUPDATE-09 | Setter is inaccessible, readonly for this receiver, or initialization-only | Reject ordinary update; no authority from the getter |
| PUPDATE-10 | Getter/setter do not have the required exact scalar value contract | Reject; no numeric conversion or guessed backing reference |
| PUPDATE-11 | Integer scalar postfix increment succeeds | Store new same-typed value and yield saved old same-typed value |
| PUPDATE-12 | Integer scalar prefix increment succeeds | Store and yield new same-typed value |
| PUPDATE-13 | Increment/decrement crosses any I endpoint | Checked failure without result or replacement |
| PUPDATE-14 | Float16 2048 is incremented through an observable accessor | New value is still 2048; retain required getter/setter effects |
| PUPDATE-15 | Ordinary writable place aliases an RHS write valid under its contracts | Keep saved old-value timing; do not invent exclusion or two-phase loans |
| PUPDATE-16 | Caller borrows a value-returning property as `write ref<T>` | Reject; getter/setter update support does not make a returned value a place |
| PUPDATE-17 | Nominal compound operator requires a write-ref destination but target is only a value getter/setter | Reject missing actual place; no implicit temporary-reference adapter or read/copy/write synthesis |
| PUPDATE-18 | Bare `target++` would discard its non-unit result | Require ordinary result use or explicit discard; compound assignment remains a statement |
| PUPDATE-19 | Integer shift assignment has invalid count after RHS effects | No final store/setter; preserve count type, failure and earlier effects |
| PUPDATE-20 | Boolean `&=`/`\|=` has a determining old value | Still evaluate RHS once before successful store |
| PUPDATE-21 | RHS rebinds a source receiver/index variable after capture | Store through original captured receiver/indices, not reevaluated source values |
| PUPDATE-22 | A shared scalar uses a built-in update | No atomic RMW guarantee; require its sharing/coordination protocol |
| PUPDATE-23 | A target is vacant, immutable, or lacks required writable access | Reject missing read/update permission; no implicit initialization |
| PUPDATE-24 | Floating `/= 0` produces infinity/NaN under the selected format | Store its normal floating result; do not impose integer division failure |
| PUPDATE-25 | Arithmetic/update result feeds a NoThrow body | Prove any required check cannot fail and include accessor/RHS effects; don't infer totality from a literal spelling |
