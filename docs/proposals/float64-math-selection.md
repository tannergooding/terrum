# Float64 math selection candidate

**Status: unapproved proposal.**

## Summary

The core-library reference proposes a `Float32Math` service with candidate
`Abs`, `Min`, `Max` and `Clamp` special-value rules, but it does not propose
the corresponding `Float64Math` service. The language and hosted compiler
already support `float64`, while the bounded self-host library now has a
provisional `float32` selection candidate.

This proposal asks whether to add a closed `Float64Math` candidate with
`float64`-typed `Abs`, `Min`, `Max` and `Clamp`, using the same numeric
selection rules as the proposed `Float32Math` candidate.

## Recommendation

Add the matching-width service candidate. Keep this as a separately reviewed
API proposal: it does not approve primitive intrinsics, total ordering,
`NoThrow` effects, or implicit float conversions. Treat NaN propagation as a
result-class rule only; payload/sign selection, signaling behavior and status
flags remain open until specified.

## Proposed selection rules

| Member | Proposed candidate behavior |
|---|---|
| `Abs` | Return the exact nonnegative magnitude of a finite value; either zero becomes positive zero and either infinity becomes positive infinity. NaN produces NaN. |
| `Min` | Propagate NaN if either operand is NaN; otherwise select the numerically lesser value. For two zeros, return negative zero when either input is negative zero. |
| `Max` | Propagate NaN if either operand is NaN; otherwise select the numerically greater value. For two zeros, return negative zero only when both inputs are negative zero. |
| `Clamp` | First reject a NaN bound or numerically reversed bounds as a checked argument failure. After valid bounds, NaN input produces NaN; otherwise select a bound only when the input is strictly outside the closed interval, preserving an in-range input and its zero sign. Infinite bounds are permitted. |

As with the `float32` candidate, NaN payload/sign retention, signaling-NaN
quieting and floating status effects are not specified. This proposal does
not change ordinary IEEE comparisons or define a floating total-order
witness. `Sqrt`, `Pow`, `Exp`, `Log`, `Sin`, `Cos` and `RoundToIntegral`
remain separate candidates with their own domain, precision and rounding
contracts.
