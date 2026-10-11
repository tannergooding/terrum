# Bounded conversion and scalar-front-end support

This informative inventory records bounded compiler-support observations,
not the complete source conversion catalog. The
[literal and conversion contracts](../specs/conversions.md),
[primitive identities](../specs/language/primitive-operators.md) and
[floating environment](../specs/language/floating-environment.md) own selected
semantics independently of this implementation's scalar subset or frontend.

## Native scalar front end

Target-typed floating literals,
`float32`/`float64` conversions, and conversions from `int32`, `uint32`,
`int64`, and `uint64` to either floating format are implemented. Conversions
from either floating format to each current integer primitive -- `int32`,
`uint32`, `int64`, and `uint64` -- are accepted as checked conversions. The
hosted implementation and v85 scalar-frontend projections remain provisional;
verification and emission remain hosted. Direct conversion to `char16` is not
included: it is a code-unit type, not an integer primitive. Explicit
conversion from `char16` to either floating format is accepted as conversion
of its unsigned code-unit value; every value is exact in both formats, so the
conversion is total and valid in `[NoThrow]`. This does not decode or validate
surrogate pairs as Unicode scalar values. Its hosted implementation and v85
scalar-frontend projection remain provisional; verification and emission
remain hosted.

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
