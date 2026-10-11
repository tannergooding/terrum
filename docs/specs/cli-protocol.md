# CLI machine protocol

This chapter owns `cli-machine-envelope/1` and
`cli-execution-supervision/1`. The selected protocol identifier is
`terrum.cli/1`. It is a tooling contract, not a serialization or runtime ABI.
Operation on private `.trm`/`.trmn`/`.tnset`/`.trng` bytes does not establish
[normative interchange](representation/persistence.md), body correctness or
native admission.

The [experimental schema-1/schema-2 reference](../implementation/cli-protocol.md)
is informative. Its raw worker statuses, successful-execution output and
fixed native deadline are not alternate normative modes of this protocol.
This chapter does not claim the WIP CLI implements the selected contract.

## 1. Selection and framing

An invocation explicitly requests `--machine terrum.cli/1`.
Do not infer this mode from a filename, terminal detection, redirected stdout
or the experimental --json option. A provider declines an unsupported
requested version before affected compilation, artifact publication or
execution. With no agreed response version, exit 2 and report the selection
error on the diagnostic channel; do not silently substitute another envelope.
Consumers reject unknown versions rather than interpreting diagnostic English
or enum ordinals. Additional versions/extensions require explicit selection.

In the selected mode, CLI stdout contains exactly one complete UTF-8 JSON
object followed by LF, without BOM, build chatter, human progress or program
bytes. This is not a sequence of events or JSON lines.
Human diagnostic mirrors may use CLI stderr; they are not machine keys.
Every normally reportable outcome, including usage/input/admission and
execution failures, uses the envelope. A fatal host failure or broken protocol
destination may prevent delivery; incomplete/no JSON is a delivery failure,
not an implicit successful or empty response.

Objects have no duplicate or unknown fields in this version. All fields
below are required; null is used only where specified. Strings are JSON
strings, with exact scalar text preserved and controls escaped as required.
Lengths, offsets, indices and durations are nonnegative JSON integers written
without fraction or exponent. Arithmetic is checked. A reader unable to
represent an integer exactly rejects it; binary64 rounding/truncation is not
valid decoding. Program returnStatus is instead a signed int32.

## 2. Common envelope and command outcome

| Field | Type and meaning |
|---|---|
| protocol | Exactly `"terrum.cli/1"` |
| command | Exact selected command identifier, or null if selection failed before one was established |
| outcome | One value from the table below |
| inputs | Ordered array of input records from section 3 |
| diagnostics | Ordered array of diagnostic records from section 4 |
| result | Null or one command-result object from section 5; null when no command was established |
| execution | Null for a nonexecution command; otherwise the record in section 6, even when execution did not start |

Command identifiers are case-sensitive ASCII names. Selection does not
execute source. The core nonexecution identifiers are check, tokens, tree,
analyze, build and build-native-import. The execution identifiers jit,
run-native and run-module use the common execution record.
Other tooling commands require a separately published command contract using
this envelope and one of its result shapes, or a newly selected protocol.
Their existence in an implementation inventory does not adopt a language,
native-import catalog or public compiler API.

| outcome | Meaning | CLI process status |
|---|---|---|
| completed | Requested command completed, with no error diagnostic | 0 |
| source-diagnostics | Invalid source or a required source proof/evaluation failed | 1 |
| usage-error | Invalid command/options or missing required destination | 2 |
| input-error | Input I/O or strict decoding failed | 2 |
| output-error | Artifact, program-output or protocol delivery failed | 2 |
| admission-error | Invalid/unsupported artifact, feature, target or retained contract | 3 |
| resource-error | Tool capacity/work/storage exhausted or unavailable | 3 |
| host-error | Host/tool infrastructure failed | 3 |
| execution-error | Started execution failed or terminated abnormally rather than returning normally | 3 |
| cancelled | Explicit cancellation stopped supervision before complete command success | 3 |
| deadline-exceeded | An explicitly selected execution deadline expired before complete command success | 3 |

