# Terrum.CodeAnalysis.Terrum.Tests

This directory holds Terrum-authored test sources and ABI adapters used by
the C# NUnit harness in
[`Terrum.CodeAnalysis.Tests`](../Terrum.CodeAnalysis.Tests/README.md). It is a
Terrum test-source area, not a separate .NET test project or a replacement for
the hosted test runner.

| Directory | Contents |
|---|---|
| `Text`, `Lexing`, `Utilities` | Terrum components exercising source, token, and utility behavior |
| `Modules` | Inputs for validating module tables and artifacts |
| `TestHelpers` | Bounded Terrum test runners and shared test components |
| `TestHelpers/Abi` | Test-only adapters for selected compiler component ABI checks |
| `Native/Bootstrap` | Terrum inputs used by bootstrap-oriented native tests |

The C# tests select and compose these files explicitly. Their location does
not imply bootstrap completion, public API status, or a stable native ABI.
See the [Terrum-authored compiler source area](../../sources/Terrum.CodeAnalysis.Terrum/README.md)
and [implementation inventory](../../docs/implementation/terrum-code-analysis.md).
