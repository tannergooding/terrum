# Floating-to-integer conversions in executable TRRM

**Status: Unapproved**

## Summary

The hosted compiler and source-free executable TRRM provisionally implement
explicit checked conversions from `float32` and `float64` to `int32`,
`uint32`, `int64` and `uint64`. Eligibility for these eight language
conversions is accepted; this proposal remains unapproved and does not approve
a stable TRRM encoding.

## Question

Should the unreleased executable `(1, 0)` draft encode those eight accepted
language pairs as a fail-fast operation, without changing TRRM's fail-fast-only
policy?

## Recommendation

Add opcode `0x1F` as `FloatToInteger rNew, tDest, rSource`. The source must be
exactly `float32` or `float64`; the destination must be exactly `int32`,
`uint32`, `int64` or `uint64`. Convert by truncating toward zero, then range
check the truncated value. NaN, infinities and out-of-range results fail with
the existing fail-fast overflow status `0xE0540001`. The native conversion
must isolate and restore the caller's floating-point environment as the
hosted emitter already does.

This may-fail operation cannot satisfy `NoThrow` when retained. Do not add
direct conversion to `char16`, checked-exception control flow, implicit
conversion, alternate rounding, or saturating behavior. Keep integer
`Convert` and `CheckedConvert` semantics unchanged.

This operation is provisional and does not change the approved IL
specifications. Amend the unreleased `(1, 0)` draft without inventing a
compatibility reader or incrementing the internal patch moniker; any
post-release evolution must make its version and compatibility policy
explicit.

## Scope and validation

Exercise all eight pairs through linear and indexed Bodies, including
fractional truncation, signed zero and representable destination boundaries.
Verify NaN, infinity and out-of-range inputs use the existing overflow
fail-fast result; reject invalid type pairs and retained `NoThrow` use. Keep
checked-exception TRRM unsupported and existing integer Body bytes unchanged.
