# Terrum repository instructions

## Build and test

- Use the exact SDK selected by `global.json`; package versions are centralized in `Directory.Packages.props`. The Windows entry points invoke `scripts\build.ps1`. See `docs\building.md` for wrapper and native qualification details.
- Wrapper actions are independent: build does not restore, and test/pack do not build or restore. Restore when dependency changes or missing assets require it. `-architecture x64` provisions the pinned SDK under `artifacts\dotnet` for that invocation without changing the system SDK.

```powershell
.\restore.cmd
.\build.cmd -fast -test
.\build.cmd -test
.\build.cmd -configuration Release -test
.\test.cmd
.\pack.cmd
```

- `-fast` skips .NET analyzers/style, trimming analysis, and XML documentation generation during the build; it retains compilation, requested tests, and Terrum verification. Use it during implementation, then run without `-fast` in the same configuration at a full-analysis checkpoint. The normal build is also the analyzer/style check.
- Tests use NUnit with `Microsoft.NET.Test.Sdk` and `NUnit3TestAdapter`. After building the relevant configuration, run a single test through the existing runner:

```powershell
dotnet test tests\Terrum.CodeAnalysis.Tests\Terrum.CodeAnalysis.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName=Terrum.CodeAnalysis.Tests.TestFileCleanupTests.DeleteWithRetryWaitsForFileLockToClearAsync"
```

- Combine related filters in one run. Use `--no-build` only when the selected configuration's outputs cover the current changes. Test regressions through public compiler APIs rather than adding `InternalsVisibleTo`.
- Outputs are under `artifacts\bin`, intermediates under `artifacts\obj`, build logs under `artifacts\log`, test results under `artifacts\tst`, and packages under `artifacts\pkg`. Native qualification probes consume an explicitly selected compiler built with normal developer commands; they must not silently build or select another configuration.
- Follow `.editorconfig`: it requires `var` even when the type is not apparent, overriding personal C# defaults. Warnings and configured style diagnostics are errors.

## Compiler architecture

- `Terrum.CodeAnalysis` is a C# host with lossless text, lexing, and syntax APIs; `Semantics` binds source snapshots into checked bound programs; `Native` emits Terrum-owned Windows x64 code and PE images. The C# implementation does not imply CIL or managed output.
- `Modules` contains dependency binding, private semantic/native artifact paths, and standard TRRM readers, writers, and executable profiles. Only `.trrm` is normative serialized semantic interchange. Do not infer standard TRRM support from a private module, `.trmn`, or `.trng` path.
- Preserve mandatory semantic verification for source, reconstructed metadata, JIT, and AOT inputs. Serialized or producer-generated inputs are not trusted substitutes for consumer-side validation.
- `self-host` contains bounded Terrum-authored compiler components. They still depend on hosted verification/emission and do not establish a bootstrapped compiler. `samples\syntax` are parser fixtures; `samples\native` are executable examples; `samples\design` and library sketches are not blanket support claims.
- `terrumc check` is syntax-only; `analyze` performs semantic analysis. For machine protocols, invoke the built `terrumc.dll` rather than mixing `dotnet run` build output into protocol data. Inspection/build commands use diagnostic exit statuses; execution workers return the program's status and require supervision.

## Contracts and source examples

- Use `docs\specs` for accepted grammar and contracts, `docs\implementation.md` and its linked inventories for documented support, and code/tests to establish actual behavior. `docs\roadmap.md` records pending sequencing; `docs\proposals` records unapproved choices.
- Keep approval, implementation, and stability separate. Existing prototypes, passing samples, SDK probes, and accepted directions do not approve adjacent syntax, metadata schemas, APIs, or ABI choices. Apply explicit user decisions without reopening settled questions or silently deciding unresolved ones.
- Read the relevant grammar and nearby source/tests before writing Terrum examples. Do not import C# syntax or API conventions by analogy. Identify whether an example uses accepted target syntax, currently implemented syntax, or proposal-only forms; use the corresponding syntax, semantic, emission, or runtime checks for the claim being made.
- Keep normative contracts, support inventories, and proposals consistent with their distinct roles. Record current support and remaining work rather than duplicating commit history in maintained prose.

