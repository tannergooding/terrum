# Terrum.CodeAnalysis.Terrum

This directory contains maintained Terrum-authored compiler components that
are consumed by the hosted `Terrum.CodeAnalysis` implementation. They are
organized by compiler area, not by a bootstrap status. The hosted compiler
still performs verification and emission; this source tree is not an
independently bootstrapped compiler.

| Directory | Contents |
|---|---|
| `Text` | Source ranges, UTF-16 units/scalars, and related source helpers |
| `Lexing` | Character/string handling, tokens, and scanner components |
| `Syntax` | Bounded Terrum-authored parser components |
| `Semantics` | Bounded scalar frontend, projection, and source readers |
| `Modules` | Terrum-authored module and table components |
| `Utilities` | Shared algorithms and numeric components |

The existing solution build composes selected files into compiler artifacts
under `artifacts\bin\sources\Terrum.CodeAnalysis.Terrum\<configuration>`.
That output does not make every file in this tree a public library or a stable
ABI. See the [implementation inventory](../../docs/implementation/terrum-code-analysis.md)
for the supported boundaries and remaining gaps.
