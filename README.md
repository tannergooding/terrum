# Terrum

Terrum (TAIR-um) is an experimental programming language from TerraFX. This
repository contains its language design and C# compiler host; source syntax and
public APIs are not stable.

The compiler exposes a Unicode-aware, lossless syntax API and a bounded
Windows x64 profile with direct PE emission and a JIT worker. The implemented
subset includes checked scalar operations, compact values and unique owners,
conditional outputs, bounded generics, managed roots/classes, and deterministic
cleanup. General stored loans, managed layouts, general exception objects and
the standard library are not implemented. See the
[implementation reference](docs/implementation.md) for supported features and
limits.

The additive [TRRM scalar path](docs/implementation.md#executable-trrm-scalar-interchange)
emits approved metadata and compact-linear/indexed Bodies for a bounded
source-free, fail-fast scalar subset. It uses the shared verifier and backend;
general imported-call linking and checked-exception Body support are not
implemented.

The [grammar](docs/grammar-spec.md) and design documents specify target
contracts independently of compiler support. Places default to readonly,
`mut` qualifies writable places/receivers, and `write` grants writable
reference/view access. Legacy `bool`/`out` and vacancy-only `bool`/`init ref<T>`
outputs have distinct caller rules. `samples\design` and library proposals are
informative, not implemented APIs.

## Build

Use the .NET 11 RC SDK pinned in `global.json`. The default commands use an
existing installation; `-architecture x64` installs that SDK under
`artifacts\dotnet` for the invocation without changing the system SDK.

```powershell
.\restore.cmd
.\build.cmd
.\build.cmd -fast -test
.\build.cmd -test
.\build.cmd -configuration Release -test
.\test.cmd
.\pack.cmd
dotnet run --project sources\terrumc -- check samples\syntax\hello.terrum
dotnet run --project sources\terrumc -- analyze samples\native\factorial.terrum
dotnet run --project sources\terrumc -- build samples\native\factorial.terrum -o artifacts\factorial.exe --runtime fail-fast
.\artifacts\factorial.exe
$LASTEXITCODE # 720
```

`-fast` supports iterative compile/test cycles: it skips .NET analyzers/style,
trimming checks and XML documentation generation, but retains compilation,
requested tests and Terrum verification. Run without `-fast` in the same
configuration at a full-analysis checkpoint. `pack` packages an existing build
and does not publish it.
Direct `dotnet build Terrum.slnx` and `dotnet test Terrum.slnx` are supported.
See [build infrastructure](docs/building.md) for wrapper behavior and
Terrum-specific build differences.

Build outputs are under `artifacts\bin`; intermediates, logs, test results and
packages are under `artifacts\obj`, `artifacts\log`, `artifacts\tst` and
`artifacts\pkg`. On Windows, the solution also builds a native
compiler-primitives DLL from maintained Terrum sources. That library is not a
self-host compiler or executable scanner/parser; see
[self-host components](self-host/README.md).

## Layout

| Location | Purpose |
|---|---|
| `sources/Terrum.CodeAnalysis` | Source/syntax APIs, binding, verification, native backend and semantic modules |
| `sources/terrumc` | Human- and machine-facing CLI |
| `tests/Terrum.CodeAnalysis.Tests` | Library and process-level CLI tests |
| `samples/syntax` | Parser fixtures; not executable Terrum |
| `samples/native` | Windows x64 executable examples |
| `samples/design`, `library` | Language and API sketches |
| `self-host` | Candidate compiler components and their execution gates |

Read the [language design](docs/language.md), [language contracts](docs/language-contracts.md),
[lifetime contracts](docs/lifetimes.md), [library design](docs/libraries.md),
[sample catalog](docs/samples.md), and [roadmap](docs/roadmap.md).
The [proposal index](docs/proposals/README.md) distinguishes unapproved
alternatives from accepted contracts.

The C# compiler host does not imply managed output. The native backend emits
Terrum-owned Windows x64 code for AOT and JIT; CIL is neither a dependency nor
the default.
Native execution tests require Windows x64, though emission does not invoke an
installed platform toolchain. The [native profile](docs/native.md) defines the
runtime and ABI; [JIT](docs/jit.md), [modules](docs/modules.md),
[module sets](docs/module-sets.md) and [native graphs](docs/native-graph.md)
document bounded execution and replacement proofs, not a production runtime.
The [compiled scanner](docs/scanner.md) documents its executable boundary.

For machine protocols, use the built `terrumc.dll` rather than mixing
`dotnet run` build output with protocol data. Inspection/build commands return
0 on success, 1 for source/compile diagnostics, and 2 for usage or I/O errors.
Execution workers return the program's status; component workers can terminate
on native failure and must be supervised. `check` is syntax-only; `analyze`
binds. The implementation reference documents coordinates, recovery,
schema-versioning and content-bound edits.
