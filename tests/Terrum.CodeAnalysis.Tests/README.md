# Terrum.CodeAnalysis.Tests

This is the C# NUnit test project for the hosted compiler, CLI, and emitted
Terrum programs. C# files are test harnesses and assertions; Terrum inputs,
fixtures, and independent ABI probes are kept with the test area that owns
them. The layout describes test purpose rather than duplicating a second
compiler project.

| Directory | Test purpose |
|---|---|
| `Cli` | Command behavior, protocols, and supervised workers |
| `Syntax`, `Lexing`, `Semantics`, `Utilities` | Hosted language services and focused compiler behavior |
| `Modules` | Dependency and module API behavior |
| `Native` | Native JIT/AOT execution, ABI, ownership, storage, and unwind tests |
| `Infrastructure` | Shared test paths, artifact handling, cleanup, and assertions |

Test inputs live under the relevant area: syntax fixtures are in
`Syntax/Fixtures`; native Terrum fixtures and probes are in named
`Native/<area>/Fixtures` and `Native/<area>/Probes` folders. The small C probe
set is retained only for independently observing native unwind, SEH, and
stack behavior that ordinary Terrum/C# assertions cannot establish; it is
not a C application library for language-feature coverage. See
[native qualification](../../docs/building.md#native-qualification) for each
probe's consumer and evidence boundary.