Warnings alone do not prevent completed. The source-diagnostics outcome
requires at least one error diagnostic. Failure outcomes identify their actual
category; an unavailable feature is not accepted with a default result.
If multiple failures occur, outcome identifies the failure preventing final
completion; diagnostics retain earlier relevant failures in observation order.
It does not erase an already established program return or committed artifact.

A normally returned program status, including nonzero, is data, not the
supervisor's status. A normally returned int32 -1 therefore permits CLI status
0 with returnStatus -1. Abnormal termination, a fail-fast language failure,
deadline and cancellation must not fabricate a normal program return.
The normal-return observation requires the execution contract's actual
completion report, not just the host process's integer exit value.

## 3. Input identities and coordinates

Each input record has exactly these fields:

| Field | Contract |
|---|---|
| id | Unique invocation-local string; stable in every reference within this response |
| kind | `"file"` or `"stdin"` |
| path | Exact requested path spelling for file; null for stdin |
| contentKind | `"source"` or `"artifact"` under the selected command input contract |
| schema | Null for source or an unestablished artifact schema; otherwise the artifact's exact actual schema |
| state | `"unread"`, `"unavailable"`, `"invalid-utf8"`, `"decoded"` or `"captured"` |
| byteLength | Complete captured original-byte length, or null if unavailable/incomplete |
| signatureLength | 0 or 3 for a complete captured source input, otherwise null |
| rawContentId | Complete original-byte identity, or null without complete capture |
| utf16Length | Immutable snapshot length only for decoded, otherwise null |
| snapshotContentId | Decoded snapshot identity only for decoded, otherwise null |

The inputs array preserves compiler-received input order. Input IDs, not paths
or equal text, identify source locations. A multi-input diagnostic names its
owning input. At most one input is stdin, at its compiler-received position;
it is not required to precede file inputs.
No input path or source content is silently merged by filesystem casing,
content equality or parallel processing. An unread record reports a planned
input not reached after an earlier failure, not an invented snapshot.

Content IDs are objects with exactly algorithm and value fields.
rawContentId uses `"sha256-original-bytes/1"` over every captured original byte.
snapshotContentId uses `"sha256-utf16le/1"` over each preserved UTF-16 unit,
low byte first, without BOM or terminator. value is the 64 lowercase
hexadecimal SHA-256 digits. These identify content, not source principals,
origins, authorization or full semantic/cache dependency equivalence.

[Source input](language/source-input.md) owns strict UTF-8, the optional leading
signature and exact preservation. An invalid UTF-8 record has captured byte
identity/length but no UTF-16 length, snapshot identity, tokens or tree.
An artifact input uses captured for complete raw bytes, never decoded or
invalid-utf8. It has no UTF-8 signature consumption, UTF-16 length or snapshot
identity. Its exact schema/admission is independent of capture; unknown or
invalid bytes do not become semantic interchange by naming a format.
I/O/resource failure is not an invalid-UTF-8 classification without established
malformed bytes. No partially decoded prefix becomes successful source.

## 4. Diagnostics

A diagnostic has exactly code, category, severity, message and location.
code is a stable producer-assigned `TRM` plus four decimal digits.
category is a stable case-sensitive machine category; severity is
`"error"`, `"warning"` or `"info"`. message is human-facing and need not be
stable or English. Dispatch uses code/category, not message or array position.

The code bands are TRM0xxx lexical, TRM1xxx syntax, TRM2xxx CLI/input/output,
TRM3xxx semantic/proof, TRM4xxx target/execution/host, TRM5xxx
artifact/admission/resource and TRM6xxx supervision.
Codes are not exception payload identities or raw host status values.
The category catalog includes input.invalid-utf8, input.io, cli.usage,
output.io, source.lexical, source.syntax, source.semantic, source.proof,
artifact.invalid, admission.unsupported, resource.exhausted, host.failure,
program.failure, program.abnormal-termination, supervision.cancelled and
supervision.deadline. More specific producer codes retain those categories.

location is null for a failure without an established source extent,
otherwise one of these closed shapes:

| coordinate | Other required fields |
|---|---|
| `"utf16-code-unit/1"` | inputId, offset, length, start and end |
| `"original-byte/1"` | inputId, offset and length |

