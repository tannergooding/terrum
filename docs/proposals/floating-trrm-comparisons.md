# Floating comparisons in executable TRRM

**Status: Unapproved**

## Summary

The approved metadata design assigns atomic TypeSpec identities to
`float32` (`0x14`) and `float64` (`0x15`), but the executable `(1, 0)` Body
profile excludes floating constants and operations. The existing integer
`Binary` kinds must not acquire floating meanings implicitly. Same-format
floating comparisons already have hosted source, verifier and native-backend
semantics, so a bounded source-free comparison slice can reuse those meanings
without deciding floating arithmetic's provisional NaN result encoding.

## Question

Should the draft executable `(1, 0)` profile add raw-bit floating constants,
`float32`/`float64` scalar signatures and a separately assigned comparison
opcode, before the metadata design is released?

## Recommendation

Add opcode `0x19` as `FloatCompare rNew, kind, rLeft, rRight`. Kinds `1..6`
mean equality, inequality, less-than, less-than-or-equal, greater-than and
greater-than-or-equal. Both operands must have the same complete TypeSpec,
which must be unqualified `float32` or `float64`; the result is `bool`.
Comparisons follow the existing IEEE source contract: NaN is unordered,
`==` is false and `!=` is true for NaN, and positive and negative zero compare
equal. Do not extend integer `Binary` kinds.

Permit `ConstBits` for `float32` and `float64` as the exact declared-width IEEE
bit pattern, encoded using the existing minimal unsigned payload width.
Loading zero-extends the payload to the TypeSpec width and preserves signed
zero and every NaN payload bit. Keep `ConstSigned`, floating arithmetic,
unary operations, mixed-format operations and numeric conversions outside
this slice. Float callable parameters and results, `Copy` and eager `Select`
use their existing type-directed contracts.

Amend the unreleased `(1, 0)` draft rather than inventing a compatibility
reader or incrementing an internal milestone. The metadata design explicitly
states that `(1, 0)` is not released and makes no compatibility promise for
draft artifacts. Preserve every existing `(1, 0)` acceptance and rejection
rule except for these newly specified forms. Any post-release evolution must
make its version and compatibility policy explicit.

## Scope and validation

Exercise both floating widths, all six predicates, finite boundaries, signed
zero and NaN through independent compact-linear and compact-indexed Bodies.
Reject wrong operand types, mixed formats, invalid kinds, noncanonical
constant widths and unsupported floating arithmetic/conversion opcodes.
Compare source-compiled and source-free JIT/native results, and verify that
existing integer Body bytes and execution remain unchanged.

This proposal does not resolve floating arithmetic NaN encodings,
`char16`-to-floating eligibility, floating-to-integer TRRM encoding, or
general TRRM floating arithmetic.
