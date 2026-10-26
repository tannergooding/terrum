# Bounded conversion and scalar-front-end support

This informative inventory records bounded compiler-support observations,
not the complete source conversion catalog. The
[literal and conversion contracts](../specs/conversions.md),
[primitive identities](../specs/language/primitive-operators.md) and
[floating environment](../specs/language/floating-environment.md) own selected
semantics independently of this implementation's scalar subset or frontend.

## Hosted source conversions

Hosted binding, mandatory verification, scalar folding/ConstEval, and Windows
x64 JIT/AOT lowering implement checked integer conversions across the fixed
8-, 16-, 32-, and 64-bit signed/unsigned types, plus integer-to-`char16`
range checks. Integer-to-`float32`/`float64` includes the narrow integer types.
Floating-to-integer remains limited to `int32`, `uint32`, `int64`, and
`uint64`. Explicit `char16`-to-floating conversion uses the unsigned code-unit
value and is total; it does not decode or validate surrogate pairs as Unicode
scalar values.

The unapproved, provisionally actionable
[`char16` conversion proposal](../proposals/char16-fixed-integer-conversions.md)
recommends checked conversion from `char16` to all ten fixed-width integers,
including `int128` and `uint128`. That source route is implemented in the
hosted verifier, ConstEval, and Windows x64 backend. JIT/AOT tests iterate the
full code-unit domain under both runtime policies, checking every total
16-/32-/64-bit conversion and each representable narrow value; `int128` and
`uint128` calls are exercised, and separate ConstEval tests compare their full
payloads. It remains a provisional implementation direction, not an amendment
to the normative conversion catalog. Narrow checked casts also survive private
semantic-module producer/consumer linking. Standard register-Body conversion
encoding and standalone Terrum-authored frontend support remain separate
unfinished routes. General `[NoThrow]` source annotations are provisionally
retired; conversion totality is determined by the source/destination range
contract.

The [implementation inventory](../implementation.md) owns actual tested
coverage. These projections do not establish general physical conversion,
constant-program, native-layout or exception-transport conformance.

## Initialization and failure support

Boolean requirements, conditional outputs, initialization loans and checked
failures have bounded support tracked in the
[implementation inventory](../implementation.md) and
[output profiles](output-profiles.md). Declarations and calls must agree on
their actual checked signature, but no legacy passing form, retained alpha
artifact or migration shim is a source-language requirement.
