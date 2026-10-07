# Terrum specifications

These documents define accepted target contracts. They are normative design
references, not inventories of compiler support; see the
[implementation summary](../implementation.md) for implemented boundaries.
Unapproved alternatives belong in [proposals](../proposals/README.md).

## Source language

| Contract | Specification |
|---|---|
| Language design and accepted direction | [Language](language.md) |
| Source productions and semantic constraints | [Grammar](grammar-spec.md) |
| Tokens and lexical rules | [Lexical grammar](lexical-grammar.md) |
| Cross-cutting source contracts | [Language contracts](language-contracts.md) |
| Type families and nominal operations | [Type families](type-families.md) |
| Numeric and character conversions | [Conversions](conversions.md) |
| Lifetimes and access | [Lifetimes](lifetimes.md) |
| Ownership, cleanup, and execution | [Memory model](memory-model.md) |
| Layout and calling convention | [Data layout and ABI](data-layout-and-abi.md) |

## Persistent representation

| Contract | Specification |
|---|---|
| TRRM metadata tables and semantic contracts | [IL metadata](il-metadata.md) |
| Body encoding and executable subset | [IL instruction set](il-instruction-set.md) |

## Modules and native execution

| Contract | Specification |
|---|---|
| Native runtime profiles and ABI | [Native profile](native.md) |
| JIT invocation and replacement proof | [JIT](jit.md) |
| Semantic modules | [Modules](modules.md) |
| Producer/consumer module sets | [Module sets](module-sets.md) |
| Explicit native dependency graphs | [Native graphs](native-graph.md) |

## Tooling protocol

| Contract | Specification |
|---|---|
| CLI status, JSON schema, coordinates, and diagnostics | [CLI machine protocol](cli-protocol.md) |

## Supporting references

[Design references and provenance](references.md) records external material
used when developing these contracts; it does not independently define
Terrum behavior.
