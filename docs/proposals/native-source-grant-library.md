# Native source-grant library proposal

**Status: unapproved proposal.**

## Summary

The invocation-scoped source-grant proposal permits bounded source-dependent
semantic-module JIT execution, but native DLL emission still rejects programs
that require `Host.Source`. The self-hosting roadmap leaves persistence of
source-aware helpers in the maintained compiler-primitives library pending.
This proposal addresses only that native-library boundary; it does not extend
the scanner profile or move scalar frontend verification or emission out of
the hosted compiler.

## Decision question

Should an internal Windows x64 native-library export be able to read a
caller-supplied `Host.Source` snapshot for the duration of one synchronous call?

**Summary:** source-aware helpers cannot be added to the maintained
compiler-primitives DLL by binding a build-time sample. A native entry point
needs an explicit per-call source ABI, with bounded length, read-only access,
and no retained address.

**Recommendation:** permit a narrowly scoped generated ABI for
source-dependent exports. Prefix only those exported signatures with a
`const char16*` source address and a `uint32` code-unit count; propagate the
same pair through their internal source-dependent call graph. Keep
source-independent exports unchanged. The caller must provide readable,
two-byte-aligned storage and keep it immutable and alive until the synchronous
call returns. This is an internal Windows x64 implementation ABI, not a
language-level pointer or span feature, public API, or stable C ABI.

## Candidate first-slice boundary

- Require only the existing reserved `Host.Source` identity with read-only
  `char16` units. Reuse the current 16,384-code-unit maximum; preserve NULs,
  line endings, and malformed surrogate units without normalization.
- Permit a null source address only when the count is zero. For a nonempty
  source, the caller is responsible for supplying an address that is aligned
  and readable for the entire extent. The generated code reads but never
  writes through it.
- Bind one source extent for the complete synchronous exported call, including
  nested local calls. The compiler must not put the address or source contents
  in the DLL, retain them after return, or use process-global or thread-local
  current-source state.
- Propagate source dependence through the local call graph. The first slice
  accepts a self-contained library only; source-dependent imports and
  cross-DLL propagation remain rejected. Calls to exports that do not require
  `Host.Source` retain their current ABI.
- Do not change the semantic-module format or the JIT adapter. Do not enable
  source-dependent executables, packages, graphs, module sets, AOT images,
  arbitrary host buffers, or scanner invocation profiles.

The unmanaged caller's extent, alignment, lifetime, and immutability
obligations are preconditions: native code cannot safely probe an arbitrary
address to establish that the full range is readable. The existing managed
source-grant adapter continues to validate and copy managed grants before
execution.

## Required validation

Before implementation, verify that native lowering can pass one explicit
source context through every source-dependent local call without changing
source-free function signatures or retaining invocation state. The first
implementation slice should compile only a self-contained library and
exercise a source-dependent export against pinned C# UTF-16 buffers.

Tests should cover empty and maximum-size inputs, fresh and repeated calls,
malformed surrogate units, NUL and line endings, and checked source boundaries.
Compare results with the hosted source facade under the applicable runtime
policies. Inspect the image and call paths to confirm that no supplied source
address or code unit is embedded or retained. Invalid external pointers cannot
be safely tested by dereferencing them; validate null/count shape and document
the caller precondition instead.

## Alternative

Keep source-dependent native exports unsupported and continue using the
semantic-module JIT adapter. This avoids adding a native call ABI and is the
current implementation boundary. The roadmap's native source-aware-helper
work remains blocked under this alternative.

## Implementation boundary

This proposal does not authorize implementation or make the candidate ABI an
accepted target contract. Exact export naming, interaction with any future
cross-DLL source dependency, and whether a higher-level managed invocation
wrapper is needed remain open. No current source-dependent DLL, compiler
bootstrap, or public native ABI is supported.

## Promotion decisions still required

### Where source-grant shape is validated

**Question:** Should each raw native export validate the source pointer/count
shape and the maximum extent, or should those be mandatory caller
preconditions?

**Summary:** The feasibility experiment's private managed helper rejects a
null pointer with a nonzero count and counts above 16,384 before invocation.
Generated code consumes the supplied count for length and bounds checks, but
does not validate the raw pointer or enforce the maximum. Native code cannot
establish that an arbitrary address is readable without dereferencing it.

**Recommendation:** Make `(count == 0 || source != null)` and
`count <= 16,384` caller preconditions on the raw internal ABI. Require any
supported managed invocation adapter to validate both before pinning and
dispatch. Keep readable extent, two-byte alignment, lifetime and immutability
as caller obligations; do not probe arbitrary pointers. The raw export must
not acquire a new failure convention solely for invalid external pointers.
The test helper's validation establishes adapter behavior only, not raw-export
validation.

### How callers learn the effective export signature

**Question:** How does a caller discover that a source-dependent export has
the hidden source pair when dependence is inferred from its transitive local
call graph?

