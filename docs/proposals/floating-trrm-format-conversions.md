# Floating format conversions in executable TRRM

**Status: Unapproved**

## Summary

The accepted source conversion policy defines checked numeric conversions
between `float32` and `float64` using IEEE-754 target-format conversion.
Executable TRRM currently has integer-only `Convert` and `CheckedConvert`
contracts; extending those integer opcode meanings to floats would be
implicit. A separate, bounded opcode can carry only the already-defined
floating-format pair.

## Question

Should the unreleased executable `(1, 0)` draft add a distinct operation for
explicit conversion between `float32` and `float64`, without extending
integer conversion policies or deciding other floating conversion pairs?

## Recommendation

Add opcode `0x1A` as `FloatConvert rNew, tDest, rSource`. The source and
destination must be different unqualified floating formats, each exactly
`float32` or `float64`. Conversion follows the accepted nearest/ties-to-even
target-format policy: inexact results are rounded, finite overflow may produce
infinity, and underflow may produce a subnormal or zero. Conversion does not
fail; NaNs and infinities remain floating values. Fixed NaN payload
preservation is not promised. Identity conversions remain ordinary `Copy`
values rather than this operation.

Keep `Convert` and `CheckedConvert` integer-only. This operation does not add
integer-to-floating, `char16`-to-floating, floating-to-integer, mixed-format
arithmetic or floating `ConstEval` support. Preserve source evaluation order
and define each result's exact destination TypeSpec.

Amend the unreleased `(1, 0)` draft rather than inventing a compatibility
reader or incrementing an internal milestone. The metadata design states
that `(1, 0)` is not released and makes no compatibility promise for draft
artifacts. Keep this change unapproved until its contract is accepted; any
post-release evolution must make its version and compatibility policy
explicit.

## Scope and validation

Exercise both conversion directions through linear and indexed Bodies,
including nearest-even ties, finite overflow, underflow, signed zero and
NaN/infinity classification. Compare source-compiled and source-free execution
and native output. Reject integer or `char16` operands, identity/mismatched
destinations, and malformed type rows. Keep existing integer Body bytes
unchanged.

This proposal does not resolve floating arithmetic NaN encodings,
float-to-integer eligibility, `char16`-to-floating eligibility, or
integer-to-floating TRRM conversion.
