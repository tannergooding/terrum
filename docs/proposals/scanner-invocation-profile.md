# Persisted scanner invocation profile proposal

**Status: unapproved proposal.** The current revision-55 source-grant JIT
continues to accept only one readonly invocation-scoped `Host.Source` grant.
This proposal does not change that contract or claim persisted scanner support.

## Summary

The Terrum-authored scanner consumes more than source text. `ScannerHost`
currently supplies immutable source and scanner tables, plus mutable token,
diagnostic, trivia and integer-value buffers. Persisted semantic-module JIT
currently has no binding model for these auxiliary buffers, and its
source-grant entry point returns only an integer rather than copied scanner
records.

This proposal considers a named, bounded invocation profile for the existing
`scanner-10` component. It is not a general multi-buffer capability, a new
scanner contract, or support for the scalar frontend, compiler-primitives DLL,
graphs, module sets or persisted AOT.

## Decision question

Should persisted semantic-module JIT support a named scanner invocation
profile, in addition to the existing source-only profile?

## Recommendation

Yes, consider a scanner-specific profile tied to the existing
`ScannerHost.SchemaVersion` and `ScannerHost.ComponentName`, rather than
generalizing source grants to arbitrary caller-defined buffers. Reuse the
existing `ScannerHost.CreateBuffers`, record readers and hosted verifier/JIT
path. Bind fresh storage for every invocation and copy all result buffers to
host-owned results before releasing native memory. If the implementation
cannot preserve that fixed profile and result publication boundary, defer
persisted scanner execution.

## Existing boundary and profile shape

`NativeJit.ExecuteWithSourceGrant` accepts exactly one readonly invocation-
scoped `char16` source grant, rejects contents longer than 16,384 code units,
copies the source into invocation storage, and returns an integer result. The
persisted scanner profile would need the existing `ScannerHost` buffer set:

| Buffer | Access | Existing shape or capacity |
|---|---|---|
| `Source` | Readonly `char16` | At most 16,384 UTF-16 code units |
| `XidStart`, `XidContinue` | Readonly `int32` | Inclusive Unicode identifier range pairs |
| `Keywords`, `EscapeKeywords`, `Operators` | Readonly `int32` | Packed kind/length/code-unit catalogs |
| `Tokens` | Writable `int32` | Three words per token, with space for EOF |
| `Diagnostics` | Writable `int32` | Count plus three words per diagnostic |
| `Trivia` | Writable `int32` | Count plus four words per trivia record |
| `IntegerValues` | Writable `int32` | Count plus five words per integer record |

`IntegerValues` is an output buffer, not a readonly table. The current
capacities are derived by `ScannerHost.CreateBuffers`; they are not a new
caller-selected sizing policy.

## Contracts to settle

1. **Profile identity and compatibility.** Tie the requirement to the
   `scanner-10` component identity and define how the Unicode identifier
   tables, keyword/escape/operator catalogs, token kinds and output record
   layouts are pinned together. A replacement must not silently change those
   meanings. Do not infer identity from buffer names alone.
2. **Persisted requirements and validation.** Persist only the named profile
   requirement and symbolic buffer operations, never sample contents,
   addresses or sample-derived bounds/effect proofs. Define how a scanner
   profile is propagated and checked during producer/reference/link
   replacement, and how it composes with the existing source-only requirement.
3. **Binding, storage and resource limits.** Require the canonical readonly
   tables and fresh, correctly typed, disjoint output storage with the
   established capacities. Reject missing, duplicate, incompatible or
   unsupported bindings before entry. Define checked capacity arithmetic,
   aggregate limits, invocation lifetime and how mutable aliases are
   excluded.
4. **Result publication and failure.** Copy token, diagnostic, trivia and
   integer-value records to host-owned results before native invocation
   storage is released. Distinguish a completed scan containing lexical
   diagnostics from a runtime failure; never report partial runtime-failed
   buffers as a successful scan.
5. **Acceptance evidence.** Reuse `ScannerHost` readers and existing scanner
   differential oracles. Reuse identical persisted artifacts across empty,
   maximum-length and differently sized sources; cover malformed UTF-16,
   lexical diagnostics and all four result buffers. Reject invalid profiles
   and demonstrate fresh-storage isolation. Any parser/binder integration
   must continue through hosted verification and emission and must not fall
   back to the host lexer.

## Explicit exclusions and stop condition

This profile would establish only persisted semantic-module JIT execution of
the existing scanner component. It would not make the scanner a
compiler-primitives dependency, move the scalar frontend beyond hosted
verification/emission, define new lexical behavior, or establish support for
module graphs/sets, native DLLs or persisted AOT.

Defer the proposal if satisfying it requires arbitrary buffer capture,
escaped views, general host-buffer registration, broader linking/AOT support
or new scanner semantics. Successful scanner-module execution alone would
not pass the self-hosting gate.

## Evidence

- [`ScannerHost.CreateBuffers`](../../sources/Terrum.CodeAnalysis/Lexing/ScannerHost.cs)
  defines the current ten-buffer component boundary and its source-derived
  capacities.
- [`Scanner.terrum`](../../self-host/Scanner.terrum) writes token, diagnostic,
  trivia and integer-value records and uses the immutable scanner tables.
- [`NativeJit.ExecuteWithSourceGrant`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs)
  currently binds only the source grant and returns an integer result.
- The [invocation-scoped source-grant proposal](invocation-scoped-source-grant.md)
  remains unapproved and continues to describe only the source-only profile.
