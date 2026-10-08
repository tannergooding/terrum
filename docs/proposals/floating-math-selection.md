# Floating-point math selection

**Status: pending API proposal.**

## Scope and decision boundary

The selection behavior for `Abs`, `Min`, `Max`, and `Clamp` on `float32` and
`float64` is accepted and recorded in the [library specification](../specs/libraries.md).
This proposal does not reopen or alter that behavior. It considers how those
operations should be exposed and whether additional floating-point formats
should receive corresponding members. Neither an API declaration nor support
for another format is accepted by this document. It supersedes the separate
standalone `Float64Math` service candidate; that service API was not accepted.

The recommendation is to expose selection operations as static members on
their respective primitive types, such as `float32.Abs(value)` and
`float64.Min(left, right)`, rather than introduce standalone `Float32Math`,
`Float64Math`, or shared `*Math` service types. This is a proposed API
placement, not an accepted language contract. Whether primitive members are
compiler-bound, declared through a library mechanism, or supplied by another
checked mechanism remains open.

The spellings below illustrate the preferred call shape only:

```terrum
float32.Abs(value);
float32.Min(left32, right32);
float64.Max(left64, right64);
float64.Clamp(value64, minimum64, maximum64);
```

They do not establish declaration syntax, overload lookup, primitive
definition authority, intrinsics, or implementation support.

## Accepted `float32` and `float64` selection behavior

The accepted contract applies to both `float32` and `float64`:

| Operation | Accepted behavior |
|---|---|
| `Abs` | Return the nonnegative magnitude; either zero becomes positive zero, either infinity becomes positive infinity, and a NaN input produces NaN. |
| `Min` | If either operand is NaN, produce NaN; otherwise select the numerically lesser value. For two zeros, select negative zero if either operand is negative zero. |
| `Max` | If either operand is NaN, produce NaN; otherwise select the numerically greater value. For two zeros, select positive zero if either operand is positive zero. |
| `Clamp` | Fail with a checked argument failure for a NaN bound or numerically reversed bounds before considering the input. With valid bounds, a NaN input produces NaN; otherwise select a bound only for a strictly out-of-range input and return an in-range input unchanged, preserving its zero sign. Infinite bounds are permitted. |

Thus `Min(+0.0, -0.0)` selects `-0.0`,
`Max(-0.0, +0.0)` selects `+0.0`, and
`Clamp(-0.0, +0.0, 1.0)` preserves the input's negative zero. `Clamp` is not
defined by composing `Min` and `Max`; that could change an in-range zero's
sign. Valid numerically equal bounds accept an equal input unchanged. A NaN
input does not conceal invalid bounds.

NaN result-class behavior does not select a NaN payload or sign and does not
specify signaling-NaN quieting or floating status effects. The accepted
selection behavior does not change ordinary IEEE comparisons, define a
floating total order, or settle the concrete failure transport/effect
metadata for invalid `Clamp` bounds.

## Other floating-point types

The proposed primitive inventory lists IEEE binary `float16`, `float32`,
`float64`, and `float128`, `bfloat16`, and IEEE decimal `decimal32`,
`decimal64`, and `decimal128` ([language specification](../specs/language.md)).
Only `float32` and `float64` have the accepted selection
behavior above. The list below identifies families for review; it does not
assert that every type exists in an implementation or should have every
member.

| Type family | Selection API coverage | Questions that remain open |
|---|---|---|
| Binary `float16`, `float128` | Not selected | Whether to provide all four members, and exact-format edge, NaN, and rounding-related behavior. |
| `bfloat16` | Not selected | Whether its distinct precision/range profile warrants the same selection members and whether any operations widen internally. |
| Decimal `decimal32`, `decimal64`, `decimal128` | Not selected | Whether to provide these operations, decimal NaN/infinity availability and behavior, cohort implications, representation-independent numeric equality, and format-specific failure semantics. |

No behavior, API coverage, conversion, widened implementation, or
cross-format promotion for these types follows from the `float32`/`float64`
contract. Any extension should specify semantics per type family rather than
copying binary rules where the representation or value model differs.

## Implementation and support boundary

Self-host components implement internal selection helpers for `float32` and
`float64`; tests cover finite values and selected special-value behavior.
This is internal helper support only, not public primitive-member support or
standalone math services. It neither approves API placement nor adds support
for `float16`, `float128`, `bfloat16`, or decimal formats. Current component
and test coverage is summarized in the
[self-host implementation reference](../../self-host/README.md).

## Separate math contracts

`Sqrt`, `Pow`, `Exp`, `Log`, `Sin`, `Cos`, and `RoundToIntegral` are outside
this selection proposal. Each needs its own domain, special-value, precision,
rounding, and failure contract. Strict target-format `float32` arithmetic
does not implicitly reassociate or contract. Native/approximate functions,
fused multiply-add, and reproducible reductions also require distinct
contracts and should not be inferred from selection-member placement.
