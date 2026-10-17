# Built-in numeric selection members

**Status: Scalar Abs/Min/Max/Clamp signatures, checked magnitude and
InvalidBoundsException and totalOrder numeric ties selected; floating
representation/results and grouped generic capabilities also selected.**

## Scope and decision boundary

The accepted member placement, generic-math access, universal applicability,
and `Abs`, `Min`, `Max`, and `Clamp` semantics are specified in
[scalar mathematics, keys and conversion](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
The canonical [absolute-value member contract](../specs/language/absolute-value.md)
additionally selects the exact numeric Abs subjects, same-type signature
and checked signed-minimum failure.
The canonical [numeric selection contract](../specs/language/numeric-selection.md)
selects the remaining same-type member signatures, ordinary call/state
contract, totalOrder numeric representation ties and explicit
InvalidBoundsException for Clamp's bound relationship.
This proposal records unresolved details only. Those accepted directions do
not approve the general library specification or unrelated candidate APIs.
The [floating-environment contract](../specs/language/floating-environment.md)
selects sign-only Abs/negation, quiet arithmetic/conversion/selection NaNs,
canonical admitted constant processing results and no ambient status facility.
[Numeric capabilities](../specs/language/numeric-capabilities.md) select the
coherent Core group shapes and primitive membership, with Min/Max/Clamp together.

All four members use their selected ordinary static call contracts.
This proposal does not
select primitive-definition registration, intrinsics or implementation support.

## Per-type inventory and generic math

The [mandatory scalar catalog](../specs/overview/conformance.md#mandatory-scalar-catalog)
already selects the numeric identities: fixed/native signed and unsigned
integers, five binary floating formats and three IEEE decimal formats.
Each numeric type has the four accepted selection-member directions.
All four exact same-type signatures are selected; remaining per-family
questions are:

| Type family | Remaining details |
|---|---|
| Signed and unsigned integer types, including native-width integers | Actual complete effect/program and physical member/witness/publication support. Abs is checked at the signed minimum; invalid Clamp bounds use InvalidBoundsException; no unchecked variant is selected. |
| IEEE binary floating types and `bfloat16` | Actual complete programs and physical support for the selected sign/quiet-NaN/constant contracts. No ambient status API is selected. |
| IEEE decimal floating types | Actual complete programs and physical support. Abs preserves quantum, Min/Max same-sign ties follow totalOrder, and Clamp preserves its selected non-NaN input representation. |

The [scalar failure catalog](../specs/language/scalar-failures.md)
selects exact Terrum.OverflowException for checked Abs overflow. Invalid
Clamp bounds explicitly use exact Terrum.InvalidBoundsException under the
ordinary requirement-construction contract. Actual helper/effect and
physical program/transport support stay separate; this proposal cannot
infer them from a CLR exception name.

An explicitly selected wrapping magnitude could return a signed minimum
unchanged, matching the width-limited magnitude bits. That considered
alternative is not ordinary Abs's nonnegative-result contract. Its API name,
policy case, generic witness and performance characteristics are unselected.

The accepted layered generic-math direction is recorded in the canonical
[scalar mathematics section](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
The exact selected interface names, shapes, membership, required members,
constraints and primitive witness matrix are in the
[numeric capability chapter](../specs/language/numeric-capabilities.md).
The older minimal `IScalarNumber<TSelf>` composite sketched in the
[core-library proposal](core-library-reference.md#existing-sampled-declarations)
is an unadopted earlier alternative, not another definition of the selected
Core composite.
Microsoft's [.NET generic math design](https://learn.microsoft.com/en-us/dotnet/standard/generics/math)
is prior art, not a selection of Terrum names, member grouping, constraints,
or semantics. For numerically equal finite values with distinct representations
within one decimal cohort, Min selects the representation earlier in IEEE
754-2019 totalOrder and Max the later. NaN propagation and opposite-sign-zero
preference precede that tie-break. The rule does not order distinct encodings
that totalOrder does not distinguish or select a public total-order variant.

## Implementation and support boundary

Self-host components implement internal selection helpers for `float32`,
`float64`, `int32` and `int64`; tests cover finite values and selected
special-value and boundary behavior. This is internal helper support only,
not proof that primitive members or generic-math interfaces are implemented.
No implementation support for other numeric types follows from the accepted
semantics. Current component and test coverage is summarized in the
[self-host implementation reference](../implementation/self-host.md).

## Separate math contracts

`Sqrt`, `Pow`, `Exp`, `Log`, `Sin`, `Cos`, and `RoundToIntegral` are outside
this selection proposal. Their per-type inventory and contracts—including
domain, special-value, precision, rounding, and failure behavior—remain
separate and open. Strict target-format `float32` arithmetic does not
implicitly reassociate or contract. Native/approximate functions, fused
multiply-add, and reproducible reductions also require distinct contracts and
must not be inferred from selection-member placement or generic-math access.
