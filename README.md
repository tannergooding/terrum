# Terrum

Terrum (TAIR-um), a programming language from TerraFX.

This repository hosts the language design and the C# bootstrap compiler/tooling.
It is experimental; source syntax and public APIs are not stable.

The compiler combines a Unicode-aware, lossless syntax API with a bounded
value-type profile and direct Windows x64 native emission. It binds declarations,
checked numerics, compact structs, places and conditional outputs, then verifies
structured operations before encoding. The same backend writes PE executables
and supplies a bounded JIT execution worker, without CIL, LLVM or an external
linker. Ownership, managed graphs, general exceptions and the standard library
are not implemented.

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
dotnet run --project sources\terrumc -- analyze samples\native\factorial.terrum
dotnet run --project sources\terrumc -- build samples\native\factorial.terrum -o artifacts\factorial.exe --runtime fail-fast
.\artifacts\factorial.exe
$LASTEXITCODE # 720
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
| `sources/Terrum.CodeAnalysis` | Reusable source/syntax, binding, verified operations, native backend and versioned semantic modules |
| `sources/terrumc` | Human/JSON command-line consumer |
| `tests/Terrum.CodeAnalysis.Tests` | Executable host and process-level CLI tests |
| `samples/syntax` | Syntax fixtures accepted by the current parser; not executable Terrum |
| `samples/native` | Executable Windows x64 numeric, control-flow and compact-value examples |
| `samples/design`, `library` | Broader language/API sketches, not implemented libraries |
| `self-host` | Candidate Terrum compiler components with explicit execution gates |

Read the [language design](docs/language.md), [lifetime contracts](docs/lifetimes.md),
[standard-library design](docs/libraries.md), [sample catalog](docs/samples.md),
[syntax API and support matrix](docs/implementation.md), and [roadmap](docs/roadmap.md).
The repository is the canonical design/development location.

The C#/.NET compiler host does not imply managed output. The first backend is
Terrum-owned Windows x64 AOT. A CIL backend could be added for a demonstrated
benefit, but it is neither a dependency nor the default. See the
[native profile](docs/native.md) for its explicit fail-fast runtime contract,
ABI, artifact format and unsupported features. Native execution tests require
Windows x64; emission itself does not invoke installed platform toolchains.
The [JIT proof](docs/jit.md) covers executable-memory and unwind lifetimes;
[modules](docs/modules.md) demonstrate process-start compatible producer
replacement against an unchanged persisted consumer. Neither is a production
runtime or hot-servicing implementation. The [compiled scanner](docs/scanner.md)
executes bounded Terrum tokenization against shared pinned Unicode data.
The `build-component` worker can use those native token/trivia records as the
lexical phase of the real parser/binder/native-emission pipeline, without
re-lexing the target source in C#.

For machine consumers use the built `artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll`
rather than mixing `dotnet run` build messages with protocol output. CLI exit
codes for inspection/build commands distinguish success (0), source/compile
diagnostics (1), and usage/input/output errors (2). Execution workers instead
return the program's status; component workers can terminate on a native scanner
failure and must be supervised. `check` remains syntax-only; `analyze` binds.
See the implementation guide for coordinates, recovery,
schema versioning and content-bound edits.
