# Terrum

Terrum (TAIR-um), a programming language from TerraFX.

This repository hosts the language design and the C# bootstrap compiler/tooling.
It is experimental; source syntax and public APIs are not stable.

The running slice is a Unicode-aware lexer, lossless concrete syntax tree,
recovering parser and syntax CLI. It does not execute Terrum or check names,
types, ownership or public contracts.

## Build

Use the exact .NET 11 RC SDK specified in `global.json`. The default commands
use an existing installation; `-architecture x64` acquires that version under
`artifacts\dotnet` instead, without changing the system SDK installation.

```powershell
.\restore.cmd
.\build.cmd
.\test.cmd
.\pack.cmd
dotnet run --project sources\terrumc -- check samples\syntax\hello.terrum
dotnet run --project sources\terrumc -- tree samples\syntax\control-flow.terrum --json
```

The wrappers use `scripts\build.ps1`, copied from TerraFX along with the build
props/targets, EditorConfig and public signing key. Actions are explicit:
`.\build.cmd -restore -test -configuration Release` restores, builds and tests;
`.\pack.cmd` packages an existing build without publishing it. Direct
`dotnet build Terrum.slnx` and `dotnet test Terrum.slnx` remain supported.
See [build infrastructure](docs/building.md) for the few Terrum-specific changes.

Outputs go to `artifacts\bin\<category>\<project>\<configuration>\<framework>`,
intermediates to `artifacts\obj`, binary logs to `artifacts\log`, test results
to `artifacts\tst` and packages to `artifacts\pkg`. Builds enforce TerraFX's
analyzer/style policy, XML documentation, nullable analysis, public signing and
trimming/AOT compatibility. Host overflow checks are enabled in Debug as in
TerraFX; this is not the implementation of Terrum numeric semantics.

## Layout

| Location | Status |
|---|---|
| `sources/Terrum.CodeAnalysis` | Reusable C# source/lexing/syntax API |
| `sources/terrumc` | Human/JSON command-line consumer |
| `tests/Terrum.CodeAnalysis.Tests` | Executable host and process-level CLI tests |
| `samples/syntax` | Syntax fixtures accepted by the current parser; not executable Terrum |
| `samples/design`, `library` | Broader language/API sketches, not implemented libraries |
| `self-host` | Candidate Terrum compiler components with explicit execution gates |

Read the [language design](docs/language.md), [lifetime contracts](docs/lifetimes.md),
[standard-library design](docs/libraries.md), [sample catalog](docs/samples.md),
[syntax API and support matrix](docs/implementation.md), and [roadmap](docs/roadmap.md).
The repository is the canonical design/development location.

The bootstrap path targets .NET CLI IL, not LLVM. Terrum semantic IL will remain
distinct from its eventual CLI lowering.

For machine consumers use the built `artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll`
rather than mixing `dotnet run` build messages with protocol output. CLI exit
codes distinguish a clean syntax result (0), source diagnostics (1), and
usage/input errors (2). See the implementation guide for coordinates, recovery,
schema versioning and content-bound edits.
