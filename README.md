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

The development [TRRM scalar path](docs/implementation.md#executable-trrm-scalar-interchange)
emits metadata and compact-linear/indexed Bodies for a bounded
source-free, fail-fast scalar subset. It uses the shared verifier and backend;
general imported-call linking and checked-exception Body support are not
implemented. Its development encoding is not verified conformance to the
current unified registry.

The bounded [Terrum-authored scalar source frontend](docs/implementation/self-host.md)
projects one source snapshot into bounded scalar functions and direct calls,
including forward calls and recursion. Its source-bound multi-function
reader audits target identities and void-call statement roots before using
the hosted verifier/emitter. This is separate
from source-free TRRM support and is not a bootstrap compiler.

The [grammar](docs/specs/grammar-spec.md) and design documents specify target
contracts independently of compiler support. Places default to readonly,
`mut` qualifies writable places/receivers, and `write` grants writable
reference/view access. The implementation retains legacy `bool`/`out` beside
distinct vacancy-only `bool`/`init ref<T>` outputs; `out T` is not canonical
target syntax. `samples\design` and library proposals are
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
New-Item -ItemType Directory -Force artifacts\bin\samples | Out-Null
dotnet run --project sources\terrumc -- build samples\native\factorial.terrum -o artifacts\bin\samples\factorial.exe --runtime fail-fast
.\artifacts\bin\samples\factorial.exe
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
[self-host components](docs/implementation/self-host.md).

## Layout

| Location | Purpose |
|---|---|
| `sources/Terrum.CodeAnalysis` | Source/syntax APIs, binding, verification, native backend and semantic modules |
| `sources/terrumc` | Human- and machine-facing CLI |
| `tests/Terrum.CodeAnalysis.Tests` | Library and process-level CLI tests |
| `samples/syntax` | Parser fixtures; not executable Terrum |
| `samples/native` | Windows x64 executable examples |
| `samples/design`, `library` | Language and API sketches |
| `sources/self-host`, `tests/self-host` | Candidate compiler components and their test fixtures |

Read the [language design](docs/language.md), [language contracts](docs/specs/language-contracts.md),
[lifetime contracts](docs/specs/lifetimes.md), [library design](docs/specs/libraries.md),
[sample catalog](docs/samples.md), and [roadmap](docs/roadmap.md).
The [proposal index](docs/proposals/README.md) distinguishes unapproved
amendments from accepted contracts. The
[pre-v1 acceptance policy](docs/specs/overview/conformance.md) does not freeze
the current design or require compatibility with unpublished drafts.

The C# compiler host does not imply managed output. The native backend emits
Terrum-owned Windows x64 code for AOT and JIT; CIL is neither a dependency nor
the default.
Native execution tests require Windows x64, though emission does not invoke an
installed platform toolchain. [Native boundaries](docs/specs/native.md) retain
ABI and runtime obligations. The [development native profile](docs/implementation/native.md),
[JIT](docs/implementation/jit.md), [module](docs/implementation/modules.md),
[set](docs/implementation/module-sets.md) and [graph](docs/implementation/native-graph.md)
guides document bounded execution and replacement proofs, not a production
runtime or standard private serialization. Only
[`.trrm`](docs/specs/representation/persistence.md) is normative serialized
semantic interchange.
The [compiled scanner](docs/scanner.md) documents its executable boundary.

For machine protocols, use the built `terrumc.dll` rather than mixing
`dotnet run` build output with protocol data. Inspection/build commands return
0 on success, 1 for source/compile diagnostics, and 2 for usage or I/O errors.
Execution workers return the program's status; component workers can terminate
on native failure and must be supervised. `check` is syntax-only; `analyze`
binds. The [development CLI response reference](docs/implementation/cli-protocol.md)
documents these worker coordinates, diagnostic versions and output shapes;
syntax edits are bound to the source content ID. The selected
[CLI supervisor protocol](docs/specs/cli-protocol.md) separately defines
standard CLI outcomes, program return data and output destinations;
experimental worker behavior is not a claim of conformance to it.
