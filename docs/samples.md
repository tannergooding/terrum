# Sample catalog

Terrum samples are grouped by scenario or language feature, not by output
backend or implementation status. A successful parse does not establish
binding, emission, execution, or approval of a provisional contract. See the
[sample entry point](../samples/README.md) for the directory map.

The catalog records current implementation behavior, which can lag the
accepted specification. In particular, runnable entries using `[NoThrow]` or
the `checked-exceptions` runtime-policy switch demonstrate legacy prototype
behavior, not accepted source or module contracts; see
[CG-003](implementation/conformance-gaps.md#cg-003).

## Sample groups

| Location | Purpose and evidence boundary |
|---|---|
| [`LanguageFeatures`](../samples/LanguageFeatures/README.md) | Focused Terrum examples grouped by language topic. Some exercise implemented behavior; others are target-language designs. |
| [`Calculator`](../samples/Calculator/README.md), [`HelloWorld`](../samples/HelloWorld/README.md), [`Mandelbrot`](../samples/Mandelbrot/README.md) | Target-language scenarios whose intended behavior requires support beyond the current implementation. |
| [`CrossModuleRecovery`](../samples/CrossModuleRecovery/README.md) | A provider and consumer demonstrating checked recovery across a module boundary. |
| [`ModuleReplacement`](../samples/ModuleReplacement/README.md) | Provider, consumer, replacement, and incompatible-module examples. |
| [`ModuleSetServicing`](../samples/ModuleSetServicing/README.md) | Two providers and replacements servicing a shared consumer. |
| [`DependencyGraphServicing`](../samples/DependencyGraphServicing/README.md) | Leaf/middle/root dependency graphs, ownership, and scoped-view examples. |
| [`Win32MessageBox`](../samples/Win32MessageBox/README.md) | A Terrum-authored raw Win32 import example; emission is testable, execution is interactive. |
| [`Win32Dx11Triangle`](../samples/Win32Dx11Triangle/README.md) | Terrum-authored WARP and hardware window/rendering scenarios with explicit runtime gates. |

Parser-only inputs belong to
[`tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures`](../tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures),
not to the user-facing sample tree. Test-only ABI and module fixtures remain
with their C# test areas. [`library`](../library) contains non-executable API
sketches, not reference assemblies. The bounded Terrum-authored compiler
components have their own [source project](../sources/Terrum.CodeAnalysis.Terrum/README.md)
and are not language samples.

## Checking and building

Build the compiler through the [normal developer workflow](building.md).
From the repository root after a Debug build:

```powershell
$terrumc = Resolve-Path artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll
dotnet $terrumc check tests\Terrum.CodeAnalysis.Tests\Syntax\Fixtures\hello.terrum
dotnet $terrumc analyze samples\LanguageFeatures\ControlFlow\factorial.terrum
New-Item -ItemType Directory -Force artifacts\bin\samples | Out-Null
dotnet $terrumc build samples\LanguageFeatures\ControlFlow\factorial.terrum -o artifacts\bin\samples\factorial.exe --runtime fail-fast
.\artifacts\bin\samples\factorial.exe
$LASTEXITCODE # 720
```

`check` is syntax-only; `analyze` binds. `build` emits an ordinary native
program; source-native imports use `build-native-import`. Execution is a
separate action and returns the program's status, which need not be zero.
Use the built DLL for JSON protocols so build output cannot contaminate them.

The [native profile](implementation/native.md) owns support details. Runnable
examples use the implemented legacy `bool`/`out` profile where required; it is
distinct from canonical vacancy-only `init ref<T>` contracts. Do not
mechanically rewrite runnable sources into unsupported target forms.
The implemented numeric subset also accepts contextual decimal, hexadecimal
and binary integer literals with digit separators; based literals use the
expected target width as a bit pattern. Buffer-view examples exercise bounded
legacy access profiles, not general `Span<T>` or reference/view support.

## Implemented language-feature examples

These examples exercise selected implemented compiler behavior; the catalog
does not claim that every neighboring language or library facility is
implemented.

| Sources | Purpose and expected observation | Runtime policy / evidence |
|---|---|---|
| [`factorial`](../samples/LanguageFeatures/ControlFlow/factorial.terrum), [`control-flow`](../samples/LanguageFeatures/ControlFlow/control-flow.terrum), [`stack-arguments`](../samples/LanguageFeatures/Functions/stack-arguments.terrum), [`for-loops`](../samples/LanguageFeatures/ControlFlow/for-loops.terrum), [`requirements`](../samples/LanguageFeatures/Contracts/requirements.terrum) | Recursion, mutation, stack arguments, loops, checked requirements; respective statuses 720, 45, 91, 285, 10 | `fail-fast`; actual-source [native tests](../tests/Terrum.CodeAnalysis.Tests/Native/NativeTests.cs) |
| [`integers64`](../samples/LanguageFeatures/NumericTypes/integers64.terrum) | Signed/unsigned 64-bit arithmetic and conversions | Ordinary native build; [64-bit tests](../tests/Terrum.CodeAnalysis.Tests/Semantics/Integer64Tests.cs) |
| [`no-throw`](../samples/LanguageFeatures/Contracts/no-throw.terrum), [`no-return`](../samples/LanguageFeatures/Contracts/no-return.terrum) | Effect contracts and conditional outputs; status 42 | `fail-fast`; actual-source native tests |
| [`try-finally`](../samples/LanguageFeatures/EffectsAndCleanup/try-finally.terrum) | Nested normal-exit cleanup preserves a pending return; status 42 | `fail-fast`; failure terminates rather than unwinding |
| [`checked-recovery`](../samples/LanguageFeatures/EffectsAndCleanup/checked-recovery.terrum) | Cross-function division failure runs `finally` before an unsafe `catch`; status 42 | `checked-exceptions`; AOT/JIT |
| [`owner-transfer`](../samples/LanguageFeatures/Ownership/owner-transfer.terrum), [`unique-owner`](../samples/LanguageFeatures/Ownership/unique-owner.terrum), [`composite-owner`](../samples/LanguageFeatures/Ownership/composite-owner.terrum) | Moves, conditional ownership publication, receiver mutation, failed replacement, and partial construction; status 42 | Use `checked-exceptions` for recovery examples; compact owners hold no OS resource |
| [`owned-buffer`](../samples/LanguageFeatures/Ownership/owned-buffer.terrum), [`readonly-buffer-view`](../samples/LanguageFeatures/Views/readonly-buffer-view.terrum), [`mutable-buffer-view`](../samples/LanguageFeatures/Views/mutable-buffer-view.terrum) | Fixed-capacity owned buffers, initialized prefixes, and scoped loans; status 42 | Both policies; bounded buffer/view profiles, not general `Span<T>` |
| [`nested-struct-receiver`](../samples/LanguageFeatures/Views/nested-struct-receiver.terrum) | Writable nested receiver paths update the original subobject; status 42 | Both policies |
| [`managed-node-cycle`](../samples/LanguageFeatures/ManagedStorage/managed-node-cycle.terrum), [`managed-classes`](../samples/LanguageFeatures/ManagedStorage/managed-classes.terrum), [`managed-optional-cycle`](../samples/LanguageFeatures/ManagedStorage/managed-optional-cycle.terrum) | Rooted nodes/classes, optional edges, partial construction, and cycle collection; status 42 | Both policies; no public managed-handle ABI or general collection library |
| [`multi-type-generics`](../samples/LanguageFeatures/Generics/multi-type-generics.terrum), [`managed-generic-selection`](../samples/LanguageFeatures/Generics/managed-generic-selection.terrum) | Closed generic inference, helper composition, and distinct conditional outputs; status 42 | Bounded compilation-local specialization, not public generic witnesses |
| [`static-protocol-quadratic`](../samples/LanguageFeatures/StaticProtocols/static-protocol-quadratic.terrum), [`static-protocol-power`](../samples/LanguageFeatures/StaticProtocols/static-protocol-power.terrum), [`static-protocol-binary`](../samples/LanguageFeatures/StaticProtocols/static-protocol-binary.terrum), [`static-protocol-comparison`](../samples/LanguageFeatures/StaticProtocols/static-protocol-comparison.terrum) | Source-defined nominal arithmetic/comparison protocols; quadratic returns 46, the others 42 | AOT/JIT; see [generic support](implementation.md#bounded-generic-functions) |

## Multi-file and platform scenarios

Provider and component files without `Main` are deliberate members of a
composition, not standalone programs. Their scenario READMEs and the linked
implementation guides identify required inputs and recipes.

| Scenario | Composition and observation |
|---|---|
| [`CrossModuleRecovery`](../samples/CrossModuleRecovery/README.md) | Separate provider `Divide` and recovering consumer; status 42 under `checked-exceptions`; see [semantic modules](implementation/modules.md). |
| [`ModuleReplacement`](../samples/ModuleReplacement/README.md) | Reference/replacement/consumer examples, incompatible replacements, component entries, and a private managed-Copy consumer; see [semantic modules](implementation/modules.md). |
| [`ModuleSetServicing`](../samples/ModuleSetServicing/README.md) | Two providers, compatible replacements, and a shared consumer; see [module sets](implementation/module-sets.md). |
| [`DependencyGraphServicing`](../samples/DependencyGraphServicing/README.md) | Leaf/middle/root servicing, owning exports, scoped views, and private managed implementations behind handle-free public values; see [native graphs](implementation/native-graph.md). |
| [`Win32MessageBox`](../samples/Win32MessageBox/README.md) | `build-native-import` emits a program using a raw Win32 pointer import; successful dialog dismissal returns success. Execution is interactive. |
| [`Win32Dx11Triangle`](../samples/Win32Dx11Triangle/README.md) | WARP or direct-hardware entry plus both `win32-api.terrum` and `dx11-api.terrum`; copy `triangle.hlsl` beside the executable. |

The Win32/DX11 entries author window creation, shader loading, rendering,
resizing, message processing, and cleanup in Terrum. The shader uses
`SV_VertexID`, so the render path needs no input layout or vertex buffer.
WARP and hardware are separate choices, with no automatic driver fallback.
Launch scripts use `checked-exceptions`; ordinary CLI tests emit both entries
under both runtime policies without running them.

The expected image is a cyan triangle on dark blue. Normal close returns 0;
setup/render/message failures return 1, and window/class cleanup failure
terminates with 87. The sample's
[NOTICE](../samples/Win32Dx11Triangle/NOTICE.txt) preserves the Microsoft
Tutorial02 attribution.

Execution requires Windows x64 and the selected graphics runtime.
[`CliTests.Dx11Sample`](../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.Dx11Sample.cs)
uses explicit `TERRUM_RUN_INTERACTIVE_WINDOW_TESTS=1` or
`TERRUM_RUN_DX11_HARDWARE=1` gates for visible-window resize, displayed-color,
and normal-close checks. These observations depend on the display/desktop
environment. They do **not** establish mapped back-buffer pixels, exact raster
coverage, GPU completion, scanout, or every native release-order effect.
The separate [provider-failure fixture](../tests/Terrum.CodeAnalysis.Tests/Native/Interop/Fixtures/dx11-provider-failure-outputs.terrum)
emits in ordinary tests; its explicit WARP execution observes selected raw
failure outputs using the maintained API companions and one hidden window.
Skipped or unselected integrations are not runtime evidence.

The native FFI/vtable source forms and metadata remain **UNAPPROVED**.
Sample execution does not approve them or give the compiler Windows/DX11
ownership, API, IID, slot, or application policy.
The triangle's [NOTICE](../samples/Win32Dx11Triangle/NOTICE.txt) preserves the
Microsoft Tutorial02 attribution and license. Its README describes platform
contracts and deliberate exclusions rather than a runnable recipe.

## Target-language design examples

These examples require unimplemented language or library facilities. Their
outputs are intended behavior, not observed compiler results. See
[language contracts](specs/language-contracts.md),
[library contracts](specs/libraries.md), and the
[implementation inventory](implementation.md).
Some sketches combine accepted target forms with separately identified
provisional API or representation choices; an example neither approves those
choices nor establishes compiler support.

| Example | Target contract / intended behavior |
|---|---|
| [Hello world](../samples/HelloWorld/hello-world.terrum) | `void Main`, text output, exit 0 |
| [Async hello](../samples/HelloWorld/hello-async.terrum) | `MainAsync`, rooted arguments through completion and cleanup |
| [Mandelbrot](../samples/Mandelbrot/mandelbrot.terrum) | Strict float32, explicit conversions, PBM; dimensions checked, points bounded after 64 updates rather than proven set membership |
| [Calculator](../samples/Calculator/calculator.terrum) | Checked int32 and conditional `init ref<int32>` publication; reject Unicode digit/operator lookalikes rather than normalizing them |
| [Collections](../samples/LanguageFeatures/Collections/collections.terrum) | Builders, guarded writable spans, node identity, and present-null versus missing |
| [Random composition](../samples/LanguageFeatures/Generics/random-composition.terrum) | Generic constructor inference and concrete interface dispatch without payload copying; those facilities remain unsupported |
| [Values/ownership](../samples/LanguageFeatures/Ownership/types-and-ownership.terrum) | Copy values, shared readonly/write handles, moved unique classes, optionals, and an explicitly unresolved inline-payload ownership sketch |
| [Text/spans](../samples/LanguageFeatures/TextAndSpans/text-and-spans.terrum) | Encoding-erased identity, checked typed projection, explicit span projection, and ASCII-only numeric tokens |
| [Formatting/construction](../samples/LanguageFeatures/ConstructionAndFormatting/construction-and-formatting.terrum) | Descriptor forwarding, borrowed interpolation, owning text, and builders |
| [Declaration requirements](../samples/LanguageFeatures/Contracts/declaration-requirements.terrum) | Declaration contracts, private proofs, and lifted initialization outputs; distinct from the runnable `requirements.terrum` |
| [Numerics/tensors](../samples/LanguageFeatures/Numerics/numerics-and-tensors.terrum) | Generic math/SIMD, distinct lane masks, multidimensional views, and explicit writable destinations; no execution/performance claim |

Existing reference variables denote descriptors: form one with `ref place`,
pass an existing descriptor directly, and use `->` or explicit dereference
for referent access. Initialization destinations write through `*destination`
and forward with `init *destination`. `mut` permits rebinding; `write` grants
referent access. `init ref<T>` and `init value` are approved target forms for
vacancy-only destinations. Inline destination declarations follow the accepted
source argument rules, not an extra sketch-only parameter mode. Legacy
`out`/`ref` call patterns are not target invocation syntax; the executable
profile retains legacy `bool`/`out` beside distinct `bool`/`init ref<T>`
contracts. See [references](specs/language/references.md).

## Test-owned inputs

Syntax fixtures under
[`tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures`](../tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures)
are parser round-trip inputs. The
[`aggregate-abi`](../tests/Terrum.CodeAnalysis.Tests/Native/Interop/Fixtures/aggregate-abi.terrum),
[`checked-bounds`](../tests/Terrum.CodeAnalysis.Tests/Native/SourceGrants/Fixtures/checked-bounds.terrum),
and [`frame-unwind`](../tests/Terrum.CodeAnalysis.Tests/Native/Unwind/Fixtures/frame-unwind.terrum)
inputs are compiler/test fixtures with dedicated callers, host grants, or ABI
assertions; they are not standalone samples. Their execution and qualification
claims are limited to the active tests and probes documented in
[building and native qualification](building.md#native-qualification).
