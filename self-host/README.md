# Candidate Terrum compiler sources

These files are parallel design candidates, not a bootstrapped compiler.
They are not linked into the C# implementation. The native profile executes
the range, character, integer and nested token components and a bounded token scanner.
The host test runner remains C#, even when it compiles and executes a candidate.

Host fixtures retain syntax round trips and add native execution of
`SourceRange`, `CharacterFacts`, `RangeTests`, `Token` and `TokenTests` with entry wrappers.
The [module fixture](../docs/modules.md) also compiles the range and character
definitions into a producer, then binds the tests and a separate entry snapshot
using only that artifact's declarations. Both AOT and JIT execute the linked
result; no producer source is rebound while compiling the consumer.
The cursor and general host-interface sketches still require later language
slices. Executing these components is not a self-hosted compiler bootstrap.
`build-component` uses the compiled scanner's records as the lexical input to
the official host parser, binder and native emitter; it does not re-lex the
target input. The scanner's own compilation and the later compiler phases
remain hosted in C#.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [IntegerFacts](IntegerFacts.terrum) | Overflow-free signed-addition range predicate | Supported by the native profile; tested with native entry wrappers, not a full compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed addition and bounded decimal accumulation through the full unsigned 64-bit range | Executed as a separately compiled module with conditional wide outputs |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed extrema, invalid digits, decimal overflow and repeated output publication | Executed through AOT and JIT with a shared entry wrapper |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and conditional bool/out publication |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Executed with `char16` comparisons, checked numeric casts and verified NoThrow classification contracts |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Span indexing, matched access, stored backing dependencies, capture contract, constructor initialization |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed with a host-supplied entry wrapper; a Terrum test runner remains future work |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a validated replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Executed through fixed checked host buffers; see the [precise subset](../docs/scanner.md) |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

`SourceRange` deliberately mirrors the current host's signed 32-bit UTF-16
coordinates, including the checked end bound. This is an interchange limit for
the syntax protocol, not a decision that Terrum collection extents are signed or that
runtime text must use UTF-16. A later larger-source protocol needs an explicit
schema change. Source file UTF-8 decoding remains separate from coordinates.

`CharacterFacts` uses `(int32)value` instead of a per-source conversion hook.
Raw `char16` values are code units, not automatically validated Unicode scalars.

The candidates reuse `Span<Token>`/`TextSpan` rather than imitating C# strings,
`IEnumerator`, `ImmutableArray`, reflection, GC handles or compiler-generated
state machines. A read cursor may forward backing dependencies; retaining its
descriptor alone cannot extend an external allocation lease. `[Captures]` is
working contract notation, not an unchecked annotation accepted by the MVP.

The scanner shares the host's generated Unicode ranges; classification,
token boundaries, keywords, leading trivia and lexical diagnostic decisions execute in Terrum.
Integer magnitudes through the unsigned 64-bit range and explicit integer tags
are decoded natively without assigning a default numeric type; larger magnitudes
are reported as beyond the decoder's range, not lexical errors. Contextual
literal binding, decoded strings and parser routines remain hosted. Keep shared
fixture expectations about observable ranges and contracts, not host object
layouts. An independent compiler bootstrap requires rebuilding and comparing
outputs, not merely parsing these files.
