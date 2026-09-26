# Build infrastructure

Build infrastructure follows [TerraFX at
9def09f6c24a0465035ce004ab240ecce0a97a04](https://github.com/terrafx/terrafx/tree/9def09f6c24a0465035ce004ab240ecce0a97a04).
Prefer copying upstream changes with minimal project substitutions rather than
maintaining a separate set of build conventions.

The root and category-level `Directory.Build.props`/`targets`, central package
versions, full `.editorconfig`, Git defaults, public signing key and Windows/
PowerShell wrappers derive directly from that revision. The upstream revision
has no shell entry point; PowerShell can also invoke `scripts/build.ps1` directly.
The Unicode data generator is Terrum-specific and is not part of the build driver.

## Commands

```powershell
.\restore.cmd
.\build.cmd
.\build.cmd -fast -test
.\build.cmd -configuration Release -test
.\test.cmd
.\pack.cmd
.\build.cmd -restore -test -configuration Release
.\scripts\build.ps1 -help
```

Each action uses the existing pinned SDK and writes a binary log under
`artifacts\log\<configuration>`. `build` does not restore; `test` and `pack` do
not build or restore. Combine switches when those prerequisites are needed.
Additional arguments pass through to MSBuild. `-solution` selects a solution
or project instead of `Terrum.slnx`.

`-architecture` installs the exact `global.json` SDK into `artifacts\dotnet`
and selects it for that invocation, without changing the machine installation.
`scripts\cibuild.cmd` enables restore, build, test and local packaging using a
local architecture-specific SDK. It does not publish packages or push Git.
Pack failures are checked independently for the default, preview and stable
variants so a later successful command cannot hide an earlier failure.

`-fast` passes `RunAnalyzers=false` only to the build action for a faster
inner loop. It retains C# compilation, type checking and test execution; it
does not turn off Terrum's language verifier. It is incompatible with `-ci`.
The default build still runs the full .NET analyzer/style and trimming policy:
use `.\build.cmd -configuration Release -test` as the full-analysis checkpoint.

## Build-time analysis

On Windows x64 with SDK `11.0.100-rc.1.26425.128`, a no-restore forced
Release rebuild of the three-project solution took 179-199 seconds with
analysis enabled versus 1.1 seconds with `RunAnalyzers=false`. The
no-analyzer wrapper rebuilt in 0.8 seconds and then ran 1,313 tests in
51 seconds. An already-cached restore took 0.5 seconds; these are not
fresh-machine restore measurements. The MSBuild performance summary assigns
178-198 seconds to `CoreCompile`/`Csc` with full analysis, rather than
evaluation, copying, or restore. A diagnostic analyzer report attributed
163 of 187 aggregate core-project analyzer-seconds to CA1508 (dead conditional
code) and 19 seconds to the trimming analyzer. Analyzer seconds overlap
wall time and other analyzer work; they are not additive. CA5403/CA5390
previously dominated a 23-minute rebuild but fell below 0.5 analyzer-seconds
each after byte-emission changes; see [CA5403](../reports/CA5403.md) and
[CA5390](../reports/CA5390.md). Keep CA1508 and trimming checks in the
full-analysis checkpoint rather than removing useful diagnostics.

To capture comparable analyzer evidence after restoring dependencies, run
`dotnet build Terrum.slnx --no-restore -c Release -t:Rebuild -p:ReportAnalyzer=true -v:diag`
and compare against the same command with `-p:RunAnalyzers=false`.
`ReportAnalyzer` prints per-rule execution time; `-clp:PerformanceSummary`
prints MSBuild stage times. Redirect diagnostic output to an ignored
`artifacts\` log, not a tracked file.

## Project-specific differences

| Area | Terrum adjustment |
|---|---|
| Identity | Terrum solution, product, description, root namespaces and category property. No repository/package URL is invented before a public repository is selected. |
| SDK/framework | Retain the exact available .NET 11 RC pin and `net11.0` targets; the local installer reads that pin rather than downloading upstream's floating .NET 10 SDK. |
| Platform | Do not stamp the portable compiler with TerraFX's Windows-only assembly attribute. |
| Dependencies | Centralize the existing xUnit packages and TerraFX's test SDK version; retain nuget.org mapping without unused native interop dependencies or the TerraFX feed. |
| Tests | Preserve public-API tests, without `InternalsVisibleTo`. The test host is not packable, trimmed or AOT-compatible and does not require public XML documentation. |
| CLI | The executable is not a library NuGet package. Its human token rendering uses reflection-free JSON escaping so trimming/AOT diagnostics remain enabled. |

The copied `TerraFX.snk` is a 160-byte **public-only** key matching
`AssemblyOriginatorPublicKey`, not a private signing secret. Public signing
provides strong-name identity, not publisher authentication.

Library builds include XML API documentation and embedded debug information,
generate the module `SkipLocalsInit` attribute, enforce warnings as errors and
enable trimming/AOT analyzers. `SkipLocalsInit` does not permit observing
uninitialized storage; the compiler's scratch buffers write each consumed byte.
Build-time compatibility analysis is not a claim that Terrum source programs
can already be emitted or AOT-compiled.

Generated Unicode tables carry their generated-code marker and data license.
Hand-written source conforms to the shared style; narrow analyzer suppressions
explain intentional parser recovery/internal control flow rather than disabling
the policy repository-wide.
