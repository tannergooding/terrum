# Terrum

Terrum (TAIR-um), a programming language from TerraFX.

This repository hosts the language design and the C# bootstrap compiler/tooling.
It is experimental; source syntax and public APIs are not stable.

The running slice is a Unicode-aware lexer, lossless concrete syntax tree,
recovering parser and syntax CLI. It does not execute Terrum or check names,
types, ownership or public contracts.

## Build

Install the exact .NET SDK specified in `global.json`. The initial toolchain uses
.NET 11 RC, already available on the development host; no system SDK installation
is performed by the repository.

```powershell
dotnet build Terrum.slnx
dotnet test Terrum.slnx --no-build
dotnet run --project sources\terrumc -- check samples\syntax\hello.terrum
dotnet run --project sources\terrumc -- tree samples\syntax\control-flow.terrum --json
```

Outputs go to `artifacts`. Builds enable nullable analysis, checked host integer
arithmetic, deterministic compilation and warnings as errors. Compiler arithmetic
is not the implementation of Terrum numeric semantics.

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

For machine consumers use the built `artifacts\bin\terrumc\debug\terrumc.dll`
rather than mixing `dotnet run` build messages with protocol output. CLI exit
codes distinguish a clean syntax result (0), source diagnostics (1), and
usage/input errors (2). See the implementation guide for coordinates, recovery,
schema versioning and content-bound edits.
