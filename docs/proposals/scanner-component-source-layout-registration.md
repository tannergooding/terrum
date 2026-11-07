# Source layout registration for scanner component probes

**Status: unapproved candidate; provisionally actionable only under the user's
authorized missing-contract workflow.** The option is provisionally implemented
for scanner-source compilation in `scan-component`/`build-component`, with
coverage in `CliTests.LayoutCoreManifest`; the focused Debug selection passed
with zero skips. This does not select production Core authority/provider,
configure subject-source compilation, or make a stability promise.

## Missing decision and recommendation

Allow the existing **`--layout-core-manifest <path>` on `scan-component` and
`build-component`, for their scanner source compilation only**. Reuse the exact
version-1 configuration, closed declaration producer and checked
`LayoutCoreRegistrationSet` from
[source layout CLI registration](source-layout-cli-registration.md). No second
manifest grammar, implicit provider or registration channel is needed.

| Inspected evidence | Consequence |
|---|---|
| [`Program.cs`](../../sources/terrumc/Program.cs), `Usage` and dispatch; [`Program.Scanner.cs`](../../sources/terrumc/Program.Scanner.cs), `RunScannerComponent` | Both commands use the same scanner compiler. It currently calls `Compilation.Create(scannerTrees, ScannerHost.CreateBuffers(source), (BoundRuntimePolicy)policy)` without layout registration; neither command accepts the option. Extending only `scan-component` leaves the same gap in `build-component`. |
| [`SourceRange.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange.terrum), [`CharacterLiteral.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterLiteral.terrum), [`StringEscape.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Lexing/StringEscape.terrum) | The directory-form scanner bundle authors Layout/Blittable annotations. Exact registration is required; the source names cannot select those roles. |
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), the multi-tree/host-buffer/options overload; [`LayoutCoreManifestReader.cs`](../../sources/terrumc/LayoutCoreManifestReader.cs) | The required options-bearing factory and shared checked configuration reader already exist. The missing decision is route eligibility, not another host API or declaration codec. |
| [`Program.Scanner.cs`](../../sources/terrumc/Program.Scanner.cs), the later `CompileTrees` call | `build-component` separately compiles the subject parsed from scanner records. That source is not part of the scanner Module. |
| [`JitTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Jit/JitTests.cs), `ScannerWorkerReturnsCopiedWritableTokenDataAsync` and `ScannerWorkerReportsLexicalDiagnosticsAndTokenSpansAsync` | Active cases invoke the real bundle without configuration and expect scanner response version 10. A scanner-compilation diagnostic response does not satisfy those assertions. |

