# Candidate Terrum compiler sources

These files are parallel design candidates, not a bootstrapped compiler.
They are not linked into the C# implementation. The native profile executes
the range, character, integer and nested token components and a bounded token scanner.
The host test runner remains C#, even when it compiles and executes a candidate.
The host parser's current language version is syntax-27. Syntax-18's qualified
generic-call type-argument lookahead enables the bounded managed-handle
specializations described below. These reuse existing, feature-selected
semantic contracts without changing the scanner record/schema, native profile
or wire format. Syntax-19 adds the bounded source-defined nominal comparison
protocols without changing those formats or the native profile.
Syntax-20 reserves `_` as a discard marker; scanner-5 publishes that
classification while keeping the existing integer-record layout.
Syntax-21 adds compilation-local unary `+`, `-`, `!` and `~` protocols;
it adds no scanner record/schema or semantic/native wire change.
Syntax-22 adds bounded conditional `init ref<T>` parameters and
`init name` calls. Semantic v33 records their mode without changing
scanner records or native output ABI. Bounded public `init ref<T>` contracts
now distinguish `init:` from `out:` across module consumers; general
references and generic init parameters remain unsupported.
Syntax-23 adds explicit getter-only properties on Copy structs and source
classes; semantic v34 records property identity without a scanner or native
ABI change.
Syntax-24 supports one-field transparent scalar wrappers with semantic v35
layout identity. Syntax-25 supports bounded internal scalar `[ConstEval]`
functions and required `const T name = expression;` locals without adding
public constant-evaluation metadata or a native ABI.
Syntax-26 adds scalar `const expression` requirements in locals, return values,
call arguments and conditions using the same bounded evaluator. The compiler
replaces each requirement at its original expression position; runtime calls
outside the required expression remain executable. No public module contract
or native ABI changes.
Syntax-27 accepts bounded constant-only body `static require` and
outer-body `static ensure` obligations; proven conditions disappear before
native emission and do not publish module contracts. Symbolic proofs over
runtime values remain scheduled.

