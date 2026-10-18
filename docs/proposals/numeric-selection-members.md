# Built-in numeric selection members

**Status: logical scalar members, result/failure rules and numeric capability
groups are accepted. Physical registration and implementation remain
incomplete; additional numeric APIs are unapproved.**

## Scope and decision boundary

This is an integration follow-up, not a competing definition of the adopted
members. Use these owners rather than copying their signatures or value tables:

| Accepted fact | Canonical owner |
|---|---|
| Mandatory scalar identities and required operator matrix | [Scalar catalog](../specs/overview/conformance.md#mandatory-scalar-catalog), [primitive operators](../specs/language/primitive-operators.md) |
| Same-type static Abs on all 20 numeric scalars; checked signed minimum | [Absolute value](../specs/language/absolute-value.md) and exact [scalar overflow payload](../specs/language/scalar-failures.md) |
| Same-type Min/Max/Clamp, representation ties, ordered-bound check and exact InvalidBoundsException | [Numeric selection](../specs/language/numeric-selection.md) |
| Sign-only operations, quiet processing results and deterministic constant NaNs | [Floating environment](../specs/language/floating-environment.md); the [fixed-runtime-encoding alternative](floating-arithmetic-nan-encoding.md) is not adopted |
| Exact Core interfaces, constraints, inherited requirements and primitive instances | [Numeric capabilities](../specs/language/numeric-capabilities.md), including one Min/Max/Clamp group |

These selections do not adopt the remaining
[core-library API inventory](core-library-reference.md#6-numerics-and-conversions),
register an intrinsic by name or grant Pure/ConstEval/NoThrow.

## Recommended integration

Recommend ordinary primitive members and checked nominal witnesses, reusing
ordinary calls rather than introducing one opcode per member, scalar width
or generic interface. Complete programs/helpers still implement each
selected format, target width, effect and failure contract. A specialized
instruction may replace such a call only under those same observations.

The [generic nominal metadata proposal](generic-nominal-construction-metadata.md)
owns candidate interface/witness transport. The
[logical numeric records](../specs/representation/logical-records.md#floating-representation-and-numeric-witnesses-encoding-pending)
and [numeric selection records](../specs/representation/logical-records.md#numeric-selection-and-bounds-failures-encoding-pending)
name the obligations that physical integration must cover. Extend
[metadata](../specs/il-metadata.md), [clause applicability](../specs/representation/contract-clauses.md),
[binding/replacement](../specs/representation/binding-and-replacement.md),
the [registry](../specs/representation/registry.md) and manifest capability
closure together after approval. This file assigns no additional schema or ID.

Do not invent a parallel numeric-only failure or ownership codec. Use the
[joint output/failure/ownership design](trrm-output-failure-ownership.md)
for member-result publication, exact precondition/construction origin,
event/cause responsibility and exceptional cleanup, and the
[proof/constant follow-up](static-proof-and-constant-evaluation.md)
for evaluation forms, programs, value codecs and resource recipes.
Missing physical support is an implementation/integration gate, not an
unresolved choice about the accepted signatures.

A broader number-root redesign would change the accepted grouping and caller
requirements, not just reduce declaration repetition. No such redesign is
recommended here: the smaller independent groups already support constrained
algorithms, and the optional composite adds no new operation. The older
three-member IScalarNumber and heterogeneous candidates in the core inventory
are explicitly unadopted alternatives, not a second Core definition.

## Conformance and support boundary

Self-host components implement internal selection helpers for `float32`,
`float64`, `int32` and `int64`; tests cover finite values and selected
special-value and boundary behavior. This is internal helper support only,
not proof that primitive members or generic-math interfaces are implemented.
No implemented primitive export, exact Core failure event or source-free
witness codec follows from those helpers. Current support belongs in the
[self-host implementation reference](../implementation/self-host.md).

Use the accepted [Abs](../specs/conformance/source-absolute-value.md),
[selection](../specs/conformance/source-numeric-selection.md),
[floating](../specs/conformance/source-floating-environment.md) and
[capability](../specs/conformance/source-numeric-capabilities.md) cases.
Physical acceptance additionally needs independent producer/consumer cases
for exact identities and full witness closure, all integer data/count
instances, native versus fixed-width identity, missing/changed members,
parameter labels, fixed bounds failure, argument effects and failed-result
vacancy. Include signed minima, NaN bounds versus NaN values, both zero signs,
decimal cohort ties and unchanged in-range Clamp representations.
Finite helper tests are not that evidence.

## Separate candidate APIs

Wrapping/saturating/unsigned magnitudes and TryAbs remain different
unapproved operations under [Abs alternatives](../specs/language/absolute-value.md#5-explicit-alternatives).
Candidate TryAbs, Sign, DivRem, BigMultiply and GreatestCommonDivisor
signatures and boundaries belong in the
[core scalar inventory](core-library-reference.md#numeric-conversion-and-scalar-operation-signatures),
not in a second selection catalog here.
Sqrt, Pow, Exp, Log, Sin, Cos, RoundToIntegral, FMA, approximate variants and
reproducible reductions still require their own per-format domain,
special-value, rounding, precision and failure contracts. The accepted
selection/grouping decisions settle none of those APIs.
