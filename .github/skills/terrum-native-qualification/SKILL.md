---
name: terrum-native-qualification
description: Qualify Terrum compiler changes with focused tests, native ABI probes, or supervised execution. Use when selecting compiler outputs, investigating native test failures, checking configuration and input identity, or assessing emission versus runtime evidence.
---

# Terrum native qualification

Follow `.github\copilot-instructions.md` and `docs\building.md`. Use standard
developer builds, the existing NUnit runner, and applicable native probes.
This skill does not add a build system or authorize interactive/hardware runs.
Paths below are repository-relative.
Qualification checks a deliberately selected contract and input, not the
immutability of today's draft. Design-only amendments use
`terrum-spec-design-review`; they do not require native execution merely to
propose new schemas or assignments. Never report a static review as runtime
evidence.

## Select the evidence required

Identify the changed contract, final source/patch, target, configuration,
runtime policy, and assertions needed before choosing commands. Select the
smallest relevant batch; widen it only when failures or material risk require.

| Evidence | What it establishes |
|---|---|
| Syntax check | Accepted input under the exercised parser; not binding or execution |
| Semantic analysis/verification | Checked facts under the exercised target, policy, and dependencies |
| SDK/C layout probe | ABI facts for the selected native headers/toolchain; not Terrum support |
| PE/library emission and inspection | Generated image structure; not successful program execution |
| Supervised native execution | Only the behavior and outcomes asserted by that run |

Read `tests\Terrum.CodeAnalysis.Tests\Terrum.CodeAnalysis.Tests.csproj` for
exclusions and the selected tests for conditional gates. Find the actual test
cases before selecting filters. Do not re-enable excluded prototypes or
interactive tests merely to produce a passing count.

## Select and build the actual inputs

- Use the exact `global.json` SDK and a deliberately selected configuration.
  Build through the existing wrappers or standard `dotnet` commands.
  For example, `.\build.cmd -configuration Debug -fast` builds Debug without
  restoring or running tests. Restore only when assets/dependencies require it.
- Run related NUnit selectors together with the matching `-c` configuration
  and `--no-build --no-restore` only after its outputs cover the current patch.
  The normal non-fast build is the analyzer/style checkpoint; do not mistake
  a fast build or a pack action for that checkpoint.
- Resolve the built compiler under `artifacts\bin\sources\terrumc` and the
  selected target framework. Record its path and SHA-256 hash, together with
  the actual companion libraries, native fixtures, and source/dependency inputs
  used by the run. HEAD alone does not identify uncommitted work.
- Prevent concurrent builds or probes from replacing those inputs or writing
  the same outputs. Recheck relevant hashes at completion; changed inputs
  invalidate the run's attribution. A timestamp is not build identity.

## Preflight each native probe

- Inspect the selected script's argument parsing, compiler/library selection,
  working directory, output paths, and assertions before copying its documented
  invocation. Useful entry points include `scripts\check-native-frame.cs`,
  `scripts\check-native-seh.cs`, `scripts\check-owned-buffer-abi.cs`, and
  `scripts\check-managed-node-abi.cs`.
- Use `--compiler` only if the script actually parses and consumes it. An
  ignored argument or `dotnet run -c Release` does not select the compiler
  loaded by the probe; the latter selects the harness configuration.
- If a script hardcodes a compiler path, qualify only when that exact file is
  the deliberately selected prebuilt input. Record the selection limitation.
  If it cannot exercise the intended input, report the blocker instead of
  silently switching configuration or claiming the documented interface works.
  Do not edit the probe unless that correction is within the authorized scope.
- For API-based probes with project references, establish which library their
  normal build/run will consume; do not attribute them to an unrelated CLI DLL.
- Check Windows x64, SDK, native fixture, and Visual Studio toolchain
  prerequisites where required. Keep `vcvars64.bat` setup and the native
  compilation command in the same `cmd.exe` process.
- Serialize probes with fixed output filenames. Do not assume unique run
  directories, a shared evidence helper, or completion markers exist merely
  because documentation describes them.

## Run with supervision and fresh evidence

- Keep normal runs headless. Visible-window and hardware integration require
  their documented explicit opt-ins. A skipped integration is an evidence gap,
  not runtime success; do not weaken the gate or substitute another device.
- Follow process patterns in `tests\Terrum.CodeAnalysis.Tests\Cli\CliTests.cs`:
  capture stdout/stderr, enforce deadlines, inspect the actual exit/result,
  and kill only the owned process/tree on timeout before waiting for exit.
  Do not use broad process-name termination.
- Distinguish compiler diagnostic exits from execution-worker program statuses.
  A nonzero program result can be expected by a test; compare the actual
  result and output to the specific assertions rather than assuming zero.
- Wait for processes to exit before deleting generated executable outputs.
  Reuse `tests\Terrum.CodeAnalysis.Tests\Infrastructure\TestFileCleanup.cs` for bounded
  file/directory cleanup retries; persistent failures must remain visible.
- Retain fresh command arguments, outputs, exit statuses, deadlines, input
  identity, and assertions under `artifacts\log` or the existing probe results.
  NUnit TRX results belong under `artifacts\tst`. Preserve an incomplete run's
  failure evidence instead of allowing a later run to overwrite its diagnosis.
- Inspect counts and expected cases, not just a runner's success exit. Zero
  matched tests, gated skips, timeouts, crashes, and partially written results
  are not a passing selection. An isolated retry does not complete an aborted
  wider suite.
- Cite a completion marker only if this invocation's implementation writes it
  after all required assertions. Otherwise use the fresh runner result and
  actual assertions; never manufacture a marker to stand in for missing proof.

## Close the qualification batch

Map results back to the requested claims and required variants. Reuse evidence
only while its relevant source and compiler inputs remain unchanged. Group
related fixes before rerunning affected checks; do not repeat a whole suite
for unchanged inputs or documentation-only edits.

Report the outcome and material failures, skips, blockers, or uncovered paths
concisely. Link retained logs when needed rather than copying their contents.
Do not declare broader feature or milestone acceptance from a focused pass.
