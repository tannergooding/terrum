# Core scalar checked failures

This chapter owns the initial nominal Core exception-object catalog for
checked scalar value failures. Its logical rule is
`scalar-failure-catalog/1`. The
[primitive operator domains](primitive-operators.md),
[conversion policies](../conversions.md#conversion-policies) and
[Abs contract](absolute-value.md) determine whether an operation succeeds;
this catalog fixes the exception class for the failed operation, not another
value algorithm.

The catalog is deliberately smaller than all runtime failures. Postconditions,
admitted checked indices, initialization and explicitly recoverable allocation
use the separately selected [standard runtime categories](../runtime/standard-failures.md).
Optional library preconditions retain their declared contracts.
Expected Boolean/status/Result
outcomes, unsupported requirements, invalid source/interchange and unadmitted
hardware faults are not silently translated to these exceptions.
The separately selected [numeric selection contract](numeric-selection.md)
uses exact InvalidBoundsException for Clamp's explicit ordered-bound
precondition. That ordinary requirement failure neither adds a fourth
arithmetic/count mapping here nor turns a clamped value into a failure.

## 1. Exact Core exception classes

| Exact public Core identity | Selected scalar domain |
|---|---|
| `Terrum.OverflowException` | A checked numeric result cannot be represented in the destination type |
| `Terrum.DivideByZeroException` | The divisor of integer division or remainder is zero |
| `Terrum.ArgumentOutOfRangeException` | A built-in integer shift count is negative or at least the data width |

Each identity is a distinct public nongeneric managed class implementing its
exact self-typed [`IException<TSelf>` contract](local-failures.md#1-the-common-exception-contract).
The common contract supplies the durable message and creation paths, authored
`InnerException`, runtime-owned `ParentException`, and `IFormattable` witness.
These scalar classes add no operand-, count-, or stack-trace-specific fields.
Their class identity and common factory contract are required; this catalog
does not select a constructor signature, payload layout, or generic category
interface.

Exact selected Core declaration identities matter. Transparent aliases
normalize to the same identity; names, constructors, annotations or another
package's types do not supply a registered Core exception class. The common
interface contract does not impose a universal stack-trace format, equality
or hash behavior, or a shared/typed capture family.
Exception eligibility does not by itself grant `Copy`, `Blittable`, a
particular byte layout, `Pure`, `ConstEval`, a failure-free contract, thread
capability or a native failure-packet representation.

## 2. Scalar operation mapping

The following rows apply only to already admitted built-in operations and
conversion pairs. They do not add numeric promotions, conversions, optional
lifting, nominal overloads, SIMD or atomic APIs.

| Operation and failed value domain | Exact exception class |
|---|---|
| Development/Debug-profile integer addition, subtraction, multiplication, negation, increment or decrement whose mathematical result is outside the same type's range | `Terrum.OverflowException` |
| An explicitly checked integer operation whose result is outside the same type's range, in any profile | `Terrum.OverflowException` |
| Signed integer division of the minimum by `-1` | `Terrum.OverflowException` |
| Signed integer Abs at that type's minimum | `Terrum.OverflowException` |
| Checked integer-to-integer conversion whose value is outside the destination range | `Terrum.OverflowException` |
| Already admitted checked numeric-to-code-unit conversion with a result outside its code-unit range | `Terrum.OverflowException` |
| Selected checked floating-to-integer conversion of a finite value whose truncation toward zero is outside the destination range | `Terrum.OverflowException` |
| Selected checked floating-to-integer conversion of NaN or either infinity | `Terrum.OverflowException` |
| Integer division or remainder with a zero divisor | `Terrum.DivideByZeroException` |
| Built-in `<<`, `>>` or `>>>` with an invalid exact source count | `Terrum.ArgumentOutOfRangeException` |

The selected floating-to-integer inventory remains `float32` or `float64`
to each of `int32`, `uint32`, `int64` and `uint64`. This failure mapping
does not complete other pairs. Check the finite truncated result, not the
untruncated source: a negative fractional value whose truncation is zero can
successfully convert to an unsigned destination. NaN has no integer result;
the same OverflowException covers that unrepresentability without claiming
NaN is a finite out-of-range number.

The raw `char16` range includes surrogate code units. A representable
numeric-to-char16 conversion does not fail merely because its result is not
a Unicode scalar. Rune validation, character-literal decoding, nominal
validity and text-decoder failures keep their separate contracts.

Development/Debug unsigned negation is checked `0 - value`: every nonzero
unsigned input maps to OverflowException. Release-profile default unsigned
negation wraps `0 - value` at the declared width. Explicit checked/wrapping
operations retain their selected policy in either profile; unsigned Abs
remains identity.
Integer remainder of a signed minimum by `-1` is zero, not quotient overflow.
A combined quotient/remainder request that needs the overflowing quotient
can fail; fusing a remainder-only request must not introduce that failure.

After a valid count, shifted-out integer bits do not cause overflow.
Count validation uses the original complete count value before any physical
control-operand conversion. A negative signed count must not become an
OverflowException by first converting it to uint32. A too-wide unsigned count
must not wrap to an apparently valid count. If the source count is valid,
any required narrower control representation must be exact.
For 128-bit data, int8 counts through 127 are valid; comparing against the
width cannot first overflow an int8 representation of 128.

Floating arithmetic retains its IEEE outcomes. Finite overflow to infinity,
underflow, NaN, inexactness and floating division/remainder by zero do not
become one of these scalar checked failures. The
[floating-environment contract](floating-environment.md) separately selects
sign preservation, quiet-NaN results and no ambient language status facility.

Explicit wrapping, saturating or Try operations retain their own selected
contracts. A same-named nominal operator/conversion or numeric witness uses
its actual declared failure contract, not this catalog inferred from a name.
An atomic provider likewise retains its separately admitted ordering,
authority and failure contract; this table supplies neither a protocol nor
permission to write and later undo a failed arithmetic attempt.

## 3. Creation and event ownership

Admit the mapped type, exact construction and actual propagation dependencies
before affected use. Evaluate the operation's operands under their ordinary
order and transfer rules, once. An operand failure prevents later operands
and the operation. A successful scalar operation invokes no exception factory and creates no
event.

At a failed value check under an admitted checked-exception policy, invoke the
selected class's common creation path once to materialize the exception object.
That object is the fresh propagation event, whose origin is the **failed source
operation** and whose contract retains the complete operand/result domain and
failure phase. A runtime helper or lowered control conversion does not become
another source origin. No operand is reevaluated, and no ambient requirement
default or unrelated exception selector participates.

Exception creation and transport need their actual allocation, root,
code/runtime lifetime and publication support. The common message or class
shape does not guarantee allocation-free, failure-free or resource-unbounded
transport. If creation or preparation fails before publication of the mapped
exception object, that actual failure follows its admitted contract; it must
not produce a fabricated mapped event, normal numeric result, fallback
exception or lost responsible holder. If no exception object can be
materialized to represent a failure, execution fails fast. Missing known
support instead declines before affected use.

Publish one responsible exception object, with no observable lost or
duplicated responsibility. Different failed operations establish different
events even when their messages are equal. Complete provenance and causes
belong to the event object, not a detached payload. Subsequent active rethrow or
consuming [CapturedException propagation](captured-exceptions.md) preserves
the same object/event. Throwing a distinct exception object establishes its
own event under the [local exception contract](local-failures.md).

Ordinary [local-failure selection and cleanup](local-failures.md) applies:
nominal type tests, object-handle catch bindings, source-order handler
selection, causal replacement and exactly-once responsibility settlement.
A scalar failure escaping a still-responsible handler replaces the pending
failure and retains it through `ParentException`. Faulting cleanup replaces
the pending failure under those same rules, without replay or loss. Exception
eligibility grants no `Copy` contract; copying a handle where separately
permitted does not duplicate the event, its propagation history or
responsibility.

An admitted terminal/fail-fast policy remains terminal. It need not construct
an unobservable local exception object/event before terminating, and it promises
neither catchable propagation nor cleanup after termination. A terminal
value-check policy does not reinterpret a failure while evaluating an
operand. This catalog does not turn a hardware trap, access violation,
stack exhaustion or native packet into a mapped local event.

## 4. Result state, effects and source phases

A failed mapped operation publishes no numeric result. Arithmetic updates
perform no replacement or setter invocation after that failed computation.
An existing destination receives no failed result; a vacant initialization
destination receives no value. Completed operand effects, explicit moves,
getter effects, reads and writes remain actual: this is not rollback.
Atomic failure cannot acquire write/release effects merely because a
language event will propagate; its actual attempted-read contract remains.

The exception catalog supplies no argument reordering, speculative evaluation,
implicit owner cleanup, cancellation or recovery permission.
Caller-safety, loan, output, lifetime and thread-affinity rules remain checked.
Recovery still needs the existing unsafe authorization; finally/defer cleanup
has its ordinary rules.

A false runtime `require` uses its own explicit/default selector and the
selected exception class's common creation path. RequireException does not
override a numeric failure in the predicate. An explicit
`require<OverflowException>` uses the same Core class, but its fresh origin is
that require site, not an invented arithmetic operation.

Static proof or literal-validation failure is a compiler diagnostic, not a
runtime construction of this catalog. Required constant execution retains
its actual admitted program, author opt-in, effects and resource rules.
An unhandled reached scalar failure prevents required result publication;
it never permits a runtime fallback. A separately admitted program can
handle its actual local event under the existing interpreter contract;
this catalog alone does not provide that interpreter or capture support.

Before async operation establishment, failure remains synchronous.
After establishment, an escaping mapped failure is failed completion even
when the body runs inline or has not awaited. Producer creation remains
synchronous; body/advance/close follow their actual operation channels.
Resuming on another thread or outlining a helper does not change the source
operation's mapped type, origin or phase.

Checked arithmetic and construction/transport retain their actual effect
and dependency bounds. Handling an internal failure does not prove that the
whole invocation cannot fail or is Pure; neither a catalog name nor a caught
exception object establishes those contracts. Relevant checks may be
eliminated only with sound supported proofs that preserve all operand,
result-state, failure and retained-contract observations.

## 5. Publication and physical completion

Retain exact Core exception-class and factory identities, source operation and
normalized scalar types, native width where applicable, mathematical check
and failed domain, selected policy, event publication/state, effect/phase,
catch/cleanup/cause obligations and full transport/helper/runtime dependencies.
Known type codes, equal ABI, an optional name or raw runtimePolicy do not
encode those contracts.

Replacement or lowering must preserve the actual mapped outcome, including
invalid shift versus conversion overflow and remainder-only success.
An explicitly justified replacement can add unrelated facilities without
breaking every caller; it cannot substitute a same-shaped exception class or rewrite
a retained failure site. Trusted readers remain access-scoped, not forced
to scan unrelated Bodies to infer the catalog.

The current physical scalar profile's fail-fast support does not activate
checked exception creation or handler execution. Complete registration,
exception-class/value, source-operation failure, phase, event/handler/cause and
transport/resource records remain unassigned. No Feature ID, Attr tag,
TypeSpec, intrinsic wire ID, opcode, exception region or native ABI is
allocated here.

[Conformance cases](../conformance/source-scalar-failures.md) distinguish finite
value/mapping and supplied ownership/admission evidence from actual compiler,
runtime event preparation, hardware containment and independent physical
codec conformance.