start/end have exactly line and column: zero-based line and UTF-16 code-unit
column under the lexical line map. Extents are zero-based and end-exclusive;
checked offset + length is within the corresponding complete input.
EOF/insertion source or artifact extents may be empty. Byte-input invalid-UTF-8 extents
are nonempty and follow source-input/1's first-malformed-prefix rule, including
the original signature in their basis. They contain no line/column fields.
Do not place byte offsets into UTF-16 fields or fabricate source coordinates
for usage, I/O, unavailable snapshots or native crashes. Artifact-byte
diagnostics use their captured original-byte basis, never a source snapshot.

## 5. Nonexecution results

Every result has exactly kind, complete and the fields assigned to that kind.
No scalar success flag duplicates outcome. complete describes that payload,
not whether a program returned zero.

| kind | Additional fields and scope |
|---|---|
| `"checked"` | stage is `"syntax"` or `"semantic"`; no executable/serialized result |
| `"tokens"` | tokens is the flat token array below |
| `"tree"` | tokens, nodes and roots are the flat tables below |
| `"artifacts"` | artifacts is the array of actually committed artifact records below |

Check publishes checked/syntax; analyze publishes checked/semantic.
Tokens/tree can publish recovered decoded-source payloads with complete false
and source-diagnostics. A source input that failed decoding has no entries.
Completed commands publish complete true. A failure with no valid partial
payload uses null, never a complete empty substitute.
Richer semantic compiler projections require their own selected result schema;
this minimal CLI analysis result is not semantic interchange.

A token has exactly id, inputId, kind, text, span, fullSpan, missing,
leadingTrivia and trailingTrivia. id is its zero-based table index.
kind is one of identifier, keyword, number, string, character, punctuation,
end-of-file or bad; text is exact source spelling, not decoded literal value.
span/fullSpan have exactly offset and length in that input's UTF-16 snapshot;
fullSpan includes the token's attached trivia. missing is Boolean; a missing
token has empty text and zero-length ordinary span at its insertion point.
Trivia items have exactly kind, text and span, with kind whitespace,
line-break, comment, directive or skipped-text.
Supplied text matches its source extent; tables do not normalize spelling.

A node has exactly id, inputId, kind, span, fullSpan and children.
id is its zero-based node-table index; kind names the source grammar
production represented, or `"Recovery"` for an explicitly recovered node.
children is an ordered array of objects with exactly kind (`"node"` or
`"token"`) and id in the corresponding table.
roots has exactly one object with inputId and nodeId for each included input.
All references exist, preserve the owning input and form an acyclic tree;
a table entry has one parent except its root. Child extents fit their parent.
Each published node/token is reachable from its input's root.
Flat tables avoid unbounded source-shaped JSON nesting.

An artifact record has exactly path, kind, schema, byteLength and contentId.
path is its resolved output path; kind is `"semantic"`, `"native"` or
`"private"`. schema names the exact actual format/profile, not a guessed
extension. contentId is sha256-original-bytes/1 of those complete emitted bytes.
Publication uses staged output and atomic replacement per artifact.
Invalid source does not overwrite an existing successful artifact.
On a later multi-output failure, report already committed artifacts with
complete false; do not pretend all outputs rolled back or all were produced.
The selected format's independent admission/ABI obligations remain required.

## 6. Execution and separate program output

The execution object has exactly program, supervision and outputs.
It is present for an execution command even when compilation/admission fails.
Nonexecution results remain null for an execution command.

program has exactly state, returnStatus and hostStatus:

| state | returnStatus |
|---|---|
| `"not-started"` | null |
| `"returned"` | Actual signed int32 normal return; 0 for an admitted void entry |
| `"abnormal"` | null |
| `"unresolved"` | null; supervision could not establish a final program outcome |

hostStatus is null or a target-defined object with exactly kind and code.
For `"windows-process-exit/1"`, code is the actual unsigned 32-bit OS exit code.
Other targets must publish their tag/domain. Host status alone never supplies
returnStatus or changes abnormal into returned.

