# Built-in numeric selection members

**Status: pending exact numeric-type inventory, signatures and generic-interface
design.**

## Scope and decision boundary

The accepted member placement, generic-math access, universal applicability,
and `Abs`, `Min`, `Max`, and `Clamp` semantics are specified in
[scalar mathematics, keys and conversion](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
This proposal records unresolved details only. Those accepted directions do
not approve the general library specification or unrelated candidate APIs.

Call spellings are illustrative, not a complete API. This proposal does not
select declaration syntax, overload resolution, primitive-definition
authority, intrinsics, or implementation support.

## Per-type inventory and generic math

Candidate built-in numeric families include signed and unsigned integers,
native-width integers, IEEE binary `float16`/`float32`/`float64`/`float128`,
`bfloat16`, and IEEE decimal `decimal32`/`decimal64`/`decimal128`
([language specification](../specs/language.md)). This candidate list does
not select the exact built-in numeric inventory. Each built-in numeric type
has the four accepted selection members; the remaining per-family questions
are:

| Candidate type family | Unresolved details |
|---|---|
| Signed and unsigned integer types, including native-width integers | The result or failure contract for `Abs` of a signed minimum that is not representable in its type. |
| IEEE binary floating types and `bfloat16` | Whether each format supports NaNs, infinities, and signed zero. |
| IEEE decimal floating types | Representation, cohort, and special-value details where they affect results. |

The exact inventory, same-type parameter/result signatures, and per-type
representation details remain open. The checked failure transport and effect
metadata for invalid `Clamp` bounds also remain open.

The accepted layered generic-math direction is recorded in the canonical
[scalar mathematics section](../specs/libraries.md#scalar-mathematics-keys-and-conversion).
The exact interface names, shape, membership, required members, constraints,
and mapping from primitive types to interface witnesses remain unapproved.
The minimal `IScalarNumber<TSelf>` composite sketched in the
[core-library proposal](core-library-reference.md#existing-sampled-declarations)
is only a candidate, not the selected hierarchy or a required conformance.
Microsoft's [.NET generic math design](https://learn.microsoft.com/en-us/dotnet/standard/generics/math)
is prior art, not a selection of Terrum names, member grouping, constraints,
or semantics. For numerically equal values with distinct representations,
such as decimal cohorts, which encoding `Min` or `Max` returns is unspecified.

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
