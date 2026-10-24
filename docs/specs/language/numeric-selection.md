# Numeric selection members and bounds failures

This chapter owns `numeric-selection-members/1`: the adopted scalar `Min`,
`Max` and `Clamp` member shapes, selected value rules and ordinary call
contract. It also owns `invalid-bounds-failure/1`, the reusable nominal Core
failure for invalid bound relationships. [Absolute value](absolute-value.md)
retains its separate magnitude contract.

These logical rules allocate no primitive registration,
intrinsic, Feature ID, opcode, target ABI or physical handler record.
The [numeric capability groups](numeric-capabilities.md) separately supply
nominal INumericSelection witnesses. The
[floating-environment contract](floating-environment.md) owns NaN quieting,
sign operations, constant NaNs and the no-ambient-status boundary.
Neither chapter adopts the entire numeric/library proposal.

## 1. Numeric subjects and signatures

Each exact owning numeric scalar has these public static nongeneric members:

```terrum
public static T Min(T left, T right);
public static T Max(T left, T right);
public static T Clamp(T value, T min, T max)
    require<InvalidBoundsException> min <= max;
```

`T` ranges over the owning identities below. It is specification notation,
not a method formal, associated result, universal number interface or implicit
promotion. The precondition refers to the resolved Core failure defined in
section 4 and the built-in comparison on that same T.

| Subjects | Exact owning identities |
|---|---|
| Signed integer | `int8`, `int16`, `int32`, `int64`, `int128`, `nint` |
| Unsigned integer | `uint8`, `uint16`, `uint32`, `uint64`, `uint128`, `nuint` |
| Floating point | `float16`, `bfloat16`, `float32`, `float64`, `float128`, `decimal32`, `decimal64`, `decimal128` |

All formals are required ordinary by-value inputs. The result has the same
complete scalar identity. There is no receiver, default argument, params
expansion, initialization output, heterogeneous comparison or width change.
Parameter names are `left`/`right` for Min and Max and `value`/`min`/`max` for
Clamp; ordinary named-argument binding uses them.
Transparent aliases preserve identity. Native types remain distinct from
equal-width fixed integers and use the selected target width, not host width.

The other six language scalars, optional types, enums, nominal wrappers,
SIMD and arbitrary numeric-looking types do not acquire these members from
representation or names. Their separately declared operations retain their
own contracts. Generic use requires actually declared operation evidence under the selected
INumericSelection group; member names do not synthesize a witness.

The owning type supplies contextual literals without a fallback numeric type.
Already typed arguments must have its exact identity, and an expected result
does not retarget that owner:

```terrum
int8 low = int8.Min(-128, 127);
int8 high = int8.Max(-128, 127);
int8 limited = int8.Clamp(100, min: -10, max: 10);
int64 widened = int64(int8.Max(-128, 127));
```

Both signed minima are valid inputs. The first two results are -128 and 127;
the third is 10. The last member still returns int8 before explicit conversion.
A typed int32 input does not bind to an int8 member merely because it fits.

## 2. Min and Max values

For integer T, Min returns the lesser mathematical value and Max the greater.
Equal inputs have that same value. No addition, subtraction, negation,
quotient or magnitude intermediate is required. Every complete integer input
pair has a representable result, including signed minima and unsigned maxima.
These member value domains introduce no overflow or division failure.

For floating T, first use its numeric ordering and the NaN/zero rules below,
not raw encoding order, key equality or a comparer that equates NaNs.
Only an otherwise equal finite representation tie uses IEEE 754-2019
totalOrder, as specified below:

| Input condition | Min | Max |
|---|---|---|
| Either input is NaN | Quiet NaN | Quiet NaN |
| Neither is NaN; numeric values differ | Lesser numeric value | Greater numeric value |
| Both are zero with different signs | The negative-zero input | The positive-zero input |
| Equal finite numeric values with the same sign, including zeros | The input representation earlier in totalOrder | The input representation later in totalOrder |
| Equal infinities | That same signed infinity | That same signed infinity |

All eight formats retain their own format, subnormals and infinity classes.
A non-NaN result uses the selected input's representation, including its
zero sign and, for decimal formats, its quantum.
Selection does not round through another format, flush subnormals, apply
numeric-key normalization or turn an IEEE value class into an integer
checked failure.

For equal finite decimal values, Q(x) is the quantum exponent of the supplied
representation, not its adjusted exponent. The same-sign tie follows
IEEE 754-2019 section 5.10:

| Common sign | Min selects | Max selects |
|---|---|---|
| Positive, including positive zero | The smaller quantum exponent | The larger quantum exponent |
| Negative, including negative zero | The larger quantum exponent | The smaller quantum exponent |