supervision has exactly deadlineMilliseconds, stopReason and termination.
deadlineMilliseconds is null or a caller-selected positive exact duration.
stopReason is null, `"cancellation"` or `"deadline"`.
termination is `"not-required"`, `"confirmed"` or `"unconfirmed"`, describing
supervisor-requested stopping, not source cleanup or success.
Without a requested stop, or before any worker starts, it is not-required.
An unresolved/unconfirmed outcome is explicit host failure, not a stopped
worker claim. Retain any already established normal-return data if a later
output/supervision failure prevents command success.

outputs has exactly stdout and stderr. Each is a record with exactly
destination, state and writtenBytes. destination is null before selection,
or exactly `{ "kind": "file", "path": <resolved path> }`,
or `{ "kind": "stream", "id": <caller-established channel identity> }`.
state is `"not-opened"`, `"complete"` or `"failed"`; writtenBytes is the exact
number acknowledged by that destination, initially zero. A stream destination
names an actual dedicated channel, not an invented extra OS standard descriptor.
Failed records describe actual output/settlement failures, not merely
forwarding still in progress. A final failure response also waits for the
required settlement attempts and reports their actual outcomes.

The caller supplies distinct program stdout/stderr destinations, disjoint
from CLI stdout and diagnostic stderr. File destinations must not alias each
other, the protocol/diagnostic destinations, source input or selected build
artifacts under the actual target path relation. Validate and acquire required
destinations before launch. Missing destinations are usage-error; acquisition
failure is output-error with no program start. Do not silently inherit the
CLI's streams, discard output, merge the two channels or choose temporary files.
The destination's explicit creation/overwrite agreement still applies.

Forward original program bytes in order within each channel, without text
decoding, newline conversion, JSON/base64 coercion, truncation or unbounded
whole-output buffering. Cross-channel timing/order is not invented.
Use bounded streaming/backpressure and retain actual write failures.
A complete output requires producer EOF, acknowledged forwarding and
successful required flush/close. Supervisor-owned files/duplicates retire
after forwarding; caller-owned endpoints are not destroyed by closing that
duplicate. Process/output/resource lifetime remains the invocation's
responsibility, not a successful-looking envelope with lost bytes.

## 7. Explicit deadlines, cancellation and final reporting

There is no mandatory execution deadline or implicit 30-second language limit.
An explicit positive deadline is measured by monotonic elapsed time from
program launch through completion reporting and required output draining.
Compile/input preparation precedes that interval; any separate tool-wide
budget must be explicitly published, not disguised as program behavior.
Cancellation is a caller-established supervision request, not an adopted
Terrum task/cancellation API.

Commit one final supervision outcome. Complete command success requires an
accepted normal-return report, worker settlement and completed output
forwarding before the selected deadline/cancellation is committed.
A stop committed first prevents completed; a completion committed first
cannot later be changed by a stale stop request. Already observed program
return data remains even if output draining subsequently hits the deadline.
The first committed stopReason remains recorded; a later stop request does
not replace it. A requested stop does not hide actual program, host or output
failures, including failures observed while settling that stop.
Elapsed-time/resource arithmetic must not wrap.

On an explicit stop, stop only the invocation's owned workers/children under
the admitted supervisor contract, settle their actual termination and output
channels, then publish cancelled/deadline-exceeded.
Forced process termination promises no Terrum finally/owner cleanup.
If stopping or forwarding fails, report that actual host/output failure and
unconfirmed/unresolved state as applicable, retaining the requested stopReason
and earlier evidence. Do not kill an unrelated process, fabricate returnStatus,
reclassify cancellation as compile diagnostics or silently drop captured bytes.
Before launch, unavailable required supervision/resource support declines.

The final envelope is attempted once after those observations are stable.
If its delivery fails, return output-error status where the host permits it;
never emit a second success-shaped response into an uncertain partial stream.
Consumers confirm complete parsing and the CLI status as well as outcome.
Raw program status, protocol delivery and command completion are three
different facts.

[Conformance cases](conformance/tooling-cli-protocol.md) cover envelope,
source coordinates, result tables, separate byte streams and supervised
outcomes; they are not executable CLI or process-containment evidence.
