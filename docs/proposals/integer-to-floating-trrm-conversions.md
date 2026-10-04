# Integer-to-floating conversions in executable TRRM

**Status: Unapproved**

## Summary

The accepted source conversion policy includes explicit conversions from
`int32`, `uint32`, `int64` and `uint64` to either `float32` or `float64`.
Executable TRRM currently has no operation for those pairs. They must not be
implicitly added to integer-only `Convert` or the separate floating-format
conversion operation.

## Question

Should the unreleased executable `(1, 0)` draft add a distinct operation for
the eight already-defined integer-to-floating conversion pairs?

## Recommendation

Add opcode `0x1B` as `IntToFloat rNew, tDest, rSource`. The source must be
exactly `int32`, `uint32`, `int64` or `uint64`; the destination must be exactly
`float32` or `float64`. The operation is total and follows the accepted
nearest/ties-to-even target-format conversion policy. It does not add
`char16`-to-floating, floating-to-integer, identity conversions, or optional
payloads. Keep integer `Convert` and `CheckedConvert` integer-only.

This operation is provisional and does not change the approved IL
specifications. Amend the unreleased `(1, 0)` draft without inventing a
compatibility reader or incrementing the internal patch moniker; any
post-release evolution must make its version and compatibility policy
explicit.

## Scope and validation

Exercise all eight pairs through linear and indexed Bodies, including signed
and unsigned boundaries and a nearest-even rounding case. Compare the
source-compiled and source-free native results. Reject `char16`, floating
sources, integer destinations, and malformed destination TypeSpecs. Keep
existing integer Body bytes unchanged.
