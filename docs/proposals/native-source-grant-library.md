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
calls and their signatures remain unchanged. No source-dependent imports,
cross-DLL propagation, aggregate results, or general wrapper are admitted.

A private test helper validates the 16,384-unit limit and null/empty shape,
pins immutable-for-the-call UTF-16 storage, invokes the export, and unpins
after synchronous return. The caller remains responsible for readable,
two-byte-aligned storage and immutability during the call. Tests do not
dereference invalid pointers. This experiment may establish implementation
feasibility only; adding source-dependent exports to the maintained library,
generalizing the ABI, or claiming language/public ABI support requires a
separate review and decision.
