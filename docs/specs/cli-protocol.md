# CLI machine protocol

The CLI distinguishes inspection/build diagnostics from execution results.
`check`, `tokens`, `tree`, `analyze`, and `build` do not execute source.
Inspection/build commands return 0 on success, 1 for source or compilation
diagnostics, and 2 for usage or input/output failures. Execution workers
return the program's raw status. `run-native` supervises its native child with
a 30-second deadline and reports `TRM5004` on deadline failure.

Use the built `terrumc.dll` or executable when stdout must contain only
protocol output; `dotnet run` may print build messages. The `-` input path
reads stdin. Human diagnostics are written to stderr; `--json` writes one
protocol object to stdout, including on failure.

Schema 1 publishes the language version, coordinate system, path, source
length, success state, and diagnostics. Parsed commands also publish the
content ID. Diagnostics contain stable codes, severity, message, and source
span/range; usage and input diagnostics have no fabricated source position.
Token output preserves exact text, full and ordinary spans, missing state,
and structured trivia. Tree output is a flat node table with a root index and
child references to node/token indices, avoiding source-sized nested JSON.

Diagnostic messages are human-facing, not stable dispatch keys. Code bands
are `TRM0xxx` for lexical errors, `TRM1xxx` for parsing, `TRM2xxx` for
CLI usage/I/O, `TRM3xxx` for semantic analysis, `TRM4xxx` for native
emission/runtime-host failures, and `TRM5xxx` for bounded module/scanner
interchange errors. Schema changes require versioning; consumers must reject
unknown versions or explicitly negotiate them, never infer meaning from enum
ordinals or diagnostic English.

This describes the implemented protocol, not a promise that experimental
source syntax or all public compiler APIs are stable. See the
[implementation summary](../implementation.md) and
[build guide](../building.md).
