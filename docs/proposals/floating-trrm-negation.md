# Floating negation in executable TRRM

**Status: Unapproved**

## Summary

The accepted source contract includes unary negation for `float32` and
`float64`. Executable TRRM currently supports floating constants, comparisons
and several provisional conversions, but has no operation for negating a
runtime floating value.

## Question

Should the unreleased executable `(1, 0)` draft add a distinct total operation
for same-format floating negation?

## Recommendation

Add opcode `0x1C` as `FloatNegate rNew, rSource`. The source and result have
the same exact type, either `float32` or `float64`. Negation toggles only the
sign bit, preserving every other payload bit, including NaN payloads. It is
total, does not perform floating arithmetic, and does not select or depend on
the canonical arithmetic-NaN encoding.

This operation is provisional and does not change the approved IL
specifications. Amend the unreleased `(1, 0)` draft without inventing a
compatibility reader or incrementing the internal patch moniker; any
post-release evolution must make its version and compatibility policy
explicit.

## Scope and validation

Exercise both floating formats through linear and indexed Bodies with positive
and negative zero, infinities and NaNs with noncanonical payloads. Verify the
exact output bits through source-compiled and source-free native execution.
Keep floating binary arithmetic unsupported and existing integer Body bytes
unchanged.
