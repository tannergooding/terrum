# Char16-to-floating conversion eligibility

**Status: unapproved eligibility proposal.** The hosted binder, verifier,
emitter and v85 scalar-frontend projection provisionally implement the
recommendation below. Verification and emission remain hosted. This proposal
does not add Unicode scalar conversion, implicit numeric conversion, or a
language-contract approval.

## Summary

`char16` stores one unsigned UTF-16 code unit, including isolated surrogate
values. The current explicit conversion policy supports fixed-width integer
sources to `float32` and `float64`, while the hosted implementation previously
excluded `char16` as a source.

## Recommendation

Allow explicit `float32(value)` and `float64(value)` when `value` has type
`char16`, interpreting it as its unsigned code-unit value in `[0, 65535]`.
Every such value is exactly representable in both floating formats, so the
conversion cannot fail and is valid in `[NoThrow]`. It does not decode,
validate, or combine UTF-16 code units into Unicode scalar values.

Keep this pair eligibility provisional until approved. Direct floating-to-
`char16` conversion and implicit conversions remain excluded.
