# CLI machine protocol conformance cases

These cases apply to [`terrum.cli/1`](../cli-protocol.md), independently of the
experimental worker schemas and private artifact formats.
Finite JSON/coordinate/stream/state fixtures do not run an actual compiler,
OS supervisor, filesystem or native program.

| Case | Situation | Required outcome |
|---|---|---|
| MCLI-01 | Valid selected response is delivered | One UTF-8 JSON object plus LF, no BOM/chatter/program bytes |
| MCLI-02 | Consumer sees an unknown protocol version | Reject; no schema-1/schema-2 or ordinal inference |
| MCLI-03 | JSON object repeats a field | Reject duplicate, not last-wins |
| MCLI-04 | An object adds an unassigned field | Reject this version's shape |
| MCLI-05 | Completed command has warnings but no errors | CLI 0; no duplicate success flag |
| MCLI-06 | Required source checking/proof fails | source-diagnostics and CLI 1 |
| MCLI-07 | Usage/input/output fails | Exact outcome and CLI 2 |
| MCLI-08 | Admission/resource/host/execution/cancellation/deadline prevents success | Exact outcome and CLI 3 |
| MCLI-09 | Multiple inputs have equal text or similar paths, with stdin at any received position | Distinct ordered invocation IDs and owning diagnostic references; at most one stdin |
| MCLI-10 | Valid source has an initial UTF-8 signature | Byte identity includes it; UTF-16 snapshot identity does not |
| MCLI-11 | Supplementary scalar and CRLF occur in a snapshot | UTF-16 offsets and zero-based lexical line/columns, not byte/Unicode-scalar counts |
| MCLI-12 | UTF-8 bytes are malformed after a signature | Exact original-byte diagnostic with no fabricated UTF-16 snapshot fields |
| MCLI-13 | Command captures a binary artifact | Raw captured input/schema; no UTF-8 decoding/signature consumption |
| MCLI-14 | Planned input is unread/unavailable | Null unavailable identities/lengths, not empty successful input |
| MCLI-15 | Extent is negative/outside input or uses Boolean/fractional numeric data | Reject invalid exact-integer extent |
| MCLI-16 | A valid integer exceeds the reader's exact supported domain | Explicit rejection, not floating rounding |
| MCLI-17 | Diagnostic message changes language/text | code/category dispatch unchanged; malformed code/severity rejects |
| MCLI-18 | Usage/I/O/native crash has no established source extent | Null location, not offset zero |
| MCLI-19 | Byte extent is placed in UTF-16 fields or refers to unavailable snapshot | Reject coordinate/input mismatch |
| MCLI-20 | Token/trivia records reference decoded spelling | Exact ordinary/full extents and text; no normalization |
| MCLI-21 | Parser inserts a missing token | Empty text and zero ordinary span at insertion |
| MCLI-22 | Multi-input syntax result uses flat node/token tables | Valid reachable roots and owning-input references |
| MCLI-23 | Tree references are missing, cyclic, multiply parented, cross-input or unreachable | Reject malformed table |
| MCLI-24 | Check/analyze publishes minimal stage result | checked/syntax or checked/semantic; no inferred semantic interchange |
| MCLI-25 | Multi-output build fails after one artifact commits | Report actual partial artifacts with complete false; no rollback/success fiction |
| MCLI-26 | Program normally returns nonzero or int32 minimum | CLI completed/0 with exact signed returnStatus data |
| MCLI-27 | Admitted void entry normally returns | returned with returnStatus 0 |
| MCLI-28 | OS unsigned exit value exists without normal-completion evidence | Host status only; no fabricated program return |
| MCLI-29 | Language failure or abnormal worker termination occurs | execution-error with no normal returnStatus |
| MCLI-30 | Source/input/admission fails before launch | execution record reports not-started |
| MCLI-31 | Program destinations are absent | usage-error before launch; no implicit inherited/discard output |
| MCLI-32 | Destinations alias each other, CLI channels, source or artifact | Reject before acquisition/launch |
| MCLI-33 | Destination acquisition fails | output-error; program not-started |
| MCLI-34 | Program emits arbitrary binary bytes on both channels | Preserve exact separate byte sequences and per-channel order |
| MCLI-35 | Output exceeds a supplied finite forwarding buffer | Bounded streaming/backpressure, not whole-output accumulation |
| MCLI-36 | Write fails after acknowledged bytes or after normal return, including during stopping | output-error with actual count/return evidence retained; stop does not hide it |
| MCLI-37 | Required worker/output settlement or EOF/flush/close is not completed | No premature final response, complete-output or completed-command claim |
| MCLI-38 | No execution deadline is selected | No implicit 30-second termination |
| MCLI-39 | Deadline is nonpositive/inexact or elapsed arithmetic is unrepresentable | Reject/report exact failure, not wrap or default |
| MCLI-40 | Deadline commits before command completion, then another stop is requested | deadline-exceeded; original stopReason deadline retained; no invented return |
| MCLI-41 | Return precedes worker settlement, or complete command success precedes a stale stop | Wait for actual settlement; once completed, remain completed |
| MCLI-42 | Return is observed before output draining hits deadline | Preserve returnStatus while command reports deadline failure |
| MCLI-43 | Explicit cancellation occurs before/after launch | cancelled with actual program/termination/output state |
| MCLI-44 | Supervisor force-stops its owned worker | No source finally/owner-cleanup guarantee |
| MCLI-45 | Stopping fails, including with a later output failure or previously observed return | host-error, unresolved/unconfirmed as applicable; retain requested stopReason, output and return evidence |
| MCLI-46 | Final protocol delivery breaks | Delivery/output failure; no second success response |
| MCLI-47 | Actual compiler commands and every source/result production | Document-only: JSON fixtures do not implement source checking or result generation |
| MCLI-48 | Actual OS containment, monotonic deadlines and cancellation delivery | Document-only: logical supervisor ordering is not process execution |
| MCLI-49 | Actual destination aliases, atomic artifact publication and I/O | Document-only: supplied path relations do not prove filesystem behavior |
| MCLI-50 | Actual pipe scheduling, backpressure and worker/output settlement | Document-only: finite byte forwarding is not liveness or OS-resource proof |
| MCLI-51 | Actual artifact/ABI/body conformance | Document-only: an exact machine envelope cannot establish those contracts |
| MCLI-52 | Actual lexer/parser recovery and all valid/invalid syntax trees | Document-only: table checks do not prove grammar recognition |
