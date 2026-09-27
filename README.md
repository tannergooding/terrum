# Terrum

Terrum (TAIR-um), a programming language from TerraFX.

This repository hosts the language design and the C# bootstrap compiler/tooling.
It is experimental; source syntax and public APIs are not stable.

The compiler combines a Unicode-aware, lossless syntax API with a bounded
value-type profile and direct Windows x64 native emission. It binds declarations,
checked numerics, compact structs, places and conditional outputs, then verifies
structured operations before encoding. The same backend writes PE executables
and supplies a bounded JIT execution worker, without CIL, LLVM or an external
linker. Compact unique values support checked moves, owned calls and deterministic
cleanup. Native buffers have checked lexical views, and a bounded managed-node
runtime traces precise roots and reclaims cycles. Bounded source-defined classes
add complete-publication construction and descriptor-based tracing. General
stored loans and managed layouts, general exception objects and the standard library are not
implemented.

Design documents and `samples\design` describe the target contract separately
from this executable subset: places default to readonly, `mut` applies to
places/receivers, and `write` qualifies reference or view access. The target
initialization-loan form is `init ref<T>`; current executable examples still
use the legacy `bool`/`out` contract until compiler declarations and call sites
are migrated together. Design sketches are not claims that these semantics or
their broader syntax and library surface are implemented; the bounded
class/`ManagedNode` exception is described next.

The syntax-15 numeric subset constructs context-typed decimal and hexadecimal
integers without a fallback type and supports checked `T(expression)`
conversions for `int32`, `uint32`, `int64`, `uint64` and `char16`. Attached
width suffixes diagnose as `TRM0006`; a `0x`/`0X` prefix without an ASCII hex
digit diagnoses as `TRM0007`; prefix casts are no longer parsed. Floating
literal binding, digit separators, custom literal targets and `[ConstEval]`
are not implemented yet; see the [implementation matrix](docs/implementation.md).

Semantic v24 validates managed-class/`ManagedNode` access: `mut class`,
writable `mut` class methods, readonly bare handles and explicit `write`
capability through qualified fields, parameters, results, optionals and
inference. Constructors initialize unpublished objects before publication;
a writable child handle retains its capability through a readonly parent,
without deep-read propagation. Semantic v25 separately records non-exported
`MutReceiver` methods on non-owning compact structs; source visibility may be
public or private, but does not expose a mutating-receiver ABI. Called on a
named mutable local, or forwarded through the existing `this` receiver, a
mutating method borrows and updates the original place, including
writes before a checked exception; readonly methods and by-value arguments
retain copy behavior, and the ordinary nonmutating receiver ABI is unchanged.
The verifier rejects forged mutable receiver loans from
immutable places, while nested field writes through a valid receiver are
allowed. Older module headers reject the v25 receiver contract; programs not
requiring it retain their versions and bytes regardless of managedness. Readers
continue to normalize private managed artifacts from versions 18 through 23
without granting more authority than their original contract. Public scalar
entry wrappers may call non-exported mutating methods; private helpers remain
non-exported as well. The native handle ABI and scalar-only public module
boundary are unchanged.

Unique/owning mutating receivers, temporary or nested-field receiver places
(such as `outer.Inner.Mutate()`),
rebinding `this`, public exported mutating-receiver ABIs and general references
remain unsupported. General reference/view/ownership forms and `init ref<T>`
call-site grammar remain unsupported; the separate legacy buffer-view access
path still needs alignment. These bounded features do not imply general
ownership/lifetime analysis or standard-library execution.

## Build

Use the exact .NET 11 RC SDK specified in `global.json`. The default commands
use an existing installation; `-architecture x64` acquires that version under
`artifacts\dotnet` instead, without changing the system SDK installation.

```powershell
.\restore.cmd
.\build.cmd
.\build.cmd -fast -test # iterative build/test during large changes
.\build.cmd -test # full analyzer checkpoint (Debug)
.\build.cmd -configuration Release -test # full analyzer checkpoint (Release)
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
For large changes, use `-fast -test` for routine compile/test cycles.
This defers .NET analyzer/style and trimming checks and XML documentation
generation, but not C# compiler errors, tests or Terrum's semantic verifier.
Run a build without the switch in the same configuration at a full-analysis checkpoint. CI legs can opt
into `-fast`; the packaging leg should run with full analysis.

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
The [language and library integration review](docs/language-library-review.md)
explores additional contracts and extensibility mechanisms; its proposals are
not accepted language semantics or additions to the current implementation scope.

The C#/.NET compiler host does not imply managed output. The first backend is
Terrum-owned Windows x64 AOT. A CIL backend could be added for a demonstrated
benefit, but it is neither a dependency nor the default. See the
[native profile](docs/native.md) for its explicit fail-fast runtime contract,
ABI, artifact format and unsupported features. Native execution tests require
Windows x64; emission itself does not invoke installed platform toolchains.
The [JIT proof](docs/jit.md) covers executable-memory and unwind lifetimes;
[modules](docs/modules.md) demonstrate process-start compatible producer
replacement against unchanged persisted semantic or native consumers, with
contract checks before execution. [Native module sets](docs/module-sets.md) extend
the persisted native consumer proof to multiple independent leaf DLLs.
The [native graph proof](docs/native-graph.md) additionally supports dependencies
between DLLs and validates the complete selected closure before execution.
Neither is a production
runtime or hot-servicing implementation. The [compiled scanner](docs/scanner.md)
executes bounded Terrum tokenization against shared pinned Unicode data and
decodes integer magnitudes without imposing a default numeric type.
The `build-component` worker uses those native token/trivia and integer records
in the real parser/binder/native-emission pipeline, without re-lexing the target
source or decoding its integer magnitudes again in C#.

For machine consumers use the built `artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll`
rather than mixing `dotnet run` build messages with protocol output. CLI exit
codes for inspection/build commands distinguish success (0), source/compile
diagnostics (1), and usage/input/output errors (2). Execution workers instead
return the program's status; component workers can terminate on a native scanner
failure and must be supervised. `check` remains syntax-only; `analyze` binds.
See the implementation guide for coordinates, recovery,
schema versioning and content-bound edits.
