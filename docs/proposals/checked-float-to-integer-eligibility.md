# Checked floating-to-integer pair eligibility

**Status: unapproved.** This proposal recommends a bounded source/destination
matrix for the already specified checked floating-to-integer value policy. The
recommended pairs have provisional hosted compiler support, but implementation
does not approve the eligibility choice or change the accepted contract.

## Current boundary

The [conversion policy](../conversions.md#conversion-policies) defines checked
floating-to-integer conversion as truncation toward zero followed by a range
check. The truncated result must be representable by the destination; NaN and
out-of-range values, including infinities, fail. Exact source/destination
eligibility remains open. The hosted compiler supports floating format
conversion and the four fixed-width integer sources to either floating format.
It now also provisionally implements the recommended floating-to-fixed-integer
pairs; the Terrum-authored scalar frontend remains unsupported for these
conversions.

## Question for direction

### Which checked floating-to-integer pairs should `T(value)` support?

**Summary:** The value policy is settled, but the exact eligibility matrix is
not. `char16` also has an existing checked conversion contract for integer
sources, while its role as a destination for floating values has not been
selected.

**Recommendation:** Add explicit checked conversions from `float32` and
`float64` to `int32`, `uint32`, `int64`, and `uint64`. Keep direct
floating-to-`char16` conversion outside the initial matrix; its code-unit
range is distinct from the fixed-width integer domain. Preserve the existing
checked behavior of `T(value)`: truncate first, then range-check the
truncated value, with NaN and infinities failing. Do not add implicit
conversions, alternate rounding modes, or unchecked/saturating syntax as part
of this slice.

Regression coverage for the provisional implementation includes fractional
values on both sides of zero, truncated values at destination boundaries,
just-outside values, signed zero, and NaN/infinities through checked exception
recovery. Successful compiled-scanner execution is checked under both runtime
policies. Constant folding must match runtime conversion outcomes without
turning a failing checked conversion into a successful constant.

Keep the implementation status distinct from approval: do not infer that this
pair matrix has been accepted as a language contract from its implementation
or from the general conversion policy alone.