For example, Min(1.0, 1.00) selects 1.00 and Max selects 1.0; Min(-1.0,
-1.00) selects -1.0 and Max selects -1.00. The same exponent rules apply to
equal-sign decimal zeros. Opposite zero signs take precedence over exponents.
Reversing the arguments cannot change the selected numeric representation.

Select a supplied operand with that representation and retain its quantum.
Do not rescale to an arbitrary third member of the same cohort, normalize
trailing zeros or prefer the first argument instead. If both inputs have the
same representation, either may be selected. Where an admitted format permits
distinct encodings of that representation that totalOrder does not distinguish,
this rule adds no encoding-bit preference or canonicalization requirement.
It does not order all raw bits or introduce a new public totalOrder member.

IEEE 754-2019 section 9.6 permits either same-sign operand on a numeric tie and
makes the preferred exponent follow the selected operand. Terrum selects the
section 5.10 tie-break within that latitude. Arithmetic's operation-specific
preferred exponents and the least-exponent rule for inexact results do not
replace this exact-selection rule.

Both quiet and signaling NaN inputs produce quiet NaN results. Runtime
sign/payload and the propagated input choice are unspecified.
Admitted constant execution chooses positive quiet NaN with zero payload.
No ambient language status observation or complete failure-free/purity guarantee
follows. A NaN is handled before the numeric
tie-break: totalOrder must not turn a NaN-propagating Min/Max into a number-only
selection.

Min and Max do not skip an argument because another input already determines
a numeric result or is NaN. Their ordinary eager call staging still occurs.
A replacement instruction or helper must implement these exact NaN and zero
rules; a same-named platform min/max instruction need not do so.

## 3. Clamp values and the ordered-bound precondition

After ordinary argument staging, check the published `min <= max`
precondition before selecting a result from `value`.
Both bounds must be comparable under that type's built-in numeric order and
in the required nondecreasing order. For integer inputs this rejects exactly
reversed bounds. For floating inputs it also rejects either NaN bound.
Do not replace the predicate with `!(min > max)`, which accepts unordered
NaN bounds.

Equal bounds are valid, including different members of one decimal cohort,
either ordering of signed zeros and equal infinities. Infinite bounds and
subnormal bounds are otherwise ordinary valid floating values. No finite-only,
strict-order, canonical-cohort or Unicode/range-encoding requirement is added.

With valid bounds:

| Input condition | Result |
|---|---|
| `value` is NaN in a floating format | Quiet NaN; runtime sign/payload unspecified, canonical positive zero payload in admitted constant execution |
| `value < min` | The supplied min value |
| `value > max` | The supplied max value |
| Otherwise, including numeric equality to either bound | The supplied value unchanged |

Selecting a non-NaN input returns that same-format value under its ordinary
copy/representation contract, without rounding or re-encoding. In particular,
an in-range decimal keeps its original cohort member, and an in-range signed zero
keeps its original sign. A selected non-NaN bound likewise keeps its value
representation. A NaN result is not a bit-preserving copy guarantee.

An out-of-range value is the normal clamping case, not
ArgumentOutOfRangeException. Invalid bounds fail even if value is NaN or
otherwise would select a bound. That priority does not reorder or skip
argument expressions.

Clamp is not generally equivalent to `Min(Max(value, min), max)`. The
composition neither checks the required bound relationship nor preserves all
in-range signed-zero/cohort observations. Such a rewrite needs a sound
supported proof that the complete selected result/effect/failure contract is
unchanged; knowing only that the numeric result fits is insufficient.
For example, an in-range decimal 1.00 with bounds 1.0 and 2.0 stays 1.00;
the composition first selects 1.0 at Max's representation tie. Likewise,
an in-range negative zero with a positive-zero lower bound stays negative
zero, whereas the composition selects positive zero.

## 4. Reusable InvalidBoundsException

Exact Core `Terrum.InvalidBoundsException` is a public nongeneric managed
class implementing its self-typed `IException<InvalidBoundsException>`
contract. Its common creation paths and message/cause/formatting members come
from that contract; the class adds no endpoint-, bound-, or stack-trace-specific
fields. Clamp's `require` path separately resolves an ordinary
zero-supplied-argument constructor binding under
[requirement failures](requirement-failures.md); it does not invoke the
common factory. This chapter selects no payload layout or category interface.

The type describes supplied bounds that do not satisfy an operation's
required relationship, including an unordered or reversed pair. An operation
using it must publish its own exact validity predicate; this name does not
impose one universal comparison, endpoint inclusivity or strictly increasing
order on every domain.
The type is reusable by separately declared interval/range operations but
adopts no new range, interval or collection API.

InvalidBoundsException and ArgumentOutOfRangeException are nominal siblings,
not a base/derived pair or one kind-coded exception. Invalid bounds are distinct
from a value outside valid bounds, a general unrelated requirement,
initialization/bounds access failure and a hardware fault. Neither comparison
syntax nor a `min`/`max` parameter name automatically selects this exception.
Transparent aliases preserve the selected identity; another provider's
same-spelled or same-shaped struct does not supply it.

