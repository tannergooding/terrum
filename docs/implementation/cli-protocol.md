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
