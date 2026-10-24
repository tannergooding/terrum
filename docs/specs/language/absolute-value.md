# Absolute-value members

This chapter owns the adopted scalar `Abs` member shape and magnitude
contract. Its logical rules are `absolute-value-members/1` and
`checked-integer-magnitude/1`. They allocate no physical member registration,
intrinsic, Feature ID or opcode. [Min, Max and Clamp](numeric-selection.md)
have their separate selected member and invalid-bound contract.
[Grouped numeric capabilities](numeric-capabilities.md) select the independent
IAbsoluteValue requirement and the optional scalar composite.

## 1. Numeric subjects and signature

Each of the following exact scalar identities has this public static,
nongeneric member:

```terrum
public static T Abs(T value);
```

`T` above is specification notation for each owning scalar, not a generic
method, associated result type or universal number interface.

| Subjects | Exact owning identities |
|---|---|
| Signed integer | `int8`, `int16`, `int32`, `int64`, `int128`, `nint` |
| Unsigned integer | `uint8`, `uint16`, `uint32`, `uint64`, `uint128`, `nuint` |
| Floating point | `float16`, `bfloat16`, `float32`, `float64`, `float128`, `decimal32`, `decimal64`, `decimal128` |

The sole required formal is the ordinary by-value scalar `value`; the
result has the same complete scalar identity. There is no receiver argument,
initialization output, optional parameter, implicit width promotion or
signed-to-unsigned result conversion. `nint`/`nuint` retain their own identities
even when their selected width equals a fixed integer width. A transparent
type alias does not create another owning identity.

The other six language scalars (`void`, `bool`, `char8`, `char16`, `char32`,
`rune`), optional types, nominal wrappers, enums and SIMD values do not acquire
this member merely from scalar status or representation. Separately declared
operations on those types remain ordinary operations with their own contracts.
A same-named member on another type or an extension is not this adopted member.
The selected declaration identity must be retained; this chapter does not
assign a namespace wrapper, primitive-definition registration mechanism or
physical Method record.

Ordinary [call binding](calls-and-inference.md) applies. The owning type
provides the literal argument's context, not a default integer width:

```terrum
int8 small = int8.Abs(-127);
uint128 large = uint128.Abs(uint128(0xFFFF_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF_FFFF));
int64 widened = int64(int8.Abs(-127));
```

The first result is `int8(127)`. The last call still computes an `int8`
result before its explicit conversion. A typed `int32` argument cannot bind
to `int8.Abs` merely because its current value fits. Result context cannot
widen the operation or change the owning identity.

## 2. Checked integer magnitude

For unsigned `T`, `Abs(value)` returns that value unchanged.

For signed `T`, the successful result is the exact nonnegative mathematical
magnitude in the same `T`. Zero returns zero; a positive input is unchanged;
a negative input other than the minimum returns its negation. The signed
minimum produces a **checked overflow failure and no result**.

