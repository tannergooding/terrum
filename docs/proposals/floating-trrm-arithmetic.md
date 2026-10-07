# Floating arithmetic in executable TRRM

**Status: Unapproved**

## Summary

The accepted source working profile includes same-format `float32` and
`float64` addition, subtraction, multiplication and division. Executable TRRM
does not yet encode those operations. The hosted native emitter already
implements them with controlled floating-point environment behavior. An
arithmetic NaN result is guaranteed to remain a NaN, but its sign and payload
are unspecified; implementations should propagate and quiet an input NaN when
practical.

## Question

Should the unreleased executable `(1, 0)` draft add a distinct operation for
same-format floating binary arithmetic under the accepted NaN-result-class
contract?

## Recommendation

Add opcode `0x1D` as `FloatBinary rNew, kind, rLeft, rRight`. Both operands
must have the same exact type, `float32` or `float64`, and the result has that
same type. Kinds 1 through 4 mean addition, subtraction, multiplication and
division, respectively. The operation is total. Any arithmetic NaN result
is a NaN, but its sign and payload are unspecified. An implementation should
propagate and quiet an input NaN when practical; no exact payload or cross-
implementation propagation choice is promised.

This does not add remainder, mixed-format arithmetic, conversion, optional
floating payloads or floating `ConstEval`. It does not change the approved IL
specifications. Amend the unreleased `(1, 0)` draft without inventing a
compatibility reader or incrementing the internal patch moniker; any
post-release evolution must make its version and compatibility policy
explicit.

## Scope and validation

Exercise all four operations in both formats through linear and indexed
Bodies, including signed zero, infinities, invalid-operation/division-by-zero
NaNs and input NaN payloads. Compare finite source-compiled and source-free
native output bits, verify NaN result classification and practical input-
payload propagation under the native emitter's controlled floating-point
environment. Keep floating remainder and existing integer Body bytes
unchanged.