Host fixtures retain syntax round trips and add native execution of
`SourceRange`, `CharacterFacts`, `RangeTests`, `Token` and `TokenTests` with entry wrappers.
The [module fixture](../docs/modules.md) also compiles the range and character
definitions into a producer, then binds the tests and a separate entry snapshot
using only that artifact's declarations. Both AOT and JIT execute the linked
result; no producer source is rebound while compiling the consumer.
The cursor and general host-interface sketches still require later language
slices. Executing these components is not a self-hosted compiler bootstrap.
Compilation-local constrained functions support bounded closed helper composition,
Copy-only selection and numeric algorithms with scalar control flow, but not the
generic spans, stored dependencies or host interfaces
required by those sketches. The scanner consumes the shared host keyword catalog,
including `abstract`, `operator` and `where`, without a second keyword table.
Eligible top-level templates support multiple independently constrained type
parameters over `Copy` structs, scalar value types, and existing
source-class/`ManagedNode` handles with their readonly/write and supported
optional forms. Typed handle substitutions retain their supplied
qualification; `Copy` does not grant member, null-test or operator
capabilities. These specializations remain compilation-local. Unique/owning
arguments, views, generic classes/structs, managed fields inside user structs,
partial holes, constructor/method inference, ordinary overloads and public
generic ABI remain unsupported. Concrete source-class `TryCreate` factories,
fields and methods may be used inside a template without granting those
operations to symbolic type parameters; existing handle qualifications,
conditional outputs and rooting still apply. See the detailed
[generic implementation boundary](../docs/implementation.md#bounded-generic-functions).
Scalars support Copy-only helpers, not primitive operator witnesses.
Explicit homogeneous Copy-struct witnesses support `+`, `-`, `*`, `/`, `%`,
`&`, `|` and `^`; generic code still requires the corresponding declared
nominal constraints. Syntax-19 also implements six separately constrained
nominal comparison protocols over homogeneous Copy structs with `bool`
results; see the [bounded generic contract](../docs/implementation.md#bounded-generic-functions).
No comparison law or universal equality/hash behavior is inferred. Unary
protocols have independent `Copy` results, including non-`bool` `!`;
heterogeneous binary, shift, `++`/`--` and `true`/`false` protocols remain
unsupported.
`build-component` uses the compiled scanner's records as the lexical input to
the official host parser, binder and native emitter; the binder also consumes
its decoded integer magnitudes. It does not re-lex or decode integer magnitudes
from the target input again. The scanner's own compilation and the later compiler phases
remain hosted in C#.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [CopyAlgorithms](CopyAlgorithms.terrum) | NoThrow selection and conditional output of complete Copy values without operator witnesses | Composed AOT/JIT calls select scalars, nested tokens and ranges under both policies; invalid indices publish no value; no primitive operator conformance, borrowed views or generic runtime dispatch |
| [IntegerFacts](IntegerFacts.terrum) | Overflow-free signed-addition range predicate | Its internal `[ConstEval]` helper is required for a scalar local and remains callable at runtime; tested with native entry wrappers, not a full compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed addition and bounded decimal accumulation through the full unsigned 64-bit range | Executed as a separately compiled module with conditional wide outputs |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed extrema, invalid digits, decimal overflow and repeated output publication | Executed through AOT and JIT with a shared entry wrapper |
| [IntegerFormatting](IntegerFormatting.terrum) | Invariant signed/unsigned decimal and explicit radix-2..36 digits in reusable owned storage | AOT/JIT boundary, exact-capacity and repeated-use fixtures; native heap checks cover same-allocation transfer and failure cleanup |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and conditional bool/out publication |
| [SourceLines](SourceLines.terrum) | Streaming line ranges, line extents and bidirectional UTF-16 offset/line-position mapping over an immutable source grant | AOT/JIT conditional outputs under both policies; CRLF, every supported line break, EOF and malformed surrogate offsets agree with the host snapshot |
| [SourceLineIndex](SourceLineIndex.terrum) | Owned line-start table, random-access line extents and bidirectional UTF-16 offset/line-position lookup | Executed as AOT/JIT under both failure policies; every offset round-trips with host line positions, with invalid outputs and owner copies rejected |
| [SourceLineTests](SourceLineTests.terrum) | Shared line-range/position fixtures, invalid offsets and repeated queries | Executed natively with host-supplied expected records; no source normalization or default output values |
| [StringLiteral](StringLiteral.terrum) | Validated quoted spelling and scalar decoding from the immutable UTF-16 source grant | Executed with conditional creation/reads, all supported escapes and exact source boundaries; not a runtime text representation |
| [StringLiteralTests](StringLiteralTests.terrum) | Quoted-form validation, scalar values and rejection of offsets inside escapes or surrogate pairs | Shared host-lexer/compiled fixtures under both native runtime policies; failed calls publish no output |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Executed with `char16` comparisons, checked numeric type-call conversions and verified NoThrow classification contracts |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Span indexing, matched access, stored backing dependencies, capture contract, constructor initialization |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed with a host-supplied entry wrapper; a Terrum test runner remains future work |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a validated replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [ManagedTokenChain](ManagedTokenChain.terrum) | Bounded linked storage for complete Copy tokens and validated source ranges, with a sequential managed cursor | AOT/JIT fixtures under both policies; 64-link capacity, count/version changes, copied indexed and sequential results, mutation invalidation and collection after clear |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Executed through fixed checked host buffers; see the [precise subset](../docs/scanner.md) |
| [ManagedScanner](ManagedScanner.terrum) | Copy a completed compiled scanner token stream into newly published managed storage or a sequential cursor | AOT/JIT parity with host token kinds/ranges under both policies; lexical failure, malformed host records and more than 64 tokens leave the output vacant; independent copies survive clear and collection |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

`SourceRange` deliberately mirrors the current host's signed 32-bit UTF-16
coordinates, including the checked end bound. This is an interchange limit for
the syntax protocol, not a decision that Terrum collection extents are signed or that
runtime text must use UTF-16. A later larger-source protocol needs an explicit
schema change. Source file UTF-8 decoding remains separate from coordinates.
The range's explicit `End` getter is exported alongside the existing `GetEnd`
method for independent module consumers; both compute the checked end.

`CharacterFacts` uses `int32(value)` instead of a per-source conversion hook.
Raw `char16` values are code units, not automatically validated Unicode scalars.

`SelectCopy<T>` selects one of two already-evaluated by-value arguments. Both
arguments are evaluated under ordinary call rules; this is not a lazy conditional
expression. Its `[Copy]` constraint is enough for whole-value returns without
operator conformance, construction, field inspection or a default value.
The caller can compose it through another Copy-constrained helper while retaining
the declared NoThrow contract. Token/range fixtures verify independent nested
copies after the original local values are replaced. The function remains a
compilation-local template, not a separately published generic library contract.
`TrySelectCopy<T>` accepts index zero or one and publishes the corresponding
value through the existing conditional `bool`/`out` contract. Other indices
return false without publishing an output; inputs are still ordinary eagerly
evaluated values. Generic helper forwarding preserves that contract rather
than manufacturing a default token or range.

`SourceLines` reads the same immutable `Host.Source` capability as the scanner,
without retaining a pointer, borrowing writable storage or allocating a line table.
The host binds the original candidate snapshots through
`Compilation.Create(trees, buffers)`, preserving source-associated diagnostics
and the single-source capability restrictions without merging source text.
`TryReadSourceLine` reads the suffix starting at a valid code-unit offset; callers
enumerate physical lines by starting at zero and advancing past each returned
break. EOF is a valid empty range. Position lookup scans those lines rather than
using the host's allocated binary-search index. Break units belong to the preceding
line, and a trailing break creates a final empty line. Inverse position lookup
also scans without allocation; only the final line admits an EOF character,
and coordinates at a subsequent line's start must use that line number. The
component also locates a line's full extent, including its break, by ordinal
without retaining a table. An invalid ordinal publishes no extent. This is
an executed bounded component, not an implemented `SourceText` collection
or an efficient indexed replacement.

`SourceLineIndex` builds on that streaming reader. It owns a fixed-capacity native
table of line starts, publishes the completed index through a conditional output,
and answers position queries by binary search over a scoped readonly slice of
the initialized table. The slice borrows the owning field only for that query;
it is not returned or stored in the index. The index retains only copied offsets
and the source length; queries do not retain or reread the source grant. It
also supports the inverse line/character-to-offset lookup from the same table.
It returns a line's full extent, including its break, using adjacent starts
without rereading the source; the final line ends at the stored source length.
Line positions include break units on their preceding line; a character at the
next line's start must use the next line number, and only the final line admits
the EOF offset. Invalid line/character pairs return false without publishing
an offset, including values that would overflow a signed offset if added first.
Construction counts lines before reserving exactly one word per line start,
including the final empty line after a trailing break. The fill pass reuses the known first
and final starts, so one- and two-line sources need no second scan. If every
code unit ends a line, starts are filled directly from their ordinal positions.
Other multiline sources trade a partial second scan for smaller storage; this
is not a resizing collection. Capacity and allocation failure follow the bounded
`OwnedInt32Buffer` contract: the capacity limit applies to line count rather
than source length plus one. This is not a stored
source view, managed line collection, or general allocator API.

`IntegerFormatting` consumes and returns a unique `int32`-word workspace.
The decimal entry points require at least twenty words. Each formatter clears
the initialized prefix without reallocating, then uses
exclusive lexical views to reverse digits or insert a minus sign. The signed
minimum is handled without negating it in the signed domain. Output words hold
explicit ASCII decimal code values; this component does not select a default
runtime text encoding or implement general format strings.
`FormatUInt64Base` and `FormatInt64Base` accept an explicit radix from 2 through
36 and publish lowercase ASCII digits (`0`-`9`, `a`-`z`), with a minus sign
for negative values rather than a two's-complement rendering. Their workspace
must hold the actual output (up to 64 unsigned or 65 signed words in base 2);
an invalid radix or insufficient capacity raises the existing checked
requirement failure without publishing a partial result. The decimal entry
points retain their 20-word minimum even for short numbers, so callers can
reuse a workspace for any decimal value. This is a bounded library operation,
not a language format string or an operation-specific formatting witness.

`ManagedTokenChain` stores at most 64 complete `Token` values in source-defined
managed links. It compiles with `Token.terrum` and `SourceRange.terrum`; each
12-byte token contains a category and a validated start/length range.
Its readonly `Count` and `Version` getters expose the same values as the
existing methods without exposing its private mutable links.
Append rejects negative categories, invalid/overflowing ranges, decreasing
starts, capacity exhaustion and version overflow before publishing a mutation.
Allocation refusal leaves the existing chain unchanged. Indexed reads return
independent Copy tokens that remain valid after the links are collected;
no link handle or retained source view escapes the chain.
`TryCreateCursor` allocates a sequential reader over the existing links rather
than rescanning from the head for each token. The reader retains its chain and
checks the chain version before every read. An append or clear invalidates old
readers: `TryRead` then returns false without publishing an output, while
`IsCurrent` distinguishes invalidation from ordinary end of stream. The reader
releases its remaining link on a rejected read; it does not preserve a snapshot
across mutation or expose a link handle.
The link factory receives a complete `Token`, preserving the already checked
`SourceRange` instead of decomposing and reconstructing it during allocation.
Its constructor still checks the nonnegative token category before publication.
Clear drops its private links and increments the version; explicit collection
reclaims them once the operation's temporary roots have retired.
This exercises compact inline Copy fields alongside optional managed edges, not a
generic collection, persistent iterator or complete token stream.
`ManagedScanner.TryScanManaged` first fills the unchanged fixed host scanner
records, then validates and copies up to 64 tokens (including EOF) into a new
chain through a compilation-private `init ref<write ManagedTokenChain>` loan.
Lexical errors, invalid records, capacity or allocation refusal leave the
output vacant; a partially filled local chain is never published. The
wrapper does not replace the scanner records, trivia, diagnostic or integer
tables used by `build-component`, nor does it introduce a parser bootstrap.
`TryScanManagedCursor` publishes a reader only after the complete scan and
cursor allocation succeed; its cursor retains the underlying chain after
the scanner wrapper returns. It is a bounded sequential consumer, not a
replacement for the host parser or a public token-stream wire contract.

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
token boundaries, keywords, leading trivia and lexical diagnostic decisions
execute in Terrum. Untagged decimal, hexadecimal (`0x`/`0X`) and binary
(`0b`/`0B`) integer magnitudes, including digit separators, through the
unsigned 64-bit range are decoded natively without assigning a default numeric
type. Empty hexadecimal and binary prefixes
report `TRM0007` and `TRM0008`, respectively. Hexadecimal and binary literals
construct bits in their destination type; nonzero bits beyond its width are
diagnosed, and decimal signed-range and negative-minimum rules are unchanged.
Attached width suffixes are lexical errors (`TRM0006`); scanner-5 retains
recognized suffix-tag metadata only in the existing record for those rejected
tokens, while valid integer records are untagged. Larger integer magnitudes
are reported as beyond the decoder's range, not lexical errors. Floating
literal binding and custom literal parsers remain unsupported; bounded
scalar `[ConstEval]` is implemented separately from literal registration.
Contextual integer literal typing/range checks
and parser routines remain hosted, while
`build-component` binds the scanner's validated integer records without a host
magnitude-decoding fallback. The string-literal candidate
decodes scalars independently; it is not wired into the scanner side tables or
the hosted semantic binder. Keep shared
fixture expectations about observable ranges and contracts, not host object
layouts. An independent compiler bootstrap requires rebuilding and comparing
outputs, not merely parsing these files.
