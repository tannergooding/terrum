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
.\build.cmd -test
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
`scripts\cibuild.cmd` enables restore, build, test and local packaging by
default, using a local architecture-specific SDK. It does not publish
packages or push Git.
Pack failures are checked independently for the default, preview and stable
variants so a later successful command cannot hide an earlier failure.

Use `-fast` for iterative builds. It passes `RunAnalyzers=false` and
`GenerateDocumentationFile=false` only to the build action, deferring .NET
analyzer/style, trimming and XML documentation checks while retaining C#
compilation, type checking, requested tests and Terrum's semantic verifier.
It does not select actions. At a full-analysis checkpoint, build and test
without `-fast` in the same configuration: `.\build.cmd -test` for Debug or
`.\build.cmd -configuration Release -test` for Release.

The default `cibuild.cmd` restores, builds, tests and packages. Select a
restore/build/test leg without packaging using
`.\scripts\cibuild.cmd -restore -build -test -fast`; run the full-analysis
packaging leg without `-fast`. Packaging consumes an existing build and does
not run analysis by itself.

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
Hand-written source follows the shared style. The inherited policy includes
global `.editorconfig` exceptions and source-category suppressions in
`sources\Directory.Build.props`; it is not exclusively member-scoped.
The category suppressions retain the upstream API/style baseline:

| Rules | Suppressed checks |
|---|---|
| CA1063, CA1711, CA1724, CA1815 | Disposal API shape, identifier suffixes, type/namespace naming and value-type equality |
| CA2000, CA2213 | Analyzer-inferred ownership and disposal of created objects or disposable fields |
| IDE0028, IDE0221 | Collection initialization and explicit-cast style |

These exceptions do not establish correct resource lifetimes or equality.
Those obligations still require code review and relevant tests; retaining
upstream policy is not a per-site correctness justification. Additional
member-scoped suppressions explain intentional parser recovery and internal
control flow. The Windows-only `scripts\check-*.cs` entry points suppress
CA2266 because they use `dotnet run --file`, not Unix shebang execution.
`.editorconfig` explicitly requires `var` even when a type is
not apparent; that repository rule takes precedence over personal defaults.

## Native qualification

Build the intended compiler with the standard developer command, then pass
that output explicitly to the frame, owned-call, owned-buffer and managed-node
probes. These probes do not build the compiler or silently select Debug.

```powershell
dotnet build sources\terrumc\terrumc.csproj -c Release
$compiler = (Resolve-Path artifacts\bin\sources\terrumc\Release\net11.0\terrumc.dll).Path
dotnet run --file scripts\check-native-frame.cs -- --compiler $compiler
dotnet run --file scripts\check-owned-call-abi.cs -- --compiler $compiler
dotnet run --file scripts\check-owned-buffer-abi.cs -- --compiler $compiler
dotnet run --file scripts\check-managed-node-abi.cs -- --compiler $compiler
```

Use the Debug output path to qualify Debug. `dotnet run -c Release` selects
the file-based harness configuration, not the compiler consumed by these four
probes. API-based probes with `#:project` references instead use the library
built by their standard `dotnet run` configuration.
The independent C callers require Visual Studio x64 C++ build tools; the
[aggregate ABI recipe](implementation/native.md#semantic-and-target-boundaries)
also builds its assembly/C fixture explicitly.

Every probe prints a unique directory under
`artifacts\log\native-probes\<probe>\<run>`. It retains command arguments,
stdout, stderr, deadlines and actual/expected exit statuses on success,
nonzero failure and timeout. Compiler/library input paths and SHA-256 hashes
identify the exercised build; changed inputs reject completion.
`completed.txt` is written only after every command and probe assertion
succeeds. A missing marker denotes an incomplete or failed run; an earlier
run's logs are not evidence for a later invocation. Probe-generated native
outputs share that run directory so concurrent invocations do not overwrite
them; independently prebuilt C caller fixtures remain separate inputs.
