# Persisted scanner invocation profile proposal

**Status: unapproved proposal.** Semantic-module revision 67 carries the
provisional implementation of the bounded `scanner-10` invocation profile,
introduced in revision 56.
That implementation does not approve this proposal, expand the target
contract, or make the scanner/scalar frontend independent of hosted
verification and emission.

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
copies the source into invocation storage, and returns an integer result.
`NativeJit.ExecuteWithScannerProfile` separately accepts a linked program
retaining the fixed scanner profile, binds fresh storage and returns validated
owned results. The persisted scanner profile uses the existing
`ScannerHost` buffer set:

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
or new scanner semantics. Successful scanner-module execution alone does not pass the self-hosting gate.

## Provisional implementation record (not approval)

Revision 56 persists the current `ScannerHost.InvocationProfileIdentity` and
the ten canonical buffer descriptors in ordinal order. The identity
fingerprints the component/schema and source bound, descriptor names, types,
mutability and capacity formulas, readonly table contents, and token/trivia/
integer record enums. Persisted descriptors contain no values or concrete
capacities. The source grant is part of this named profile; arbitrary profile
composition, graphs, module sets, native DLLs and persisted AOT are rejected
or remain unsupported.

Structured `BoundBufferAssignment` operations are retained as semantic-module
statement tag 27. The scanner JIT adapter binds a fresh `ScannerHost` buffer
set for each source, copies native data after normal completion and validates
tokens, diagnostics, trivia and integer records before returning. Lexical
diagnostics remain successful scan results; a runtime failure does not return
partial scanner buffers. Regression coverage reuses identical persisted
producer/consumer artifacts for empty, varied, malformed-UTF-16 and
maximum-length sources under both runtime policies, compares lexical results
with the hosted scanner, and checks integer magnitudes.

This is implementation evidence for review, not approval of the proposed
support boundary or the related source-grant proposal. The scanner and scalar
frontend still depend on hosted verification/emission, and no self-hosting
gate is claimed.

## Evidence

- [`ScannerHost.CreateBuffers`](../../sources/Terrum.CodeAnalysis/Lexing/ScannerHost.cs)
  defines the current ten-buffer component boundary and its source-derived
  capacities.
- [`Scanner.terrum`](../../self-host/Scanner.terrum) writes token, diagnostic,
  trivia and integer-value records and uses the immutable scanner tables.
- [`NativeJit.ExecuteWithSourceGrant`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs)
  retains the source-only API; the separate scanner adapter binds the named
  profile and publishes validated owned results.
- The [invocation-scoped source-grant proposal](invocation-scoped-source-grant.md)
  remains unapproved and continues to describe only the source-only profile.
