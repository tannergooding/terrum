# Persisted scalar frontend v88 summary invocation profile

**Status: UNAPPROVED recommendation, with provisional revision-77
implementation.** This is a distinct profile, not an extension or replacement of
`scanner-10`, acceptance of a frontend projection encoding, or a change to the accepted 64-token
cursor/window contract.

## Bounded recommendation

Use a separately fingerprinted `scalar-frontend-v88-summary-1` profile for
persisted semantic-module JIT on Windows x64. Its coherent operation would scan
one fresh source grant, publish the complete v88 frontend program, and run
`ScalarFrontendProgramReader.TryDecodeScalarFrontendProgram` against that
publication within the same invocation. Export only a scalar entry; keep the
scanner, parser, managed parser state and summary helpers private.

Use the ten canonical scanner buffer descriptors followed by writable
`int32` `Frontend` and `FrontendProgram` buffers, in that order:

| Requirement | Candidate bound |
|---|---|
| Source and scanner tables/results | Existing `ScannerHost` types, access, tables and capacity formulas |
| Source length | At most 16,384 UTF-16 code units, provisional and not an accepted source-size contract |
| Token count | At most 64 including the scanner's real EOF; retain the reader's exclusive frame token-end bound of 63 |
| Function count | 1 through 32 when the publisher produces a complete v88 record |
| `Frontend` | Exactly 2,274 words; zero successful-publication scratch before decoding, then the comparison summary |
| `FrontendProgram` | Exactly 25,025 words; completed v88 program, 32 possible 780-word frames and zero reserved tail |

The whole-invocation token ceiling would be an explicit restriction of this
candidate, not a reinterpretation of the accepted per-window contract. Do not
admit the provisional larger-token parser path through this profile. Do not
fall back to a standalone v87 publication when complete v88 publication or
summary decoding fails.

A separate implementation-only identity,
`scalar-frontend-v88-summary-2`, is also provisionally implemented. It retains
the same twelve-buffer shape and complete-v88-only behavior, but permits up to
`ScannerHost.MaximumSourceLength + 1` scanner tokens (16,385 including EOF).
Its exclusive frame token-end limit is derived from the source-bounded token
buffer; profile 1 continues to pass the fixed limit of 63. Profile 2 is not
part of this 64-token recommendation, does not widen profile 1, and does not
change the accepted cursor/window contract. It remains unapproved.

Recommend expanding the provisional function capacity from 10 to 32 across
both identities without changing the per-function v88 encoding. Keep the
summary and each function frame independent:
`Frontend = 2 + 32 * 7 + 32 * 64 = 2,274` words,
`ProgramFrame = 4 + 712 + 64 = 780` words, and
`FrontendProgram = 4 + 32 * 780 + 61 = 25,025` words. The program buffer is
about 98 KiB, bounded independently of the source token ceiling. The 64-bucket
temporary function-name table uses at most half its capacity at 32 functions.
The fingerprint must continue to bind the function limit and derived buffer
shape. This is a bootstrap-capacity recommendation only; it does not accept the
invocation profile, span persistence, or any source-level function-count
contract.

The separate token-window and node-capacity proposals retain a ten-function
recommendation. Reconcile that scope with this provisional 32-frame capacity
before accepting any of these proposals; implementation support is not
approval of either function-count decision.

The identity must cover the scanner fingerprint, both frontend buffer
descriptors/capacities, v88 layout and summary encoding, resource ceilings and
entry/result convention. Persist only that requirement and symbolic buffer
operations, never seed values, source-derived capacities, addresses or trusted
analysis results. Link/reference/replacement validation must compare this
identity, not infer it from buffer names or reuse the scanner identity.

The provisional scalar `int32` entry result contains the scanner token count only
after successful complete publication and decoding; nonpositive results
represent failure, not an empty valid program. The host adapter must reject
incompatible metadata, source length, buffer types/access/capacities or
publication before returning a result. Allocate invocation-owned source and
disjoint writable outputs anew, and return only owned audited snapshots after
normal completion.

## Private-span boundary

The current complete frontend requires private `Span<Token>` parameters,
snapshot-to-span construction, length queries, unsigned indexing and descriptor
retirement. Revision 77 provisionally persists this private IR. Adding two buffer descriptors
alone would not serialize or verify these operations. Even with the unused
`TokenCursor` component omitted, the required
`TryGetScalarWindowToken` path still contains a span.

The exact private-span persistence slice remains a separate unapproved
recommendation. Its implementation retains the
complete admitted structured IR and compiler-known Token/SourceRange/
TokenSnapshot roles through module-local type remapping, and rederives backing,
staging and lifetime obligations during mandatory verification after reading
and linking. The new exact revision leaves revision-76 span rejection intact
and persists no trusted provenance.

This corpus does not require `TokenCursor` capture storage, `AdvanceOne`,
returned span forwarding or an exported/imported span ABI. Keep those features,
general views/references, arbitrary profile composition, native DLLs/packages,
graphs/module sets, persisted AOT and source-free TRRM outside this candidate.
Existing managed-leaf restrictions must still hold. See the separate
[private-span persistence recommendation](token-span-semantic-module.md);
the concrete corpus justifies this provisional slice but accepts none of its contracts.

Continue provisional implementation within these exact gates; do not replace
the Terrum publisher with hosted parsing, source-rewritten buffer reads,
captured precomputed publications or a scanner-only success result.

## Required evidence and host boundary

Reuse identical persisted producer/consumer artifacts for different fresh
sources under fail-fast and checked-exceptions, including ten compact empty
`void` functions (61 scanner tokens including EOF), smaller programs, exact
resource bounds and rejected inputs. Confirm artifacts contain no seed data.
Prove invocation and retained-result isolation and reject altered identities,
descriptor shapes, insufficient capacity, malformed IR and invalid source
grants before unsafe execution.

Reuse the scanner host readers for token/trivia/diagnostic/integer publication.
Validate the unchanged complete v88 program against the original source and
policy with `ScalarFrontendHost.ReadProgram`. Complete-v88 success fixes the
original publication scratch to zero; the host adapter reconstructs that
protocol-required zero buffer separately from the copied summary, only after
checking the completed v88 envelope.
Compare every summary descriptor, packed field, call slot and unused word with
the independently audited host projection. Decoder success alone is not
semantic validation. Reject zero, incomplete, malformed and semantically
corrupted publications explicitly; never turn failure into empty success.

Ordinary frontend AOT/JIT and persisted `scanner-10` checks are independent
prerequisites, not evidence that this complete persisted path executes. Actual
implementation and rejection evidence belongs in the
[implementation reference](../implementation.md) and
[self-host inventory](../implementation/self-host.md), not an approval declaration.
