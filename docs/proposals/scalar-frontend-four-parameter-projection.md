# Scalar frontend four-parameter projection

**Status: UNAPPROVED recommendation.** This proposal records a bounded
implementation direction only. Provisional implementation or test support
does not accept the encoding or change the canonical language, metadata or
execution contracts.

## Recommendation

Add a new current complete-program projection tag (`v86`) for up to four
scalar parameters and direct-call arguments. Keep the existing standalone
compact/tree `v85` projection limited to three parameters. Route a single
four-parameter function through the complete-program path, and reject old
`v85` complete-program records rather than supporting two complete-program
layouts.

Preserve the complete-program buffer, frame and tree sizes. Store the fourth
parameter type and name in the currently unused tree-header words `[18..20]`.
Extend the complete-program signature masks to four bits. Keep the first
three call roots in their existing node fields and encode a fourth root and
four two-bit formal mappings in the call-only `High` word. The hosted reader
must validate all encoded fields against the source and child-order,
uniqueness, target and publication invariants before binding. Keep the
success-only summary decoder non-authoritative; hosted source auditing,
binding, verification and emission remain required.

The implementation limit is exactly four scalar parameters and arguments,
using the existing direct-call and scalar mode contracts. This does not
change the native ABI or approve arbitrary arity, overloads, new call modes,
or standalone `v85` encoding changes.

## Recommended encoding

Keep the four-word program envelope, ten 428-word frames, 37-word function
header, 32 eight-word nodes and 392-word summary. In each v86 function payload,
store the fourth parameter type in word `[18]` and its source-name span in
`[19..20]`; preserve the existing three parameter fields. Use the existing
out/init-ref masks as four-bit masks, rejecting any bit beyond the declaration
arity. The fourth header triplet must be zero when the function has fewer than
four parameters.

For call nodes, retain the first three child roots in `Left`, `Right` and
`Low`. Reinterpret the call-only `High` word as follows:

| Bits | Meaning |
|---|---|
| 0–2 | Argument count, 0–4 |
| 3–10 | Four two-bit source-ordinal-to-formal mappings |
| 11 | Named-argument marker |
| 12–17 | Fourth root plus one; zero when absent |
| 18–31 | Reserved and zero |

When the named marker is clear, mappings are zero and positional mapping is
implicit. When set, mappings must be a permutation of the declared formals;
entries beyond the argument count are zero. The fourth root is present exactly
when the count is four. The reader validates the packed form, then exposes a
normalized arity, explicit fourth child, and mapping to hosted binding.

Keep the seven-word summary descriptors and their existing buffer positions.
Pack return and four parameter type codes in descriptor word `+4` at bits
0, 4, 8, 12 and 16; put flags at bit 20, arity at bit 24 and the four-bit out
mask at bit 27. Put the four-bit init-ref mask in descriptor word `+6` starting
at bit 8, above the existing statement/local counts. Summary decoding remains
success-only and structural, not authoritative semantic verification.

## Unresolved decision

Whether to accept and standardize this projection encoding remains open.
The current provisional `v88` implementation supports four-parameter
projections alongside the separate 64-node-capacity experiment. That is
implementation coverage only; the `v86` layout above remains this proposal's
recommendation, and no specification or roadmap status has changed.