This candidate supersedes **only these two commands' scanner-stage membership**
in the base proposal's
[component exclusion](source-layout-cli-registration.md#exact-command-line-contract),
including that exclusion's summary in the
[private-module extension](private-module-source-layout-registration.md).
The base remains the sole owner of the manifest grammar and producer. Its
existing direct/Standard eligibility and the independent private-module
extension are unchanged. The separate
[frontend-component extension](frontend-component-source-layout-registration.md)
owns the test probe's component-source compilation; frontend
build/library/run, private native producer/consumer/set/graph routes,
syntax-only commands and every source-free validation/build/run/catalog route
remain excluded.

Preserve [Windows x64 layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas),
[Core identity/authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
[attribute grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
[ordinary lookup](../specs/language/names-and-binding.md#lexical-lookup-and-imports)
and [binding/replacement](../specs/representation/binding-and-replacement.md).
This changes no accepted source rule, target layout, metadata/private payload,
codec, profile, feature or revision.

## Command and using-Module contract

The proposed forms are:

```text
scan-component <scanner.terrum|terrum-project-directory> <input|-> --runtime <fail-fast|checked-exceptions> [--layout-core-manifest <path>] [--json]
build-component <scanner.terrum|terrum-project-directory> <input|-> -o <output.exe> --runtime <fail-fast|checked-exceptions> [--arithmetic-profile <development|release>] [--layout-core-manifest <path>] [--json]
```

Inherit the base option's exact argv rules: one occurrence after required
positionals among existing options, one following nonempty operand, and an
optional final `--json`. Reject wrong casing, equals syntax, aliases, duplicates
even with the same path, `-`, dash-leading operands and extra operands. An
explicit relative/absolute path can name a dash-leading file. Resolve the
manifest against the invocation working directory, not the component directory.

The first positional remains a scanner **file or the existing fixed directory
bundle**, never stdin. The second remains the bounded strict-UTF-8 scanner
input, from a file or stdin. Add no `--source`, `--source-name`, `--identity`,
`--corelib`, native-import, source-grant or ABI-output option. Preserve the
existing bundle membership, namespace checks, synthetic scanner entry point,
input limit and diagnostic source labels, including `<stdin>`.

The manifest's `usingModuleIdentity` names **one scanner source Module**:
all `ReadScannerTrees` snapshots and the existing synthetic entry point share
its immutable registration context, while each tree keeps its lexical imports.
Neither the subject input, host buffers, scanner path/namespace, component
response name, output stem nor command role supplies another Module identity.
The using identity cannot equal a selected provider identity. There is no
separate publication identity to compare or persist on this route.

For `build-component`, the later subject-source `CompileTrees` call is a
**separate, unconfigured compilation**. Do not forward the scanner registration
or its using identity into it. Subject-authored Layout/Blittable uses therefore
still need a separately designed selection route; this candidate adds none.
Reusing one manifest for both compilations would conflate using contexts.
A second subject-selection option would be a larger, unnecessary decision for
the reported scanner gap; ordinary configured `build` remains available.

`--runtime` continues to select both scanner execution and, for builds, subject
compilation/emission. Scanner arithmetic remains Development.
`--arithmetic-profile` remains build-only and applies only to the subject.
The manifest grants no native registration, unsafe authority or lowering
agreement. Without it, there is no scanner layout registration: otherwise
supported unannotated scanners remain usable, while affected annotations
diagnose without name-based recognition or a hidden test/provider default.

## Admission, paths and execution ownership

Reuse the base owner's [host authority and producer rules](source-layout-cli-registration.md#host-authority-and-producer-construction)
and [complete version-1 schema](source-layout-cli-registration.md#version-1-json-shape)
unchanged, including full validation when no selected role is used. Passing the
option is an explicit host authorization decision; source, package contents,
a colocated file or a test mode cannot authorize it. Configuration is neither
an artifact provider nor executable adapter/plugin input.

Perform admission in this order:

1. Validate the entire command and option combination before reading input.
   Resolve component, subject-file, manifest and output paths once. For a
   directory component, determine the existing selected scanner-file list
   without reading their contents; do not recursively select additional files.
2. Require a regular manifest file, not stdin, a directory, URL or search root.
   Reject its alias with **any selected scanner file**, the subject input file
   or the build output. For builds, also retain output distinctness from every
   scanner file and the subject file. Use the shared
   [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs) relation:
   full paths, platform casing, resolved links and available file identities,
   including hard links, not argument-string equality. Required identity
   inspection failures are input errors, not proof of distinctness.
   Stdin has no file alias to compare. A manifest may reside inside the bundle
   directory if distinct from its selected files; that location grants nothing.
3. Capture manifest bytes once and completely admit the shared closed producer,
   checked origins/exports, immutable original expectations and layout set
   **before reading scanner source or consuming subject stdin**. Reuse
   `LayoutCoreManifestReader.Read`; do not reopen configuration, add a competing
   resolver or cache registration by path/identity text alone.
4. Read the existing scanner and subject snapshots and enforce their current
   bounds. Call the existing multi-tree/host-buffer/options `Compilation.Create`
   overload with the selected runtime and
   `CompilationOptions.LayoutCoreRegistration`. Supply the set once through
   options, not additionally through a host-configured reference. Preserve the
   shared carrier's duplicate-selection and same-snapshot composition checks;
   this route constructs layout-only configuration.
5. Apply ordinary lookup, checked constants, layout normalization and shared
   verification. Start the existing in-process scanner JIT only after successful
   configuration admission and scanner compilation. Keep scanner result
   validation, integer records and parser handoff unchanged; never retry with
   the host lexer or another provider. For builds, compile the separate subject
   without this registration and use existing staged executable publication.

No source annotation edits, injected imports, Core-name retries or synthesized
provider identities are permitted. Neither configuration nor host authorization
is serialized into token responses, executable data, source/debug metadata or
a synthetic runtime dependency. Already normalized layouts retain their
ordinary downstream verification/lowering obligations.

## Errors and observable results

| Condition | Required projection |
|---|---|
| Ineligible command, invalid option spelling/arity/operand, duplicate or existing command restriction | `TRM2000`, exit 2, before input reading, scanner launch or output acquisition. |
| Manifest I/O/access/path inspection, strict decoding/JSON/version/field/type/range error, invalid origin/declaration/expectation/registration | `TRM2001`, exit 2. Catch shared-reader `FormatException` and checked-carrier failures in configuration admission; do not leak an exception or report scanner-protocol `TRM5001`. |
| Manifest aliases a selected scanner/subject file or output; build output aliases a selected source or is not `.exe` | `TRM2002`, exit 2, before scanner launch or destination acquisition/replacement. |
| Existing nonconfiguration source input/decoding/bounds failure | Preserve `TRM2001`, exit 2. |
| Scanner source lookup, schema, constant, layout or other compilation error | Existing located compilation diagnostics, exit 1, with the original single/bundle source ownership. |
| Unsupported scanner native execution or invalid returned scanner records | Preserve the existing `TRM4001` or `TRM5001`, exit 1, respectively. A native fail-fast termination remains a supervised process outcome, not a successful scanner response. |
| Subject lexical/parser/binding/lowering or output failure in `build-component` | Preserve existing diagnostics, emission refusal and output-error categories/statuses; no fallback emitter or registration. |

Configuration errors name the manifest and, when established, the offending
JSON property/index path. Trailing `--json` uses the existing schema-1
input-error response without a fabricated source span. Scanner compilation
errors keep existing single-source/schema-1 or bundle/schema-2 responses.
For `scan-component`, results including subject lexical diagnostics retain
scanner schema **10** and component **`scanner-10`**; `build-component` retains
its subject compilation/artifact response. Add no response fields or revisions
and make no [`terrum.cli/1`](../specs/cli-protocol.md) conformance claim.

Usage/configuration/path failures launch no scanner. Every prepublication
failure preserves existing executable bytes and all inputs and publishes no
successful artifact; later subject errors may follow an already executed valid
scanner. Preserve truthful staged-publication/cleanup failures, not an atomic
transaction promise.

## Implementation and acceptance gates

After provisional integration, `Program.cs` help and
`Program.Scanner.cs` parsing/admission/factory selection are the route owners;
the shared manifest reader, checked layout carrier and path-identity helper
remain reusable owners, not scanner-specific copies. The experimental CLI and
hosted support references can record implemented eligibility only after
validation. No such code/reference/index change is made by this proposal.

Use the existing public CLI runners with explicit synthetic host-authorized
manifests, unique artifacts, deadlines, owned-process termination/wait and
`TestFileCleanup` retries. Update affected scanner call sites explicitly; do not
make a generic runner silently inject configuration or change no-registration
negatives.

| Required cases | Acceptance |
|---|---|
| Real directory bundle; file and stdin subject input; both runtime policies | Restore the named JIT scanner regressions and CLI token/trivia/integer tests with explicit selection. Assert schema 10, exact UTF-16 spans, Unicode/trivia/diagnostics and integer magnitudes, not merely process success. |
| `build-component` versus hosted `build` | Exercise `NativeScannerFeedsTheOfficialCompilerWithoutChangingEmissionAsync`, `BuildPublishesNativeArtifactAndFailedCompilationPreservesItAsync` and lexical/parser/integer-binding cases in [`CliTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.cs) and its partials. Compare content IDs/artifact bytes where already required; execute a headless output under supervision and check its actual status separately from emission. |
| Single-file scanner; unrelated provider/key/name choices and distinct per-tree imports | Registration uses ordinary lookup, not scanner/sample identity. Cover selected/unimported/shadowed/wrong declarations and single/bundle located diagnostics, including `ScannerBundleCompilationDiagnosticsKeepTheirSourceFileAsync`. |
| Stage isolation and absence | A scanner without layout uses still works unconfigured, including existing [apphost/registrar coverage](../../tests/Terrum.CodeAnalysis.Tests/Native/Jit/JitTests.CliRegistrar.cs). The real annotated bundle fails without selection. A valid scanner manifest does not authorize subject annotations in `build-component`; subject arithmetic/runtime behavior stays unchanged. |
| Configuration and argv failures on both commands | Reuse the base reader's malformed JSON/version/registration negatives, including on scanners with no layout use; valid unused configuration still admits. Invalid cases reject before an unclosed stdin blocks or a trapping scanner runs. Test duplicates, missing/empty/dash operands, casing/equals forms and misplaced `--json`. |
| Path aliases and publication | Compare manifest with single-file scanner, **every selected bundle file**, subject file and output; compare output with all source inputs. Exercise relative/case, links/hard links where supported, missing/inaccessible files and unchanged sentinel outputs/inputs. Include a manifest inside the bundle that aliases no source. |
| Invocation isolation and failure handoff | Changed manifest contents at the same path and distinct using contexts cannot reuse stale handles. Preserve `ScannerFailureNeverFallsBackToTheHostLexerAsync`, invalid record/native failure behavior and all prepublication output-retention cases. |
| Unchanged exclusions | Add explicit rejection coverage for `frontend-component` and frontend build/library/run. Retain syntax-only, private-native and source-free negatives in [`CliTests.LayoutCoreManifest.cs`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.LayoutCoreManifest.cs); accepting these two commands must not widen shared/frontend dispatch. |

The cited JIT/CLI cases are included by the current
[`Terrum.CodeAnalysis.Tests.csproj`](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj);
they are not excluded prototype files. Confirm selected counts and final
compiler/configuration inputs when implementing; no tests were run for this
documentation-only candidate. Keep ordinary runs headless without window or
hardware opt-ins.

No additional route-contract blocker remains under this recommendation.
Actual authorized authority, provider revisions/schema selections, keys,
source-visible names and scanner using identity remain explicit deployment or
test-host inputs, not production constants for the implementer to invent.
This closes neither independent subject-registration design nor broader
scanner/compiler conformance gates.
