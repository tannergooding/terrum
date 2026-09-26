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
Compilation-local static operator protocols now support bounded closed numeric
expressions, but not the generic spans, stored dependencies or host interfaces
required by those sketches. The scanner consumes the shared host keyword catalog,
including `abstract`, `operator` and `where`, without a second keyword table.
`build-component` uses the compiled scanner's records as the lexical input to
the official host parser, binder and native emitter; it does not re-lex the
target input. The scanner's own compilation and the later compiler phases
remain hosted in C#.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [IntegerFacts](IntegerFacts.terrum) | Overflow-free signed-addition range predicate | Supported by the native profile; tested with native entry wrappers, not a full compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed addition and bounded decimal accumulation through the full unsigned 64-bit range | Executed as a separately compiled module with conditional wide outputs |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed extrema, invalid digits, decimal overflow and repeated output publication | Executed through AOT and JIT with a shared entry wrapper |
| [IntegerFormatting](IntegerFormatting.terrum) | Invariant signed/unsigned decimal digits in reusable owned storage | AOT/JIT boundary and repeated-use fixtures; native heap checks cover same-allocation transfer and failure cleanup |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and conditional bool/out publication |
| [SourceLines](SourceLines.terrum) | Streaming line ranges and UTF-16 line/character mapping over an immutable source grant | Executed with conditional outputs; CRLF, every supported line break, EOF and malformed surrogate offsets agree with the host snapshot |
| [SourceLineIndex](SourceLineIndex.terrum) | Owned line-start table and binary-searched UTF-16 positions | Executed as AOT under both failure policies; every position matches host fixtures, with invalid outputs and owner copies rejected |
| [SourceLineTests](SourceLineTests.terrum) | Shared line-range/position fixtures, invalid offsets and repeated queries | Executed natively with host-supplied expected records; no source normalization or default output values |
| [StringLiteral](StringLiteral.terrum) | Validated quoted spelling and scalar decoding from the immutable UTF-16 source grant | Executed with conditional creation/reads, all supported escapes and exact source boundaries; not a runtime text representation |
| [StringLiteralTests](StringLiteralTests.terrum) | Quoted-form validation, scalar values and rejection of offsets inside escapes or surrogate pairs | Shared host-lexer/compiled fixtures under both native runtime policies; failed calls publish no output |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Executed with `char16` comparisons, checked numeric casts and verified NoThrow classification contracts |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Span indexing, matched access, stored backing dependencies, capture contract, constructor initialization |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed with a host-supplied entry wrapper; a Terrum test runner remains future work |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a validated replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [ManagedTokenChain](ManagedTokenChain.terrum) | Bounded linked storage for copied token categories and source offsets | AOT/JIT fixtures under both policies; 64-link capacity, count/version changes, indexed reads and collection after clear; not wired into the scanner |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Executed through fixed checked host buffers; see the [precise subset](../docs/scanner.md) |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

`SourceRange` deliberately mirrors the current host's signed 32-bit UTF-16
coordinates, including the checked end bound. This is an interchange limit for
the syntax protocol, not a decision that Terrum collection extents are signed or that
runtime text must use UTF-16. A later larger-source protocol needs an explicit
schema change. Source file UTF-8 decoding remains separate from coordinates.

`CharacterFacts` uses `(int32)value` instead of a per-source conversion hook.
Raw `char16` values are code units, not automatically validated Unicode scalars.

`SourceLines` reads the same immutable `Host.Source` capability as the scanner,
without retaining a pointer, borrowing writable storage or allocating a line table.
The host binds the original candidate snapshots through
`Compilation.Create(trees, buffers)`, preserving source-associated diagnostics
and the single-source capability restrictions without merging source text.
`TryReadSourceLine` reads the suffix starting at a valid code-unit offset; callers
enumerate physical lines by starting at zero and advancing past each returned
break. EOF is a valid empty range. Position lookup scans those lines rather than
using the host's allocated binary-search index. Break units belong to the preceding
line, and a trailing break creates a final empty line. This is an executed bounded
component, not an implemented `SourceText` collection or an efficient indexed replacement.

`SourceLineIndex` builds on that streaming reader. It owns a fixed-capacity native
table of line starts, publishes the completed index through a conditional output,
and answers position queries by binary search over a scoped readonly slice of
the initialized table. The slice borrows the owning field only for that query;
it is not returned or stored in the index. The index retains only copied offsets
and the source length; queries do not retain or reread the source grant. Construction
reserves one word per source code unit plus one for the final line, a conservative
single-pass bound rather than a resizing collection. Capacity and allocation
failure follow the bounded `OwnedInt32Buffer` contract. This is not a stored
source view, managed line collection, or general allocator API.

`IntegerFormatting` consumes and returns a unique workspace of at least twenty
`int32` words. It clears the initialized prefix without reallocating, then uses
exclusive lexical views to reverse digits or insert a minus sign. The signed
minimum is handled without negating it in the signed domain. Output words hold
explicit ASCII decimal code values; this component does not select a default
runtime text encoding or implement general format strings.

`ManagedTokenChain` stores at most 64 category/offset pairs in source-defined
managed links. Append rejects negative categories/offsets, decreasing offsets,
capacity exhaustion and version overflow before publishing a mutation.
Allocation refusal leaves the existing chain unchanged. Indexed reads return
copied scalar values; no link handle or retained source view escapes the chain.
Clear drops its private links and increments the version; explicit collection
reclaims them once the operation's temporary roots have retired.
This exercises optional managed fields and bounded mutable storage, not a
generic collection, persistent iterator, complete token stream or scanner
integration.

`StringLiteral.TryCreate` validates a complete quoted source range before
publishing its spelling and scalar count. `TryReadScalar` reports a Unicode
scalar as `int32` and the next absolute UTF-16 offset, rejecting offsets inside
an escape or surrogate pair. Arbitrary-position reads rescan the prefix.
`StringLiteralCursor` instead advances through verified boundaries in linear
total time, publishing the scalar and next immutable cursor together.
Neither form allocates an index or decoded buffer. The private construction invariant
depends on the fixed immutable `Host.Source` grant, not on a retained pointer
or a general text-view lifetime contract. The candidate does not allocate decoded
text, normalize it, select a runtime encoding, or add native string expressions.

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
literal binding and parser routines remain hosted. The string-literal candidate
decodes scalars independently; it is not wired into the scanner side tables or
the hosted semantic binder. Keep shared
fixture expectations about observable ranges and contracts, not host object
layouts. An independent compiler bootstrap requires rebuilding and comparing
outputs, not merely parsing these files.
