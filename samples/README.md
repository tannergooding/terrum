# Terrum samples

Sample folders identify the scenario or language concept being demonstrated;
they are not divided by output backend or by whether the current compiler
supports the source. The [sample catalog](../docs/samples.md) distinguishes
implemented behavior, design-only examples, runtime policy, and observed
evidence.

| Directory | Purpose |
|---|---|
| [`LanguageFeatures`](LanguageFeatures/README.md) | Focused examples grouped by language topic. It contains both implemented examples and design-only sketches. |
| [`Calculator`](Calculator/README.md), [`HelloWorld`](HelloWorld/README.md), [`Mandelbrot`](Mandelbrot/README.md) | Target-language scenario designs; intended behavior is not a compiler execution result. |
| [`CrossModuleRecovery`](CrossModuleRecovery/README.md) | Multi-file checked-recovery example. |
| [`ModuleReplacement`](ModuleReplacement/README.md) | Module provider, consumer, replacement, and compatibility examples. |
| [`ModuleSetServicing`](ModuleSetServicing/README.md) | Multi-provider module set and replacement example. |
| [`DependencyGraphServicing`](DependencyGraphServicing/README.md) | Multi-level dependency graph and servicing examples. |
| [`Win32MessageBox`](Win32MessageBox/README.md) | Terrum source calling a raw Win32 import; execution is interactive. |
| [`Win32Dx11Triangle`](Win32Dx11Triangle/README.md) | Terrum-authored window, Direct3D 11, and shader scenario. |

Parser fixtures belong to
[`tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures`](../tests/Terrum.CodeAnalysis.Tests/Syntax/Fixtures),
not to this user-facing tree. Test-only compiler inputs and ABI probes likewise
stay with their owning test areas. The [library sketches](../library) are
non-executable API designs, not sample dependencies or reference assemblies.
