# Persisted scalar frontend v88 summary invocation profile

**Status: unapproved, with provisional private-module implementation.**
This is neither `scanner-10`, acceptance of the scalar projection nor a
widening of the [accepted cursor exception](span-backed-token-cursor.md#accepted-bounded-exception).

## Candidate profiles

Recommend a separately fingerprinted Windows x64 semantic-module JIT
operation: scan one fresh snapshot, publish a complete v88 program, then run
`ScalarFrontendProgramReader.TryDecodeScalarFrontendProgram` in that same
invocation. Keep scanner/parser/state/summary helpers private and export only
a scalar entry. Do not replace that producer/reader execution with hosted
parsing, rewritten source or captured precomputed records.

Two candidate identities select different **whole-invocation** token limits:

| Identity | Token ceiling including real EOF | Decoder's exclusive frame-token-end limit |
|---|---|---|
| `scalar-frontend-v88-summary-1` | 64 | 63 |
| `scalar-frontend-v88-summary-2` | Source-derived, at most 16,385 | Derived from the selected source-bounded token buffer |

Profile 2 is separately implemented but remains unapproved; adopting profile 1
would not adopt profile 2. Neither identity changes per-window source semantics
or silently selects the other when input is too large.

Both candidates use the canonical ten
[scanner descriptors](scanner-invocation-profile.md#candidate-identity-and-binding),
then writable `int32` `Frontend` and `FrontendProgram`, in that order.
The [common projection proposal](bootstrap-frontend-profiles.md#scalar-projection-and-resources)
owns the coherent 32-function/64-node/four-parameter candidate, replacing the
overlapping ten-function and v86 recommendations. Invocation-specific bounds
are:

| Resource/publication | Candidate |
|---|---|
| Source | At most 16,384 UTF-16 code units; not a language/file-input maximum |
| Completed program | 1–32 functions, complete v88 only |
| `Frontend` | Exactly 2,274 words; zero publication scratch, then complete comparison summary |
| `FrontendProgram` | Exactly 25,025 words, including 32 possible 780-word frames and 61 reserved zero words |

Persist only the exact profile requirement and symbolic operations. The
fingerprint binds the scanner identity, descriptor order/types/access,
capacity formulas, v88 layout, summary/result convention and resource ceilings.
Current runtime policy is separately retained and compared by the adapter:
compatibility requires **both** the matching profile and matching policy,
not a claim that the current fingerprint hashes policy. No buffer-name
inference, scanner-only success, standalone-v87 fallback or automatic legacy
decoder is allowed.

Changing private tags or capacities is possible before adoption, but requires
one explicitly selected exact schema and corresponding identity. Preserving a
numeric development revision is not preserving a schema. This proposal makes
no standard TRRM assignment.

## Invocation and publication

Validate retained requirements, source, policy, descriptor shape and checked
capacities before entry. Bind fresh invocation-owned source and disjoint
writable outputs. Readonly catalogs and source cannot alias writable data.
Keep storage valid through synchronous execution and defined cleanup.

The scalar `int32` result is a positive scanner token count **only after**
complete publication and summary decoding. A nonpositive result is failure,
not an empty valid program. Runtime failure, malformed publication and
incompatible metadata must not produce a successful partial result.

After normal completion:

1. Copy and validate every scanner result family using its host reader.
2. Check the completed v88 envelope before reconstructing the
   protocol-required all-zero publication scratch in separate host storage.
   The Terrum reader has overwritten the original scratch with its summary;
   do not pass that summary to an API expecting publication scratch.
3. Keep the copied program unchanged. Audit it against the **original source
   and selected policy** using `ScalarFrontendHost.ReadProgram`, then perform
   the ordinary mandatory semantic verification.
4. Compare all 2,274 summary words with the independently audited projection:
   descriptors, packed fields, call targets and every unused word. The
   call-target area starts at word 226. Successful structural decoding alone
   establishes none of the source/type/flow claims.
5. Return only owned audited snapshots before releasing invocation backing.
   Later invocations must not mutate retained results.

The fixed layout is a bounded implementation choice. A substantive alternative
would replace the summary with a general verified bound-program handoff;
that needs its own source association, ownership, schema and trust design.
Prefer this narrow exact profile for the current experiment, not as a
universal compiler protocol.

## Private-span prerequisite

The complete producer/reader requires snapshot-to-span construction, private
readonly `Span<Token>` parameters, copies, length, unsigned indexing and
descriptor retirement. Adding two buffer descriptors does not persist those
operations. The [private-span proposal](token-span-semantic-module.md) owns
their exact IR, type roles/remapping and mandatory backing/staging/lifetime
reconstruction.

Do not tie this profile to “revision 77 only”: combined private layouts in
current fixtures use a later exact revision. Numeric revision tags are
implementation evidence, not a persisted span contract.

This corpus does not require cursor captures/`AdvanceOne`, returned span
forwarding or public/imported/exported span signatures. Keep those separate,
along with general references/views, arbitrary profile composition, native
DLLs/packages, graphs/module sets, persisted AOT and source-free TRRM.

## Acceptance gates

Adopt each token identity, the common projection envelope and private-span
slice explicitly; implementation of one is not acceptance of the others.
Required evidence:

- Identical persisted producer/consumer artifacts across smaller, varied,
  empty/invalid and maximum sources under both policies, with no seed data.
  Ten compact empty `void` functions (61 tokens including EOF) is a useful
  profile-1 case, not proof of all 32 frame slots.
- Exact 64-token versus larger-token identity isolation; node, function and
  source boundaries independently, including 64/65 nodes and 32/33 functions.
  Vary comments/whitespace separately from token count.
- Four-parameter/named/output call modes, fourth children in the overflow node
  bank, namespace/alias identity, reserved words and call targets across frames.
- Rejection of altered identities, policies, descriptors, capacities, malformed
  private IR and invalid grants. Recheck replacements; reject source-free
  admission of source/span requirements.
- Zero/incomplete envelopes, source-correlated semantic corruption and every
  summary field/unused-word mismatch. Distinguish a completed lexical-diagnostic
  scan from successful frontend publication.
- Fresh-storage and retained-result isolation, checked cleanup and failure
  paths. Qualify ordinary frontend and persisted scanner prerequisites
  separately; neither proves this complete persisted operation.

Current evidence entry points are the
[host adapter](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs),
[invocation auditor](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.Invocation.cs),
[complete-program regressions](../../tests/Terrum.CodeAnalysis.Tests/ScalarFrontendInvocationProfileTests.CompleteProgram.cs)
and [private-span regressions](../../tests/Terrum.CodeAnalysis.Tests/ScalarFrontendInvocationProfileTests.PrivateSpans.cs).
The [composition inventory](../implementation/bootstrap-frontend-composition.md)
owns current support. No independent compiler bootstrap or accepted profile
follows from those tests.
