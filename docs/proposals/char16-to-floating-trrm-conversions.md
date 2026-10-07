# Char16-to-floating conversions in executable TRRM

**Status: Unapproved**

## Summary

Explicit language conversion from `char16` to `float32` or `float64` is
accepted as conversion of the unsigned UTF-16 code-unit value. Executable TRRM
provisionally encodes this pair with opcode `0x1E` in both linear and indexed
Bodies; that encoding remains unapproved.

## Question

Should this provisional opcode be adopted in the executable TRRM contract?
Its current implementation is an unreleased draft choice, not an approved IL
specification; the source-language eligibility is settled separately.

## Recommendation

Add opcode `0x1E` as `Char16ToFloat rNew, tDest, rSource`. The source must be
exactly `char16`; the destination must be exactly `float32` or `float64`. It
interprets the code unit as an unsigned value in `[0, 65535]`, without UTF-16
decoding or surrogate validation. Every value is exactly representable in
either floating format, so conversion is total and suitable for `[NoThrow]`.
Keep `IntToFloat` and integer `Convert` operations restricted to their stated
integer source types.

This operation is provisional and does not change the approved IL
specifications or alter the accepted source-language contract. Amend the
unreleased `(1, 0)` draft without inventing a compatibility reader or
incrementing the internal patch moniker; any post-release evolution must make
its version and compatibility policy explicit.

## Scope and validation

Exercise zero, surrogate code units and `0xFFFF` through linear and indexed
Bodies. Reject integer or floating sources and integer destinations. Keep
integer and `IntToFloat` Body bytes unchanged.

The current regression covers zero, surrogate and maximum code-unit values
for both floating destinations through linear and indexed Bodies, and rejects
an integer destination. The reader checks that the source is exactly `char16`
and the destination exactly `float32` or `float64`; integer conversion
opcodes retain their existing source-type restrictions.
