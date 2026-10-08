# Built-in numeric selection members

**Status: pending numeric-type inventory and generic-interface proposal.**

## Scope and decision boundary

Two directions are accepted:

- Built-in numeric types have no separate `*Math` classes. Relevant members
  are exposed directly on their primitive type `T` and are accessible through
  generic-math interfaces.
- The selection semantics for `Abs`, `Min`, `Max`, and `Clamp` apply
  universally across built-in numeric types, with special-value rules applying
  where a type supports those values.

These are specific accepted directions only. They do not approve the general
library specification as a whole, which remains under review, or any
unrelated candidate APIs described there.

This proposal records those decisions without settling which individual
primitive types are classified as built-in numeric types, the exact
generic-interface contracts or witness mapping, or implementation support.
The four selection members apply to every built-in numeric type; their
availability is not optional by numeric format. The call spellings illustrate
placement, not a complete API:

```terrum
float32.Abs(value);
float32.Min(left32, right32);
float64.Max(left64, right64);
float64.Clamp(value64, minimum64, maximum64);
int32.Abs(integer);
decimal64.Clamp(decimalValue, lower, upper);
```

These examples do not establish declaration syntax, overload resolution,
primitive-definition authority, intrinsics, or a particular generic-math
interface.

## Accepted universal selection semantics and member applicability

`Abs`, `Min`, `Max`, and `Clamp` are applicable members of every built-in
numeric type. This is universal member coverage, not an optional candidate
that can be omitted for a particular numeric format. Their exact signatures
and generic-interface contracts remain open.

The accepted `float32`/`float64` behavior is the instance of these universal
rules for those formats. Where a numeric format supports NaNs, infinities, or
signed zero, the corresponding rules apply; formats without those values do
not acquire them through these operations.

| Operation | Accepted behavior |
|---|---|
| `Abs` | Return the nonnegative magnitude. For a format with signed zero, either zero becomes positive zero; for a format with infinities, either infinity becomes positive infinity; a NaN input produces NaN where NaNs are supported. |
| `Min` | Select the numerically lesser value, producing NaN if either operand is NaN where NaNs are supported. For two zeros in a format with signed zero, select negative zero if either operand is negative zero. |
| `Max` | Select the numerically greater value, producing NaN if either operand is NaN where NaNs are supported. For two zeros in a format with signed zero, select positive zero if either operand is positive zero. |
| `Clamp` | Fail with a checked argument failure for numerically reversed bounds, and for NaN bounds where NaNs are supported, before considering the input. With valid bounds, a NaN input produces NaN where supported; otherwise select a bound only for a strictly out-of-range input and return an in-range input unchanged, preserving its zero sign where signed zero exists. Bounds may include infinities where the format supports them. |

Thus `Min(+0.0, -0.0)` selects `-0.0`,
`Max(-0.0, +0.0)` selects `+0.0`, and
`Clamp(-0.0, +0.0, 1.0)` preserves the input's negative zero. `Clamp` is not
defined by composing `Min` and `Max`; that could change an in-range zero's
sign. Valid numerically equal bounds accept an equal input unchanged. A NaN
input does not conceal invalid bounds.

NaN result-class behavior does not select a NaN payload or sign and does not
specify signaling-NaN quieting or floating status effects. The accepted
selection behavior does not change ordinary comparisons, define a floating
total order, or settle the concrete failure transport/effect metadata for
invalid `Clamp` bounds. `Abs` on a signed integer minimum may not be
representable in that type; its per-type result and failure contract remains
open rather than implying widening or wrapping.

## Per-type inventory and generic math

Candidate built-in numeric families include signed and unsigned integers,
native-width integers, IEEE binary `float16`/`float32`/`float64`/`float128`,
`bfloat16`, and IEEE decimal `decimal32`/`decimal64`/`decimal128`
([language specification](../specs/language.md)). These families illustrate
the proposed inventory; exact built-in numeric type membership is still
subject to review. Every type that is a built-in numeric type has all four
selection members, whether or not a particular type is currently implemented.

| Type family | Inventory status |
|---|---|
| Signed and unsigned integer types, including native-width integers | All four members apply; signed-minimum `Abs` behavior remains open. |
| IEEE binary floating types and `bfloat16` | All four members apply; `float32`/`float64` selection behavior is specifically accepted, while support for NaNs, infinities, and signed zero in each other format remains a per-type question. |
| IEEE decimal floating types | All four members apply; representation, cohort, and special-value details remain open where they affect results. |

The exact inventory of built-in numeric types, same-type parameter/result
signatures, and any per-type representation details remain open; the
applicability of these four members to every built-in numeric type is not.

Generic-math hierarchy and membership are **UNAPPROVED**. The minimal
`IScalarNumber<TSelf>` composite sketched in the
[core-library proposal](core-library-reference.md#existing-sampled-declarations)
is one candidate sketch, not the selected hierarchy or a required conformance
for numeric types. Evaluate a more .NET-like layered interface family:
granular operation-focused contracts composed into a small number of
higher-level numeric contracts, with separate layers for integer,
floating-point, classification and format-specific operations where
appropriate. Microsoft's [.NET generic math design](https://learn.microsoft.com/en-us/dotnet/standard/generics/math)
is useful prior art for this layering; it does not establish Terrum's names,
member grouping, constraints or semantics.

The exact interfaces, composition hierarchy, required members and generic
constraints, and mapping from each primitive type to interface witnesses all
remain open. Access through generic-math interfaces is an accepted direction;
it does not imply that any specific interface hierarchy or all required
operations are implemented. For numerically equal values with distinct
representations, such as decimal cohorts, which encoding `Min` or `Max`
returns is not specified by numeric ordering alone.

## Implementation and support boundary

Self-host components implement internal selection helpers for `float32`,
`float64`, `int32` and `int64`; tests cover finite values and selected
special-value and boundary behavior. This is internal helper support only,
not proof that primitive members or generic-math interfaces are implemented.
No implementation support for other numeric types follows from the accepted
semantics. Current component and test coverage is summarized in the
[self-host implementation reference](../../self-host/README.md).

## Separate math contracts

`Sqrt`, `Pow`, `Exp`, `Log`, `Sin`, `Cos`, and `RoundToIntegral` are outside
this selection proposal. Their per-type inventory and contracts—including
domain, special-value, precision, rounding, and failure behavior—remain
separate and open. Strict target-format `float32` arithmetic does not
implicitly reassociate or contract. Native/approximate functions, fused
multiply-add, and reproducible reductions also require distinct contracts and
must not be inferred from selection-member placement or generic-math access.