Clamp explicitly uses ordinary `require<InvalidBoundsException>` at its
published precondition. Its exact constructor binding is resolved before
execution. A caller's RequireException recipe or arithmetic
policy cannot change that selected class or remove the bound check.
The failure has the semantic origin of the selected Clamp precondition,
even if a compiler checks it at a call boundary or outlines its code.
It is not a fabricated caller check, generic arithmetic overflow or helper
failure.
If evaluating the precondition itself fails under an admitted comparison or
effect contract, propagate that actual failure without constructing a bounds
exception. The selected constructor binding is executed only after the predicate
normally produces false; the no-ambient-status contract does not
authorize replacing an actual comparison failure with InvalidBoundsException.

The [ordinary requirement-failure protocol](requirement-failures.md) governs
the selected constructor only on false, publication of one fresh event,
actual preparation/transport failure, source ownership, causal replacement
and cleanup. No factory/event runs for valid bounds. The exception contract
grants no event-copying or responsibility-duplication rule; active/captured
propagation preserves the same object and event, while a distinct replacement
retains the pending exception through `ParentException`.
The shape does not grant a native packet/byte layout, shared capture,
allocation-free transport or a Pure/ConstEval/failure-free capability.
An admitted terminal policy retains its ordinary termination boundary,
without local catch/cleanup promises or reinterpretation of argument failures.

## 5. Evaluation, result state and phases

These are ordinary eager calls. Admit the selected members and complete
required support before affected evaluation. Evaluate the callable and
written argument expressions once in source order, then perform invocation
checks and the callee's precondition/body phases. Named arguments map values
to formals but do not reorder evaluation into formal-declaration order.
An argument failure prevents later arguments and member invocation; keep its
actual event and governing failure channel rather than creating a bounds
failure.

By-value scalar inputs snapshot their staged values. The member does not
retain a reference to the caller's place, implicitly consume it or recompute
the result after the place changes. An explicit move follows ordinary
transfer rules, including for Copy scalars; a later argument or bounds failure
does not restore its source. Completed argument effects and destination
writes remain actual effects.

Publish a numeric result only after successful member completion. Invalid
bounds perform no result store, default/zero initialization or later setter.
An existing assignment destination receives no failed result, while a vacant
destination remains without a result. Neither statement rolls back prior
argument effects, transfers or getter actions.
Ordinary cleanup, access/loan/output obligations and unsafe recovery still
apply. Active/captured rethrow retains the event; an authored recovery result
does not make the failed member call successful.

Before async establishment, escaping failure remains synchronous; after
establishment it is failed completion even for inline execution. Producer
creation/advance/close retain their actual channels. These library signatures
introduce no suspension or new task/producer protocol.

Min/Max have no selected integer value-domain failure. Clamp's required bound
relationship is separately provable for an admitted input domain. Neither
fact proves the complete invocation failure-free or Pure, including argument,
initialization, dependency, construction and transport effects.
Sound proof can omit a redundant check, but cannot invent generic operation
evidence, discard argument effects or replace a false precondition with a
successful result.

Pure and ConstEval remain independently authorized by their
[existing schemas](published-predicates-and-effects.md). A static member,
mathematical algorithm or constant argument supplies no opt-in or admitted
program/value/cost recipe. Required constant use retains its actual choices
and effects, exact target dependencies and deterministic result program.
Any reached unhandled failure prevents result publication, never a runtime
fallback. An admitted constant Min/Max program must retain the selected
numeric representation tie-break and the
[canonical constant NaN recipe](floating-environment.md#4-deterministic-constant-nans).
That recipe does not supply a missing admitted program or author opt-in.

## 6. Retained contracts and completion boundary

Retain exact owning scalar/member and parameter names/modes, complete
parameter/result identities, native target width, numeric/NaN/zero/cohort
result observations, Clamp's exact published predicate and Core factory
identity, source precondition origin, normal versus failed result state,
selected policy, actual effects/phases and full helper/runtime/value/transport
dependencies. Exact scalar layout or a machine compare/select instruction is
not the complete contract.

Missing actual member, predicate, factory, target, value/program or
propagation support declines before affected use. An unused compiler-only
dependency need not become a retained runtime dependency. Unrelated valid
provider additions do not break a complete unchanged expectation.
Trusted readers remain access-scoped; no preliminary unrelated-Body scan is
needed to infer these members.

Primitive registration and independent physical
member/precondition/value/program/event/handler/cause codecs remain separate.
No unchecked/number-only/total-order variant, SIMD operation, new math function
or implementation migration follows.

[Conformance cases](../conformance/source-numeric-selection.md) distinguish
finite exact value/signature, state/event and supplied admission evidence from
actual source binding, runtime resources, floating providers and physical
source-free conformance.