For a selected signed width `N`, the range is
`[-2^(N-1), 2^(N-1) - 1]`. The minimum's magnitude is `2^(N-1)`, exactly
one greater than the maximum. This is the sole integer `Abs` value-domain
failure. It is not an invalid input encoding, division failure, checked
argument failure, implicit unsigned result, saturation or wrapped success.
The [primitive checked-negation rule](primitive-operators.md#integer-arithmetic-and-bit-operations)
has the same signed-minimum boundary.

| Subject | Successful boundary examples | Overflow input |
|---|---|---|
| `int8` | `-127 -> 127`, `0 -> 0`, `127 -> 127` | `-128` |
| `int16` | `-32767 -> 32767`, `32767 -> 32767` | `-32768` |
| `int32` | `-2147483647 -> 2147483647` | `-2147483648` |
| `int64` | `-9223372036854775807 -> 9223372036854775807` | `-9223372036854775808` |
| `int128` | `-(2^127 - 1) -> 2^127 - 1` | `-2^127` |
| `nint` | The corresponding selected target-width values | `-2^(N-1)` for that target |

These are mathematical boundary examples, not a new exponent operator or
approval of another target width. The target contract supplies the native
width; the compiler host does not.

The input to `int8.Abs(-128)` is a valid signed literal. An ordinary
execution fails at `Abs`, not while constructing its argument. Likewise,
the signless based literal in `int8.Abs(0x80)` supplies the valid `int8`
value -128 and reaches the same overflow boundary. Widening the result
afterward cannot make either call succeed. An explicitly wider argument
and owning operation is a different call.

The [Core scalar catalog](scalar-failures.md) selects exact
Terrum.OverflowException for this checked magnitude failure. Actual failure
policy, factory creation, propagation and runtime support remain separately
admitted; this selects no exception-object ABI or allocation strategy. A `RequireException`
recipe selects runtime `require` failures; it does not replace this member's
overflow with the callable's requirement default. A terminal policy promises
neither a local catch nor cleanup after termination.

## 3. Floating magnitude

All eight floating subjects retain their own format. `Abs` clears only the
representation's sign component under the
[sign-only contract](floating-environment.md#2-sign-only-operations).
Every other admitted encoding bit remains unchanged, including decimal
coefficient/quantum, NaN payload and quiet/signaling state.
There is no intermediate binary64 value, integer conversion, precision change,
rounding or flush-to-zero substitution.

| Input class | Result |
|---|---|
| Positive or negative finite nonzero value | Its nonnegative numeric magnitude in the same format |
| Positive or negative subnormal | The same nonzero magnitude; subnormal support is not optional |
| Positive or negative zero | Positive zero |
| Positive or negative infinity | Positive infinity |
| Quiet or signaling NaN | Same payload and quiet/signaling state with positive sign |

These values do not themselves produce integer overflow or an invalid-bound
failure. IEEE decimal is not CLR `System.Decimal`. A finite decimal keeps
its supplied quantum; the operation does not select another cohort member.
Abs does not quiet a signaling NaN, including in admitted constant execution.
The [floating-environment boundary](floating-environment.md) supplies no
ambient language status-flag facility and retains actual native control duties.
[Representation operations](../conversions.md#conversion-policies) retain
their separate bit contracts. A mathematical result domain alone does not
publish an unconditional failure-free contract for an arbitrary provider or its
required execution substrate.

## 4. Evaluation, failure and result state

This is an ordinary call, not a special operator or conditional evaluation
form. Admit the selected member and its required contracts before affected
evaluation. Evaluate its argument once under ordinary call staging, including
all reads, effects and failures, before the member computes the magnitude.
An argument failure prevents the member operation; it is not replaced with
an overflow event or a fabricated zero.

An ordinary scalar value argument copies its input. The member does not
implicitly consume or mutate the caller's place, retain a reference to it
or recompute its result when the caller later changes that place.
An explicit `move` during argument staging follows the ordinary transfer
contract, including for a Copy scalar: the source stays vacant even if Abs
subsequently fails. Argument-side mutations and transfers remain actual
caller effects, not state restored by the failed magnitude operation.

Publish a scalar result only on successful member completion. If failure
occurs while staging an assignment's RHS, an existing destination is not
overwritten by that result and a vacant initialization destination receives
no result. This is not rollback: argument-side writes to the destination or
other places remain actual effects.
The surrounding [initialization and cleanup](initialization-and-flow.md)
rules still govern other effects and outputs. An authored handler can return
its own recovery value; that does not make the failed `Abs` call successful.
Established async/producer execution retains its ordinary failure channel,
not a channel selected from the numeric argument or machine instruction.

Integer `Abs` has the stated value-domain boundary. A caller needing failure
freedom must use an admitted guarantee or sound supported proof of the
applicable input domain and all other actual effects. This chapter does not
add private-body inference to the mandatory proof floor or an unconditional
failure-free declaration.

The [Pure and ConstEval rules](published-predicates-and-effects.md) remain
separate. Mathematical computability, a static signature or constant
arguments do not supply author opt-in, a checked pure form, a constant
program/intrinsic or its dependency/cost recipe. A required constant request
needs that actual support; a reached failure prevents result publication
rather than falling back to runtime execution.

## 5. Explicit alternatives

The ordinary member is checked in every build. An enclosing arithmetic
policy does not silently flow into its implementation or replace its
selected declaration.

An explicitly selected **wrapping magnitude** could compute the mathematical
magnitude modulo `2^N`, then interpret those bits as the same signed `T`.
That would agree with ordinary `Abs` except at the signed minimum, where it
would return the minimum unchanged. Such an operation is total over integer
values but does not promise a nonnegative result. Saturating magnitude,
an unsigned magnitude result and a Boolean Try operation are different
contracts again.

These alternatives are not adopted here. No `AbsUnchecked`/`AbsWrapping`
name, policy case, overload, generic witness or intrinsic is selected.
A future variant needs an explicit identity, result/failure/effect contract
and complete admission under the
[ordinary policy rules](../conversions.md#conversion-policies). A missing
required variant must reject; a permissive scope can fall back only to the
ordinary checked member, not an invented wrapping implementation.

The wrapping description is a value contract, not a generated-code or
performance guarantee. Native magnitude instructions, checked sequences and
software helpers vary by type and target.

## 6. Retained contracts and completion boundary

Retain the exact owning scalar/member identity, parameter/result types,
native target width where applicable, mathematical domain, normal versus
failed result state, selected failure policy, actual effects and required
program/helper/target dependencies. An optimization that removes a call must
preserve its argument evaluation, failure behavior and any observable selected
floating choices.

Equal machine signatures or representation bits do not justify replacing
checked `Abs` with wrapped, saturated or unsigned-result behavior. A
replacement must satisfy the complete retained domain/outcome expectations.
Missing required support declines before affected use; it cannot fabricate
a magnitude, suppress overflow or silently invoke a different member.
An unused compiler-only dependency need not become a runtime dependency when
no retained value, contract or execution path requires it.

Physical generic-math witness and primitive member-registration records,
complete effect/program contracts, and constant/call/handler codecs remain
separate completion work. The selected numeric groups, scalar failures and
floating representation contracts do not supply those encodings or programs.
This chapter does not admit a numeric opcode by name, waive selective-reader
boundaries or migrate the current implementation.

[Conformance cases](../conformance/source-absolute-value.md) distinguish finite
mathematical/type/state evidence from actual source binding, full floating
providers, failure construction and independent physical execution.
