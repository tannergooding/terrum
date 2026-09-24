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
