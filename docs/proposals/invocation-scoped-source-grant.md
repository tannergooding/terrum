# Invocation-scoped source grant proposal

**Status: unapproved proposal.**

## Summary

The bounded `SourceLines`, scanner and scalar-frontend components read the
immutable UTF-16 `Host.Source` grant. Hosted compilation can bind that grant as
a read-only `char16` host buffer. Semantic-module revision 55 now supports the
bounded invocation-scoped source facade described below; the maintained
compiler-primitives library and other execution paths remain source-independent.

This proposal concerns only how persisted modules may declare and receive the
existing source grant. It does not change source decoding, UTF-16 coordinates,
line-break behavior, the scanner contract, or the bounded frontend's reliance
on hosted verification and emission.

## Decision question

Should a persisted module be allowed to require an immutable source snapshot
supplied by its caller for each invocation?

## Recommendation

Yes, add a narrow `Host.Source` module dependency, distinct from arbitrary
host buffers and from stored references. Persist only the dependency identity
and its required read-only `char16` shape. The caller supplies the actual
snapshot for each invocation; module artifacts must not contain source code
units or retain a source address beyond that invocation. Existing checks for
source ranges, malformed UTF-16 and line coordinates remain unchanged.

This is the smallest direction that lets existing source-aware components
participate in a persisted compiler library without inventing general buffer
capture, reference or span semantics.

## Provisional first implementation slice

**Status: implemented provisionally; proposal remains unapproved.**

**Summary:** The broad recommendation does not define invocation extent,
dependency propagation, source-length binding, or the initial execution
backend. The existing `SourceText` and `SourceLines` facades need only four
read operations; the scanner and scalar frontend additionally depend on
writable interchange buffers and other contracts outside this proposal.

**Recommendation:** First persist only the four-operation source facade as one
development semantic producer module, with `SourceRange` and
`CharacterFacts` as host-neutral dependencies. Invoke it through an
independently persisted consumer using an explicit source-binding JIT adapter.
Keep source length and reads symbolic in the module; at each invocation bind
the supplied snapshot into fresh execution-owned storage, lower those
operations to the existing checked host-buffer representation, verify the
linked program, then use the hosted JIT emitter. Reusing module bytes does not
require reusing generated machine code.

**Implemented provisionally:** Semantic-module revision 55 stores the
requirement bit and symbolic length/read operations. References propagate the
requirement; linking checks it; `NativeJit.ExecuteWithSourceGrant` validates
and copies one eligible source snapshot before hosted verification/emission.
The native emitter, graph and module-set paths reject unbound or unsupported
source requirements. No general buffer dependency or source-aware maintained
compiler-primitives DLL was added.

The same capability also runs the bounded Terrum-authored `Utf16Scalar`,
`CharacterLiteral` and `StringLiteral` leaf decoders and the provisional `StringRow` name and
namespace encoders for ASCII, UTF-8, UTF-16LE and UTF-32LE as persisted
semantic-module providers. This reuses the existing requirement and grant
validation; it does not make `StringRow` a TRRM section writer or make the
scanner or scalar frontend source-independent, independently
verified, or executable outside the hosted verifier/emitter path.

The provisional contract for this slice is:

- A module may require only the reserved identity `Host.Source`, with readonly
  `char16` elements and invocation lifetime. The requirement is module-level,
  exposed through references and propagated through retained producer
  dependencies. Replacement must preserve the recorded requirement.
- One synchronous entry invocation, including nested calls and defined
  unwinding/cleanup, observes one immutable snapshot. Different invocations
  bind independently; there is no process-global current source, capture,
  suspension, or rebinding during execution.
- The caller explicitly supplies exact UTF-16 code units, copied into
  invocation-owned backing. Empty source is valid and distinct from a missing
  grant. This first profile accepts at most 16,384 code units, preserving
  malformed surrogates, NUL and line endings. Missing, duplicate, writable,
  wrong-element-type, invalid-code-unit or oversized grants reject before
  entry execution.
- Artifacts retain only the requirement and symbolic source operations, never
  invocation contents, concrete length or address. Source-dependent operations
  cannot use a compile-time sample length for constant evaluation or persisted
  bounds/effect proofs. Checked indexing and conditional-output publication
  retain their existing behavior.
- The capability cannot become a source value, field, pointer, returned view
  or retained handle. Copied code units, scalar coordinates and `SourceRange`
  values may escape; they carry no source backing or implicit snapshot
  identity.
- Initial support covers a single-producer semantic-module JIT execution only.
  Source-dependent native DLLs, native packages, graphs and persisted AOT
  images continue to reject explicitly. A reusable AOT contract requires a
  separate invocation adapter; embedding a snapshot in an executable does not
  satisfy this profile.

