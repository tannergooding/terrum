# Optional scalar TRRM execution boundary

**Status: implementation-only support under the assigned Body contract; no
language-design change.** Current coverage and implementation limits are
summarized in the [implementation reference](../implementation.md#executable-trrm-scalar-interchange).

## Contract and support boundary

The [instruction-set TypeSpec rules](../specs/il-instruction-set.md#type-and-definition-model-for-operands)
assign optional types, and the [operation table](../specs/il-instruction-set.md#operation-encoding-and-normalized-effects)
assigns `MakeOptional`, `None`, `IsPresent`, `Copy` and eager `Select`.
Implemented execution is limited to Body-local optional values with supported
root-free scalar payloads. Optional floats remain rejected, and optional
`char16` remains limited to its assigned operations and equality.

There are no optional source types or source type-system mapping, optional
`MethodSig`/`Param`/`Call` signatures, or native ABI support. Optional managed
handles, checked-exception execution and broader optional operators also
remain outside the implementation boundary.
