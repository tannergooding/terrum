# Tooling protocols

This partition owns selected machine-facing tooling contracts independently
of current commands, worker support or private artifact serialization.

| Chapter | Canonical responsibility |
|---|---|
| [CLI machine protocol](../cli-protocol.md) | Exact versioned envelope, command/results, diagnostics and coordinates, CLI status versus normal program return data, separate program output and explicit supervision |
| [Source input](../language/source-input.md) | Strict compilation-byte decoding, immutable lexical snapshots and byte/code-unit/origin coordinate authority used by tooling |
| [Source documentation](../language.md#documentation-comments) | Source attachment and selected structured documentation; prose is not a compiler proof or capability grant |
| [Tooling cases](../conformance/tooling-cli-protocol.md) | Framing, duplicate/unknown data, extents, signed return values, output separation and supervised publication obligations |

Additional commands or schemas require their own complete adoption; an
implementation inventory cannot extend the selected protocol by name.
The [persistence taxonomy](../representation/persistence.md) keeps machine
responses distinct from standard semantic interchange and private payload
formats.

The [experimental CLI responses](../../implementation/cli-protocol.md) and
[development JIT worker](../../implementation/jit.md) are informative
implementation references. Their raw return statuses and legacy envelopes
do not redefine the selected supervisor's outcomes or version handling.
