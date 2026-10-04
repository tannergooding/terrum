# Checked floating-to-integer pair eligibility

**Status: unapproved.** This proposal recommends a bounded source/destination
matrix for the already specified checked floating-to-integer value policy. It
does not change accepted conversion semantics or current compiler support.

## Current boundary

The [conversion policy](../conversions.md#conversion-policies) defines checked
floating-to-integer conversion as truncation toward zero followed by a range
check. The truncated result must be representable by the destination; NaN and
out-of-range values, including infinities, fail. Exact source/destination
eligibility remains open. The hosted compiler currently supports floating
format conversion and the four fixed-width integer sources to either floating
format, but not floating-to-integer conversion.

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

If accepted, regression coverage should include fractional values on both
sides of zero, truncated values at each destination boundary, just-outside
values, signed zero, NaN, and both infinities under each supported runtime
policy. Constant folding must match runtime conversion outcomes without
turning a failing checked conversion into a successful constant.

Until this pair matrix is approved, keep the existing implementation boundary
and do not infer support from the policy table alone.
