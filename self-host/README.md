# Candidate Terrum compiler sources

These files are parallel design candidates, not a bootstrapped compiler.
They are not linked into the C# implementation and no Terrum execution is claimed.
The host tests remain executable C# tests; the candidate tests below have the same
eventual self-hosting premise as the compiler sources.

Host fixture tests parse and round-trip `SourceRange`, `CharacterFacts` and
`RangeTests` only. They do not execute their functions, prove their semantics,
or cover the unsupported cursor/host-interface syntax.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Binding, private construction, checked integers, conditional bool/out publication, CLI IL emission |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Static calls, `char16` comparisons, typed literals |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Span indexing, matched access, stored backing dependencies, capture contract, constructor initialization |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | The above plus a test runner/assertion library and short-circuit initialization analysis |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

`SourceRange` deliberately mirrors the current host's signed 32-bit UTF-16
coordinates, including the checked end bound. This is an interchange limit for
syntax-0, not a decision that Terrum collection extents are signed or that
runtime text must use UTF-16. A later larger-source protocol needs an explicit
schema change. Source file UTF-8 decoding remains separate from coordinates.

The candidates reuse `Span<Token>`/`TextSpan` rather than imitating C# strings,
`IEnumerator`, `ImmutableArray`, reflection, GC handles or compiler-generated
state machines. A read cursor may forward backing dependencies; retaining its
descriptor alone cannot extend an external allocation lease. `[Captures]` is
working contract notation, not an unchecked annotation accepted by the MVP.

To progress: execute the range/character tests through the future binder and CLI
backend; port token records and the pinned Unicode tables; add parser routines;
then compare host/self-host token, tree and diagnostic fixtures. Keep shared
fixture expectations about observable ranges and contracts, not host object
layouts. An independent compiler bootstrap requires rebuilding and comparing
outputs, not merely parsing these files.