**Summary:** Adding a source read in a private callee can change an export's
effective native signature without changing its authored parameters or export
name. A test-only follow-up now derives selected-export descriptors from the
same emission selection and generates matching cdecl delegate types, including
the hidden pair only for source-dependent exports. Recompiling an unchanged
export after changing only its private callee verifies both signature shapes.
This is feasibility evidence, not a production managed caller-binding
mechanism.

**Recommendation:** For any promoted managed path, generate its invocation
stub from the same verified export set used for DLL emission, including the
hidden source pair only for source-dependent exports. Do not require callers
to infer the ABI from source bodies or hand-maintain delegate signatures.
Direct unmanaged calls remain an internal rebuild-coupled ABI obligation, not
a stable C ABI; no new semantic-module metadata is needed solely to generate
the stub.

## Provisional test-only experiment

The continuing self-hosting task directs a bounded feasibility experiment;
this does not approve the proposal or change the implementation boundary
above. Keep the maintained compiler-primitives DLL and all production
source-dependent native exports unsupported unless a later decision promotes
the experiment.

The experiment uses a separate test DLL containing `Utf16CodeUnits`,
`Utf16Scalar`, and a test-authored export named
`Terrum.CodeAnalysis.ReadUtf16ScalarForSourceAbiTest`. Its source-dependent
signature prefixes the source address and `uint32` code-unit count, in that
order, before the authored scalar parameters. The test adapter takes `at`,
`end`, and an expected scalar so the ABI exercises a fifth argument on
Windows x64 while returning the decoder result only when it matches. Internal
source-dependent calls receive the same hidden source pair; source-independent
calls and their signatures remain unchanged. Separate signed and unsigned
64-bit exports read a source code unit through local callees, and a source-free
64-bit export checks byte-identical emission against ordinary native code.
No source-dependent imports, cross-DLL propagation, aggregate results, or
general wrapper are admitted.

The test-only generated binding validates the 16,384-unit limit, pins the
provided UTF-16 array for the synchronous call, adds the hidden pair only when
the selected export descriptor requires it, and unpins after return. The
caller remains responsible for readable storage and immutability during the
call; pinned arrays provide the required two-byte alignment. Lower-level
adapter tests still check null/count and alignment rejection before dispatch.
Tests do not dereference invalid pointers. This experiment may establish
implementation feasibility only; adding source-dependent exports to the
maintained library, generalizing the ABI, or claiming language/public ABI
support requires a separate review and decision.

### Experiment outcome

The test-only path is implemented as internal
`NativeEmitter.EmitSourceGrantLibraryForTest`; the public `EmitLibrary` path
continues to reject source grants. Tests compile the two UTF-16 components
and test adapters through hosted binding, then mark that test IR as a library
without adding a library-buffer compilation API or changing semantic metadata.
Admission is deliberately narrower than the candidate contract: only
self-contained structured `bool`, `char16`, `int32`, `uint32`, `int64`,
`uint64`, and `void` functions are accepted. Blocks, initialized locals,
assignments, expression statements, conditionals, requirements, returns,
scalar expressions, and local calls are explicitly visited. It also admits
pre-tested structured loops whose `BoundWhile` has no separate iterator;
post-tested loops, loops with a separate iterator, and `break`/`continue`
remain rejected. Source forms lowered to this same bound shape are
necessarily admitted as well.
Cleanup regions, indexed graphs, aggregates, output loans, intrinsic
functions, and external declarations are not admitted.

Direct source length/read expressions and the transitive local call graph
determine which functions receive the hidden pair. The pair is saved in
frame-local slots before volatile-register use. Authored argument positions,
outgoing stack space, and local calls account for those two positions only
on source-dependent functions. Length reads load the invocation count;
buffer reads compare their index against that count and use zero-extending
two-byte loads from the invocation pointer. No source buffer data or address
fixups are emitted.

Focused Windows x64 tests load one separate temporary DLL per runtime policy
and reuse it for fresh pinned UTF-16 storage. The scalar adapter and its
three-argument local callee exercise the fifth argument, including reordered
named arguments. Empty/null-zero calls use the length export and scalar
adapter guard. Valid decoder results match hosted C# `Rune.DecodeFromUtf16`
for maximum-size input, BMP/supplementary scalars, NUL, CR/LF and other line
breaks, malformed surrogates, final units, and bounded lookahead.
The signed and unsigned code-unit exports are also compared against every BMP
code unit across four fresh maximum-size pinned grants under both policies;
the scalar adapter is checked at the same one-unit ends and at every
supplementary high-surrogate boundary, so no following code unit can complete
a surrogate pair outside the requested extent. The full native scalar adapter
also decodes all 1,048,576 valid supplementary scalars across fresh
maximum-size grants under both policies. This is direct-read and
bounded-decoding evidence only, not support for the `SourceText` facade or
output loans.
One pinned allocation is reused with different counts, and two
simultaneously pinned allocations with equal counts produce distinct results.
The test adapter returns `-2` for invalid ranges without calling the decoder,
and `-3` for an expected-value mismatch; neither changes the real decoder's
precondition or `-1` malformed-surrogate result. Managed test helpers reject
null/nonzero, oversized, misaligned, and storage/count-inconsistent calls
before native invocation; tests never dereference invalid pointers. A separate
boundary export indexes a still-readable one-unit allocation with a zero-unit
grant, and the checked-exceptions DLL must raise `SEHException` before the
read. Both runtime-policy images assert the dynamic-count comparison,
conditional branch, and failure call; the fail-fast failure path is not invoked
because it terminates the process.