**Acceptance evidence:** `SourceFacadeModulesBindFreshSnapshotsAndRejectInvalidGrantsAsync`
reuses identical producer and consumer artifacts across empty, differently
sized, malformed-surrogate and mixed-line-break snapshots. It compares the
facade with hosted `SourceText`, checks failed-output vacancy, rejects invalid
grants, and preserves host-neutral module behavior. The focused source,
module, native-package, module-set and line-component selection passed 399
tests. Persisted-index coverage also checks invocation-relative `^1` reads
across empty and one-code-unit snapshots and resolves call-valued indexes.
`CharacterLiteralModuleUsesFreshSourceGrantsAsync` and
`StringLiteralModuleReadsFreshSourceGrants` additionally passed under both
runtime policies, comparing decoded scalar results and lexical validity
against the hosted lexer across fresh grants.
`StringRowModuleMatchesAllFormatsAcrossFreshSourceGrants` also passed under
both policies, comparing name and namespace row bytes for ASCII, UTF-8,
UTF-16LE and UTF-32LE with the hosted string-table writer. It also checks
empty namespace rows and rejects empty names, non-ASCII text under ASCII, and
embedded zero scalars across fresh grants.
`StringRowComparisonsUseFreshSourceGrantsAcrossModuleJit` passed under both
policies, comparing the ordinal decoded-scalar name and namespace predicates
with hosted ordinal string equality and checking invalid empty-name and
embedded-zero inputs.
`Utf16ScalarModuleDecodesFreshSourceGrants` passed under both policies,
checking scalar values and code-unit widths for BMP and supplementary
characters and rejection of unpaired surrogate code units.
`CharacterFactsModuleClassifiesEveryUtf16CodeUnit` passed under both policies,
checking the ASCII-digit and line-break predicates against independent host
conditions for all 65,536 UTF-16 code units in four bounded fresh grants.
`SourceLineIndexModuleIndexesFreshSourceGrants` passed under both policies,
reusing persisted provider and consumer artifacts across empty, ordinary,
mixed-line-break and malformed-surrogate snapshots while comparing line
counts, content/full-break extents, position mappings, offset round-trips and
invalid query boundaries with the hosted snapshot.
`SourceLinesModuleQueriesFreshSourceGrants` also passed under both policies,
reusing persisted streaming-component artifacts across fresh grants and
comparing full line extents, positions, offsets and invalid-query results with
the hosted snapshot.
`StringEscapeModuleAnalyzesFreshSourceGrants` passed under both policies,
comparing decoded scalars, consumed widths and error spans with hosted lexer
diagnostics across fresh escape grants, including malformed Unicode escapes
and escaped unpaired surrogates.
`IntegerParsingModuleReadsFreshSourceGrants` passed under both policies,
reusing the persisted decimal parser provider and consumers across fresh
snapshots and checking signed/unsigned 32-/64-bit and `char16` results,
boundaries, separators and rejected inputs.
`IntegerParsingRadixModuleMatchesHostOracleAcrossFreshSourceGrantsAsync` also
passed under both policies, comparing all five parser APIs across radices
2–36, their type boundaries and overflows, invalid digits/radices and the
radix-specific separator rules over fresh grants.
`IntegerFormattingModuleMatchesFreshSourceGrantsAsync` passed under both
policies, comparing decimal and radix-2..36 formatting for signed/unsigned
32-/64-bit values and `char16` against a host-side oracle. Its test-only wrapper
reads expected text from the grant; formatting remains source-independent, and
unique output workspaces do not cross the module boundary.
`StringLiteralEncodingModuleMatchesHostEncodersAcrossFreshSourceGrants` also
passed under both policies, comparing encoded words and measured lengths for
UTF-8, UTF-16 units and LE/BE bytes, strict ASCII and Latin-1, and UTF-32 LE/BE
bytes against host encoders. Unrepresentable ASCII and Latin-1 inputs are
rejected; this adds encoding-path evidence without changing the proposal's
unapproved status or the frontend and persisted-AOT boundaries.
This slice does not make the maintained
compiler-primitives DLL source-aware or move the scalar frontend beyond the
hosted verifier/emitter.

## Alternative

Keep persisted modules host-neutral and defer source-aware exports until the
compiler can supply source-bound code per compilation. This avoids a new
module capability but leaves `SourceLines` and other source readers outside
the persisted bootstrap library.

General host-buffer dependencies and explicit span parameters are not proposed
as substitutes: they would widen the module or reference ABI beyond this
source-specific requirement.

## Acceptance boundary

If accepted, a future implementation should demonstrate that one persisted
source-aware module can be invoked with different source snapshots without
rebuilding or retaining either snapshot. Missing or incompatible source
grants must fail validation before execution; modules without this dependency
remain host-neutral, and arbitrary host buffers remain unsupported. AOT/JIT
results should preserve the existing `SourceText` parity fixtures.
