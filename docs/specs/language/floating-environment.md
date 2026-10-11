# Floating environment and representation operations

This chapter owns `floating-status-observability/1`,
`floating-sign-operations/1` and `floating-nan-results/1`.
[Primitive operators](primitive-operators.md), [conversions](../conversions.md),
[Abs](absolute-value.md) and [numeric selection](numeric-selection.md)
retain their operand domains, ordering, rounding and failure rules.
These rules apply to all five binary and three IEEE decimal scalar formats;
they do not add conversion pairs, library functions or physical opcodes.

## 1. Value-oriented execution

Ordinary Terrum floating execution uses the operation's specified format and
rounding, with subnormals retained and no floating traps. There is no
language-level ambient rounding mode or floating-status flag facility.
Inexact, underflow, overflow, division-by-zero and invalid-operation status
do not create an independently observable Terrum status value, sticky state,
callback or failure event. A NaN or infinity remains an ordinary value where
the operation admits it.

This is not a full IEEE floating-environment binding. IEEE default non-stop
handling can raise status flags even without trapping; Terrum does not make
those incidental flags part of these source operations' observation contract.
An implementation need not reproduce, clear or preserve them merely to
implement a Terrum expression. This does not permit a changed finite result,
zero sign, decimal quantum, required NaN class or representation operation.

Actual native control-state and ABI obligations remain independent.
The [Windows x64 deployment contract](../targets/windows-x64-deployment.md#1-register-and-control-state-preservation)
requires restoration of changed nonvolatile control fields and its standard
call environment; its volatile MXCSR status bits are not a new language
promise. A foreign observer uses its separately admitted foreign contract,
not an implicit ambient Terrum API. Software arithmetic has the same value
requirements without a requirement to simulate hardware sticky flags.

Any future status-aware API needs an explicit operation/result and complete
effect, state, target and dependency contract. No such API, status enum,
thread-local context, save/restore wrapper or optional trap policy is adopted
here. Evaluation-mode queries are not floating-environment queries.

## 2. Sign-only operations

Floating unary negation toggles only the representation's sign component.
Floating `Abs` clears only that component. For an admitted bit encoding,
these operations respectively toggle or clear exactly its sign bit and leave
every other bit unchanged.

| Input property | Negation | Abs |
|---|---|---|
| Finite magnitude, including subnormals | Same magnitude; opposite sign | Same magnitude; positive sign |
| Zero | Opposite zero sign | Positive zero |
| Infinity | Opposite infinity sign | Positive infinity |
| Decimal coefficient and quantum | Preserve both | Preserve both |
| NaN payload and quiet/signaling state | Preserve both; toggle sign | Preserve both; clear sign |
| Other admitted encoding bits | Preserve | Preserve |

There is no arithmetic rounding, NaN quieting or cohort normalization.
Negation is not subtraction from zero, and Abs is not an arithmetic
comparison/negation conditional. Those rewrites can change signed-zero,
NaN or decimal representation observations.

Ordinary copies, unary plus and same-format identity conversion likewise
preserve their representation. Explicit bit-preserving operations retain
their independently declared bit/validity contracts.
Neither sign operation repairs an invalid input encoding or admits a
representation that the selected value/target contract rejects.

These rules also apply in admitted constant evaluation. Negating an explicitly
constructed signaling NaN preserves its signaling state and payload; Abs
preserves them while clearing the sign. Negating a previously formed canonical
positive quiet NaN yields its negative representation, not another positive
canonical NaN.

## 3. NaN-processing results

The following operations produce a **quiet NaN** whenever their successful
floating result is NaN, whether they consume a quiet/signaling NaN or create
a NaN from non-NaN operands:

| Operation | Required NaN result boundary |
|---|---|
| Two-operand floating `+`, `-`, `*`, `/`, `%` | Quiet NaN in the same format |
| Floating increment/decrement | The quiet-NaN rule of their underlying arithmetic |
| An admitted nonidentity numeric conversion producing a floating NaN | Quiet NaN in the destination format |
| Floating Min or Max with either input NaN | Quiet NaN in the same format |
| Floating Clamp with valid bounds and NaN value | Quiet NaN in the same format |

Runtime result sign, payload and choice of propagated input are unspecified.
They must form a valid quiet NaN in the selected result representation.
Quieting is a result contract, not a requirement to mutate the input, trap,
publish a status flag or preserve a particular payload. It is not permission
to canonicalize a copy, identity, negation or Abs.

Comparisons return their specified Boolean results without modifying supplied
values. An unordered Clamp bound still fails its exact `min <= max`
precondition; it cannot become a successful quiet NaN. A floating-to-integer
conversion of NaN retains its checked unrepresentability failure, not a
floating result or zero substitute.

Non-NaN selection results preserve the selected supplied representation,
including zero sign and decimal quantum. Arithmetic's operation-specific
preferred exponents, selection's totalOrder ties and Clamp's in-range
preservation remain distinct.

## 4. Deterministic constant NaNs

For an admitted constant execution of a NaN-processing operation in section 3,
an otherwise unspecified NaN result is the result format's **positive quiet
NaN with zero payload**. Apply that choice at the semantic operation.
Do not rewrite arbitrary input or emitted constant bits at serialization.

For example, with actual constant-program and bit-observation admission,
float32 Min of a signaling NaN and a number produces `0x7FC0_0000`.
Float32 Abs of an explicitly supplied `0xFFA0_0001` instead produces
`0x7FA0_0001`; unary negation of `0x7FC0_0000` produces `0xFFC0_0000`.
The latter two are sign operations, not NaN-processing canonicalization.

This rule does not supply ConstEval author opt-in, an interpreter body,
intrinsic, value codec, resource recipe or failure-transport program.
[Pure and ConstEval](published-predicates-and-effects.md) authorization and
[constant execution](proof-and-constant-evaluation.md) admission remain
required. Pure alone does not select this constant-result recipe.
An escaping bounds/construction failure prevents a required constant result;
it does not fall back to runtime NaN selection.

## 5. Effects and retained requirements

Preserve ordinary eager argument/operand staging, explicit moves, actual
failures, cleanup, result publication and execution channels. A determined
NaN result does not permit skipping a later argument. No floating flag
observation follows, but argument, initialization, helper, resource and
failure effects still belong to their actual contracts.
No unconditional Pure, ConstEval or NoThrow capability follows solely from
fixed rounding or the absence of ambient status.

Retain the exact operation/form, complete format/value representation,
runtime versus constant NaN contract, sign/copy distinction, target control
obligations and complete execution dependencies.
Missing required support declines before affected use. A hardware min,
conversion or arithmetic instruction is suitable only if its actual
behavior implements the selected contract.
This logical selection assigns no new Feature ID, Attr tag, intrinsic,
instruction kind, ABI or physical evaluation/handler codec.

[Conformance cases](../conformance/source-floating-environment.md) distinguish
representation/result facts from actual floating providers, control-state
restoration and admitted source/constant execution.