Artifact assertions establish empty host data, buffer offsets, and data
fixups; absent image data sections, TLS, and base relocations; identical DLL
bytes after changing the sample source units and length; frame-local context
stores before body code; and dynamic-count comparisons followed by
`movzx eax,word ptr [rdx+rax*2]` in the emitted PE text. A five-authored-argument
source-free export remains callable without the hidden pair and its function
bytes match ordinary source-free emission exactly.

The test-only managed binder consumes the selected-export descriptors emitted
alongside the image. A fixture keeps the public `int32(int32)` export
unchanged while switching only its private callee from source-free to a
`Host.Source` read; the generated delegate changes from one authored argument
to the hidden source pair plus that argument, and execution matches the
expected source unit under both runtime policies. Oversized managed grants are
rejected before invocation, source-free calls retain their authored signature,
and production `EmitLibrary` continues to reject both fixtures. This does not
generate production stubs or change the maintained compiler-primitives DLL
boundary.

The test-only fixture also verifies source-dependence fixed-point propagation
through a terminating two-function mutual-recursion cycle under both runtime
policies, with the native result matching the hosted source-grant JIT result.
This adds feasibility evidence only; native source-grant DLL support remains
unapproved and unsupported.

The reject-by-default experiment admits only self-contained structured
`bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`, and `void` functions.
These results establish feasibility of the provisional generated ABI only.
They do not establish native support for the existing four-operation
`SourceText`/`SourceLines` facade, whose current components require output
parameters and structures outside the experiment. The scalar line-scan
fixture does not change that boundary; adopting the source facade would
require separate implementation and validation evidence.

### Test-only increment: structured line scans

**Question:** Should the feasibility classifier admit structured pre-tested
loops without a separate iterator for scalar-returning source-dependent
helpers, without admitting the output parameters or aggregate results needed
by the full source facade?

**Summary:** The test-only classifier now admits `BoundWhile` only when
`Iterator` is null and `IsPostTested` is false, recursively visiting its
condition and body for source-dependence. The line-count fixture starts at one
(including empty input), treats CRLF as one break, and counts CR, LF, NEL, LS
and PS as line breaks. Under both runtime policies, generated-binding results
match `SourceText.From(text).Lines.Length` across empty/null grants, each
supported line ending, mixed and trailing endings, malformed UTF-16, NUL,
excluded VT/FF, and maximum-length inputs. Explicit maximum-bound checks cover
16,384 plain units, 16,384 LF units, 8,192 CRLF pairs and a final CR; the
16,385-unit grant remains rejected before dispatch. The line-count export
also matches hosted `SourceText` for each of the 65,536 possible one-unit
UTF-16 sources under both runtime policies, including every surrogate and
control code unit. A second exhaustive pass appends each possible UTF-16 unit
after CR, checking CRLF pairing and adjacent breaks against the hosted line
count under both policies. For every single-unit source, native line/character
positions at both offsets also match `SourceText.GetLinePosition`, and each
valid result round-trips through the native inverse query and hosted
`SourceText.TryGetOffset`.
The same test DLL also returns a test-local packed line/character pair and
matches hosted `SourceText.GetLinePosition` for every offset in the short
edge-case fixtures and selected boundary offsets in maximum-length inputs.
Both coordinates fit unsigned 16-bit fields under the 16,384-unit source
bound. The packing is only a scalar test carrier; it does not define a
production return ABI or remove the full facade's output-parameter and
aggregate requirements. For those same valid positions, a scalar inverse
query maps line/character back to the original offset and agrees with hosted
`SourceText.TryGetOffset`. Invalid line/character pairs and any production
failure representation remain outside the experiment.

**Recommendation:** Keep this admission confined to the test-only
feasibility path. Continue rejecting post-tested loops, separate iterators,
loop transfers, output loans, aggregate types, the four-operation source
facade, maintained DLL exports and production caller binding. These parity
tests add implementation evidence only and do not approve the proposal.
Public library emission, native packages, production CLI paths, maintained
compiler-primitives exports, JIT execution, scanner profiles, executables,
graphs, module sets, AOT, and cross-DLL source propagation remain unchanged
and unsupported for this experiment. Readable aligned memory, lifetime, and
immutability remain caller preconditions; there is no general native pointer
validation or public wrapper. **Status remains unapproved.**