## Native boundaries

- Raw imports and native-callable declarations are caller-unsafe, including scalar-only signatures. Preserve the unsafe obligation through declarations, metadata, binding, verification, and calls. An `unsafe` declaration propagates caller-unsafety; its body still needs explicit unsafe blocks or expressions for unsafe operations.
- Use the selected target's directly interoperable `Blittable` representation for boundary data. There is no implicit marshalling, pinning, rooting, string/array conversion, or lifetime extension. Unsafe authorization does not erase known backing, provenance, access, alignment, or cleanup obligations.
- Represent native pointer parameters according to the current pointer grammar, not checked reference destinations. Distinguish pointer-cell initialization from pointee initialization, and checked references from address-stability dependencies. Do not silently adopt unresolved qualifier forms or treat `ptr<T>` and `pin ref<T>` as aliases.
- Safe wrappers are authored Terrum code. They must establish stable backing, result-correlated output validity, retention/lifetime promises, ownership transfer, and cleanup on every outcome. A caller-selected IID plus `void**` does not establish a fixed COM interface; typed acquisition must fix or validate the IID. Foreign annotations are evidence about a provider contract, not an automatic ownership or marshalling system.

## Compiler generalization

- Shared compiler behavior must operate on general COM, function-pointer, handle, layout, ownership, initialization, and effect contracts, not DX11 APIs, `Vertex3`, exact sample names, or application control-flow patterns. API-specific declarations, IIDs, slots, and layouts belong at authored library/dependency boundaries through the applicable reviewed representation.
- A catalog-backed or source-authored sample path is not proof that all compiler-owned sample policy is gone. Track remaining admission, verification, adaptation, ownership, and emission special cases until they are removed or a genuine contract decision is surfaced.
- Carry generic facts through binding, shared bound-tree traversal/rewriting, mandatory verification, persistence/dependency binding, and lowering. Preserve loan and owner provenance through adapters. Exercise replacements with unrelated synthetic APIs and negative cases, not only the motivating DX11 sample.

## Native test execution and evidence

- Keep ordinary tests headless. Visible-window and hardware integrations require their documented explicit opt-ins; do not enable them as an incidental background check or weaken their gates to obtain a pass.
- Follow the existing process-supervision patterns in `CliTests`: use deadlines, capture the actual result, terminate only the process/tree owned by the test when necessary, and wait for exit before deleting executable outputs. Reuse `TestFileCleanup` for bounded Windows file/directory deletion retries; surface failures after those retries rather than swallowing them.
- Check `Terrum.CodeAnalysis.Tests.csproj` exclusions and conditional gates before citing coverage. Excluded prototypes are not active regressions. Confirm the intended selectors matched and distinguish passed, failed, skipped, incomplete, and crashed runs.
- SDK layout probes establish native ABI facts; successfully emitting an image establishes emission support, not execution. Parsing/binding does not establish emission, and runtime probes establish only the behavior actually exercised. None substitutes for the others. Keep final-patch/configuration and compiler-input identity with validation evidence; do not reuse stale results as proof of changed inputs.

## Roadmap acceptance

- Distinguish completion of an implementation slice from acceptance of the active milestone. Track every remaining relevant compiler special case, persistence/dependency gap, validation requirement, and unresolved contract decision before declaring the milestone complete.
- At coherent integration boundaries, reconcile acceptance criteria, todo dependencies/statuses, evidence for the final relevant patch, and local commit state when commits are authorized. An audit disposition or passing focused filter does not establish broader feature completion.
- During an authorized roadmap continuation, proceed to ready work after completing a slice; do not stop solely because that slice reached a checkpoint. Advance beyond the active item only when its acceptance gate is met, and do not expand a completed decision review into unrelated API design.
