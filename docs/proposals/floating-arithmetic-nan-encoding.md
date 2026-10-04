# Floating arithmetic NaN encoding

**Status: Unapproved**

## Summary

The accepted working-profile contract requires arithmetic NaN results to be
canonical, but does not select the exact `float32` or `float64` bit pattern.
Bit-preserving operations remain outside this choice.

## Question

Should Terrum define the canonical arithmetic NaN as the positive quiet-NaN
encoding for each binary format?

## Recommendation

Select `0x7FC00000` for `float32` and `0x7FF8000000000000` for `float64`.
This gives both widths one stable, positive quiet-NaN result without changing
IEEE comparison behavior.

## Scope

This choice applies only to NaNs produced by floating arithmetic. Comparisons
retain IEEE unordered behavior. Bitcasts and copies preserve their original
bits, and numeric conversions retain their separately specified payload
policy. The hosted emitter currently uses the recommended encodings
provisionally; this proposal is not an accepted language-contract change.
