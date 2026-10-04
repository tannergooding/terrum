# Char16-to-floating conversions in executable TRRM

**Status: Unapproved**

## Summary

The hosted implementation provisionally supports explicit conversion from
`char16` to `float32` or `float64` as the unsigned UTF-16 code-unit value.
Eligibility remains unapproved in the separate
[source-conversion proposal](char16-floating-conversion-eligibility.md), and
executable TRRM has no operation for this pair.

## Question

Should the unreleased executable `(1, 0)` draft encode the two provisional
`char16`-to-floating pairs without widening integer conversion operations?

## Recommendation

Add opcode `0x1E` as `Char16ToFloat rNew, tDest, rSource`. The source must be
exactly `char16`; the destination must be exactly `float32` or `float64`. It
interprets the code unit as an unsigned value in `[0, 65535]`, without UTF-16
decoding or surrogate validation. Every value is exactly representable in
either floating format, so conversion is total and suitable for `[NoThrow]`.
Keep `IntToFloat` and integer `Convert` operations restricted to their stated
integer source types.

This operation is provisional and does not change the approved IL
specifications or resolve the separate source eligibility proposal. Amend the
unreleased `(1, 0)` draft without inventing a compatibility reader or
incrementing the internal patch moniker; any post-release evolution must make
its version and compatibility policy explicit.

## Scope and validation

Exercise zero, surrogate code units and `0xFFFF` through linear and indexed
Bodies. Reject integer or floating sources and integer destinations. Keep
integer and `IntToFloat` Body bytes unchanged.
