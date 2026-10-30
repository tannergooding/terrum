# Experimental CLI responses

This is an informative reference for the development CLI, not the selected
[`terrum.cli/1` machine contract](../specs/cli-protocol.md).
The latter has one supervisor envelope, separate program status and dedicated
program-output destinations. No implementation migration or conformance claim
follows from that selection.

## Existing schemas and worker behavior

The experimental check, tokens, tree, analyze, build and build-native-import
commands distinguish inspection/build diagnostics from execution.
Build-native-import opts into source-authored Windows x64 native imports;
ordinary build does not enable those imports.
These nonexecution commands return 0 on success, 1 for source/compilation
diagnostics and 2 for usage/input/output failures. Execution workers instead
return raw program status. Run-native currently supervises its native child
with a 30-second deadline and reports TRM5004 on deadline failure.

## Provisional source arithmetic selection

Source-compiling commands accept the provisional
`--arithmetic-profile development|release` selector. Values are exact and
case-sensitive; omission selects Development. It applies to eligible source
compilation commands, including hosted builds and analysis, module/TRRM/native
artifact producers, and frontend build/library/run commands. It is rejected by
syntax-only, scanner-only, source-free and artifact-only commands. For
frontend build/library/run commands, the selected profile is forwarded to the
original-source C# compiler process. In the explicit test-only
`frontend-component` probe, it applies to the subject source while the
component itself is compiled with Development. Arithmetic selection is
independent of `--runtime`, and invalid or ineligible uses follow the existing
usage-error path before output replacement. This implementation follows the unapproved
[source arithmetic selection proposal](../proposals/source-arithmetic-profile-selection.md);
it does not adopt or amend the selected `terrum.cli/1` protocol.

## Provisional normal-backing agreement selection

The source `analyze`, `build`, `build-native-import` and `jit` commands accept
the exact, case-sensitive
`--native-lowering-agreement terrum.experimental.windows-x64.normal-backing`
selector. Other commands, repeated or malformed selections, and library/Core
output combinations reject it as a usage error. The choice is a host lowering
selection, not a TargetProfile, semantic schema or persisted authorization.
Analysis responses publish `"profile": null` and identify the selected key
separately as `nativeLoweringAgreement`; they do not label it as one of the
existing private native profiles. Native `build` and `jit` now execute the
selected agreement for source-bound programs whose normal class payloads
contain only scalar/native-address or inline-copy fields. This slice supports
normal class creation, handle copies, borrowed receiver calls, scope retirement
and checked construction rollback under both runtime policies. Reference
descriptors, managed class fields, handle results/by-value handle parameters,
Cleanup, foreign calls and source-independent indexed bodies remain
unsupported and reject before emission. `build-native-import` accepts the
selector but still rejects at its native-import lowering boundary. No route
falls back to the address-only or tracing representation. This provisional
implementation follows the unapproved
[normal-backing proposal](../proposals/reference-normal-backing-support.md);
it does not adopt or amend the selected `terrum.cli/1` protocol.

Use the built terrumc.dll/executable for stdout isolation; dotnet run may print
build messages. The `-` input path reads stdin. Human diagnostics use stderr;
--json writes the implemented response on diagnostic/inspection paths.

Schema 1 publishes language version, coordinate system, path, source length,
success state and diagnostics. Parsed commands publish a content ID.
Diagnostics carry code/severity/message and source span/range.
Usage/input failures have no fabricated source position.
Multi-source compiler errors use schema 2 and identify the owning input.
These names/versions do not select the new protocol despite their similar
numeric version labels.

Token output preserves exact text, ordinary/full spans, missing state and
structured trivia. Tree output is a flat node table with root index and
node/token child references. Messages are human-facing, not stable dispatch
keys. Existing code bands cover lexical, syntax, CLI/I/O, semantic,
native-host and bounded-artifact diagnostics.

Target file/stdin input is subject to the selected
[source-input boundary](../specs/language/source-input.md).
An invalid-byte file cannot be made conforming by filling schema-1 UTF-16
fields from a partially decoded prefix. The implemented schema does not
gain byte-coordinate fields from prose or from the newly selected protocol.

## Execution distinction

The development [JIT worker](jit.md) returns raw int32 program status,
or zero for a void entry. Its --json option formats compiler diagnostics;
successful execution does not emit a supervisor success envelope.
The current successful worker output therefore cannot be consumed as a
conforming terrum.cli/1 execution response.
A caller should explicitly supervise possibly nonterminating native work;
the selected protocol imposes no mandatory default deadline.

Private [module](modules.md), [set](module-sets.md) and [graph](native-graph.md)
command/output examples retain their own implementation meanings.
Their private schemas, containers and limits are not normative interchange,
new machine-mode status rules or automatic adoption of compiler APIs.
