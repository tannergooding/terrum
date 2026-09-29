# Candidate Terrum compiler sources

These files are parallel design candidates, not a bootstrapped compiler.
They are not linked into the C# implementation. The native profile executes
the range, character, integer and nested token components and a bounded token scanner.
The full-solution build also produces
`artifacts\bin\self-host\<configuration>\terrum-compiler-primitives.dll`
from `CharacterFacts`, `SourceRange` and `Token`. This maintained native
compiler-primitives library exports `Terrum.CodeAnalysis.IsAsciiDigit`,
`Terrum.CodeAnalysis.IsLineBreak` and `Terrum.CodeAnalysis.GetDecimalDigit`;
it is not a self-host compiler or an executable scanner/parser.
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
references remain unsupported. Compilation-local `Copy` generic templates
can conditionally initialize `init ref<T>` without a public generic ABI.
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
Compilation-local Copy helpers may publish `bool`/`init ref<T>` outputs after
definition-site verification, without introducing a public generic ABI.
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
`frontend-component self-host <input|-> --runtime <fail-fast|checked-exceptions> --json`
executes the bounded native scalar frontend with explicit source buffers and
projects its typed result for inspection. Invalid shapes produce a code and
source span; lexical failures retain scanner diagnostics and publish no typed
result. This command does not use the typed result to bind or emit a program;
it needs supervision like the other in-process component commands.
`frontend-build-library self-host <input|-> -o <output.dll> --runtime <fail-fast|checked-exceptions> --json`
instead lowers the same validated
native facts into verified IR and emits a Windows x64 DLL for an explicitly
`public` scalar function. The output is replaced only on success.
`frontend-build self-host <input|-> -o <output.exe> --runtime <fail-fast|checked-exceptions> --json`
command emits a Windows x64 executable from native facts for a zero-parameter
`int32 Main()` through the hosted emitter, retaining the output on failure.
`frontend-run self-host <input|-> --runtime <fail-fast|checked-exceptions> --json`
JIT-executes the same bounded entry without writing an image. Like hosted
`jit`, it returns the raw `Main` exit status and only uses JSON for diagnostics;
supervise this in-process native command with a deadline.
The bounded input and source directory remain required, and general source
binding, broader libraries and compiler self-build remain outstanding.
One hundred and forty-two shared scalar/conditional/Boolean/integer-literal fixtures have byte-identical DLL output from
the hosted and Terrum-authored frontend paths under both runtime policies;
both paths still use the hosted native emitter.
Twenty-six bounded `int32 Main()` fixtures likewise emit byte-identical Windows
executables and run under both policies; this does not establish an independent
native emitter or compiler self-build.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [CopyAlgorithms](CopyAlgorithms.terrum) | NoThrow selection and conditional `out` or `init ref<T>` output of complete Copy values without operator witnesses | Composed AOT/JIT calls select scalars, nested tokens and ranges under both policies; invalid indices publish no value; existing `out` calls remain valid; no primitive operator conformance, borrowed views or generic runtime dispatch |
| [IntegerFacts](IntegerFacts.terrum) | Overflow-free signed-addition range predicate | Its internal `[ConstEval]` helper is required for a scalar local and remains callable at runtime; tested with native entry wrappers, not a full compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed and unsigned 64-bit addition, subtraction, multiplication, division and remainder, signed negation and absolute value with conditional `init ref` publication, total unsigned signed-magnitude extraction, plus bounded decimal/radix-2..36 accumulation through the full unsigned range | Executed as separately compiled modules with conditional wide outputs and both `out` and vacancy-only `init ref` accumulation under both policies; division by zero, signed-minimum quotient overflow, negation and absolute value do not publish; magnitude includes the signed minimum |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed arithmetic extrema, invalid digits/radices, per-radix unsigned/signed limits and conditional output publication | Executed through AOT and JIT with a shared entry wrapper under both failure policies |
| [IntegerCheckedArithmetic](IntegerCheckedArithmetic.terrum) | Checked `int32`/`uint32` addition, subtraction, multiplication, division and remainder, plus signed negation and absolute value with conditional `init ref<T>` publication and total unsigned signed-magnitude extraction | Independent semantic-module producer/consumer with AOT/JIT and both failure policies; zero divisors, signed-minimum quotient overflow, negation and absolute value do not publish, while signed-minimum remainder by `-1` is zero and magnitude includes it; these are named library functions, not new operator syntax |
| [IntegerFormatting](IntegerFormatting.terrum) | Invariant signed/unsigned 32-/64-bit and `char16` code-unit decimal and explicit radix-2..36 digits (lowercase or uppercase) in reusable owned storage, with vacancy-only radix length queries for exact allocation | AOT/JIT per-radix length, exact-capacity and repeated-use fixtures; native heap checks cover same-allocation transfer and failure cleanup |
| [IntegerParsing](IntegerParsing.terrum) | Strict signed/unsigned radix-2..36 ASCII parsing of an exact UTF-16 source slice with conditional 32-/64-bit and unsigned `char16` code-unit `out` or `init ref` outputs | AOT/JIT under both policies; full-range per-radix boundaries, invalid spelling/bounds and unpublished outputs; existing `out` entry points remain unchanged |
| [IntegerCheckedConversions](IntegerCheckedConversions.terrum) | All twelve cross-type 32-/64-bit integer conversions and four checked narrowings to `char16`, preserving the value or leaving an `init ref` result vacant on out-of-range input | Independent semantic-module producer/consumer with AOT/JIT under both policies; `char16` preserves any in-range code unit, including surrogates, without asserting Unicode scalar validity; provisional named alternatives to failing checked type-calls, not new syntax or conversion witnesses |
| [IntegerSaturation](IntegerSaturation.terrum) | All twelve cross-type 32-/64-bit integer conversions and four `char16` narrowings preserving or clamping to the destination range | AOT/JIT range boundaries and an independent compiled-module consumer under both policies; these named functions are provisional, not general conversion syntax or witnesses |
| [IntegerWrapping](IntegerWrapping.terrum) | All twelve cross-type signed/unsigned 32- and 64-bit conversions plus four `char16` narrowings preserving destination-width low bits | AOT/JIT host-oracle cases and an independent compiled-module consumer under both policies; no `unchecked` syntax or new primitive conversion policy |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and conditional `out` or vacancy-only `init ref` publication through independent module consumers |
| [SourceLines](SourceLines.terrum) | Streaming line ranges, line extents and bidirectional UTF-16 offset/line-position mapping over an immutable source grant | AOT/JIT `out` and vacancy-only `init ref` outputs under both policies; CRLF, every supported line break, EOF and malformed surrogate offsets agree with the host snapshot |
| [SourceLineIndex](SourceLineIndex.terrum) | Owned line-start table, random-access line content and including-break extents, and bidirectional UTF-16 offset/line-position lookup | Executed as AOT/JIT under both failure policies; existing `out` APIs and vacancy-only `init ref` construction/lookups, exact offset round-trips, invalid output and owner-copy rejection |
| [SourceLineTests](SourceLineTests.terrum) | Shared line-range/position fixtures, invalid offsets and repeated queries | Executed natively with host-supplied expected records; no source normalization or default output values |
| [StringLiteral](StringLiteral.terrum) | Validated quoted spelling and scalar decoding from the immutable UTF-16 source grant, with explicit ASCII, UTF-8, UTF-16 unit/UTF-16LE byte and UTF-32LE byte length measurement and encoding into owned storage | Executed with conditional `out` and vacancy-only `init ref` creation/reads, all supported escapes and exact source boundaries; ASCII rejects non-ASCII scalars while UTF bytes/units agree with host encoders; no default runtime representation or selected TRRM row grammar |
| [StringRow](StringRow.terrum) | Provisional tagged ASCII/UTF-8/UTF-16LE/UTF-32LE String-row measurement and emission from validated literal scalars into reusable owned byte words, with nonempty metadata-name and empty-permitted namespace paths that both reject embedded zero scalars, plus ordinal decoded-scalar name and namespace comparison | AOT/JIT fixtures check exact unit counts, encoding-width terminators, four-byte alignment, zero padding, invalid flags, rejected name/namespace text, escape-equivalent spelling, byte parity with the host String-row writer and checked undersized cleanup; not a TRRM section writer, reader, or literal-reference contract |
| [StringLiteralTests](StringLiteralTests.terrum) | Quoted-form validation, scalar values and rejection of offsets inside escapes or surrogate pairs | Shared host-lexer/compiled fixtures under both native runtime policies; failed calls publish no output |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Executed with `char16` comparisons, checked numeric type-call conversions and verified NoThrow classification contracts |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Span indexing, matched access, stored backing dependencies, capture contract, constructor initialization |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed with a host-supplied entry wrapper; a Terrum test runner remains future work |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a vacancy-only initialized replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [ManagedTokenChain](ManagedTokenChain.terrum) | Bounded linked storage for complete Copy tokens and vacancy-only validated source ranges, with a sequential managed cursor, non-consuming indexed lookahead and conditional kind consumption | AOT/JIT fixtures under both policies; 64-link capacity, count/version changes, `out` and vacancy-only `init ref` cursor creation, lookahead and indexed/sequential Copy results, mismatch does not advance, mutation invalidation and collection after clear |
| [TokenSnapshot](TokenSnapshot.terrum) | Owned, indexed copy of a completed compiled scanner stream using three words per token, without the managed chain's 64-link limit | AOT/JIT indexed-read parity beyond 64 tokens, exact EOF and checked invalid indices; buffer capacity and allocation still bound the snapshot, trivia requires the separate snapshot, and diagnostics and general parser integration remain future work |
| [TokenTriviaSnapshot](TokenTriviaSnapshot.terrum) | Separately owned scanner-5 leading-trivia records with kind, range and owning token index, atomically published with `TokenSnapshot` by `TryScanLossless` | AOT/JIT host-lexer parity, contiguous source coverage, invalid record/owner rejection and input mutation independence under both policies; no diagnostic recovery, integer-value ownership or general CST parser |
| [TokenIntegerSnapshot](TokenIntegerSnapshot.terrum) | Separately owned scanner-5 numeric-token indices and raw unsigned magnitude words, published with tokens and trivia by `TryScanCompilerInput` | AOT/JIT full-`uint64`, radix and overflow fixtures, indexed token lookup, invalid index/status/tag/owner rejection and atomic failure; shape checks do not prove a producer's arithmetic, and general binding remains hosted |
| [TokenDiagnosticSnapshot](TokenDiagnosticSnapshot.terrum) | Independently owned lexical diagnostic codes and source ranges, available after a scan with errors even when token snapshots cannot publish | AOT/JIT host diagnostic parity under both policies, invalid buffer rejection, stable reads after host mutation and no success-shaped empty token stream on lexical failure |
| [ScalarParserTypes](ScalarParserTypes.terrum) | Shared scalar type recognition, token-name equality, scalar comparison and scanner-integer decoding for separately compiled bounded parser probes | Scanner-owned source ranges, UTF-16 spelling and integer records, with no independent syntax or semantic ABI |
| [ScalarFrontend](ScalarFrontend.terrum) | Reusable bounded frontend phase over a successful scanner snapshot: one scalar function with zero to two named parameters, parameter references, Boolean literals or unary `!` over a Boolean literal/parameter (including grouped operands), `~` over a contextual integer literal/parameter (including grouped operands), `+` over a contextual integer literal/parameter or named parameter (including grouped operands), or signed `-` over a named integer parameter (including grouped operands), contextual unsuffixed integer literals (including unary signed minus and decimal minima), either order of parameter/literal integer arithmetic or bitwise operations and integer/`char16` comparisons, two contextual 32-bit integer literals in arithmetic or bitwise operations, Boolean equality, bitwise or short-circuit operations between parameters and/or literals, optional `if`/`else` returns or a trailing fallthrough return, whole-expression grouping, and explicit `public`/`internal` visibility | AOT/JIT typed parameter, expression, branch and UTF-16 span facts under both policies; scanner integer records and destination bounds determine integer constants without rescanning digit text; unsupported or invalid shapes publish only a code/span, lexical failure publishes no artifact. Uses `Host.Source` and managed tokens, so neither the persisted primitives DLL nor `build-component` consumes its typed result yet |
| [ScalarFrontendProjection](ScalarFrontendProjection.terrum) | Provisional 34-word projection of the bounded native frontend result into a separate host-owned buffer, with completion status written last; version 3 encodes parameter/literal identity and order with integer magnitude halves or Boolean literal kinds, or one word per 32-bit integer literal in a pair; version 4 marks a returning branch followed by a fallthrough return | `ScalarFrontendHost` validates source-bound scalar/operand/span facts and integer bits, retains earlier projection versions, and lowers the supported subset to verified `BoundProgram` without the hosted parser or binder pass; signed unary literals reuse the shared scalar constant-folder. AOT/JIT local callers and an independent native DLL export exercise it under both policies. This is not a standalone native frontend artifact or a self-host compiler |
| [ScalarFunctionParser](ScalarFunctionParser.terrum) | Provisional scalar function probe: grouped/unary literals, two-literal checked arithmetic and bitwise operations, typed integer/`char16` comparisons, and one named scalar parameter in comparisons or integer checked addition | Hosted-span and executed-value parity with AOT/JIT at decimal/based boundaries across all four integer widths and `char16` comparisons; names bind to declarations, invalid magnitudes and checked addition overflow reject; requires `ScalarParserTypes`; other declarations still require the host parser |
| [ScalarParameterPairParser](ScalarParameterPairParser.terrum) | Separate bounded probe of two named scalar parameters returned through checked integer addition/subtraction, `x + y + literal` with both checked steps, or a Boolean comparison (including `char16`) | AOT/JIT under both policies across signed/unsigned 32-/64-bit extrema, code-unit comparisons and decimal/based literal boundaries; both identifiers bind to distinct declarations, either overflow or unsigned subtraction underflow rejects; requires `ScalarParserTypes`, not a bootstrap compiler |
| [ScalarConditionalParser](ScalarConditionalParser.terrum) | Separately compiled probe of a `bool` parameter selecting between two named, same-typed scalar parameters with explicit `if`/`else` returns | AOT/JIT under both policies check both reachable arms across six scalar types and reject swapped/unbound names and missing branches; uses scanner-owned tokens and `ScalarParserTypes`, not the general hosted parser or a bootstrap compiler |
| [ScalarConditionalAddParser](ScalarConditionalAddParser.terrum) | Separate bounded probe of a Boolean-controlled checked addition of two named integer parameters with a fallback return of the first parameter | AOT/JIT under both policies check successful addition, both overflow directions, and that a skipped addition cannot fail; scanner-owned tokens and shared scalar range guards, not general branch parsing or a bootstrap compiler |
| [ScalarComparisonConditionalParser](ScalarComparisonConditionalParser.terrum) | Separate bounded probe of two named integer/`char16` parameters used in a six-way comparison predicate selecting one parameter per `if`/`else` return | AOT/JIT under both policies check signed/unsigned and code-unit extrema, selected versus discarded arms, and rejected unbound/swapped identifiers; scanner-owned token records, not an adopted Body encoding or a bootstrap parser |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Executed through fixed checked host buffers with overflow-safe record-capacity checks for tokens, trivia, integers and diagnostics; see the [precise subset](../docs/scanner.md) |
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
The decimal entry points require capacity for the actual output (at most
twenty words for 64-bit values, or eleven for signed 32-bit values).
`FormatUInt32` and `FormatInt32` widen their values through the same
decimal formatting path. Each formatter clears
the initialized prefix without reallocating, then uses
exclusive lexical views to reverse digits or insert a minus sign. The signed
minimum is handled without negating it in the signed domain. Output words hold
explicit ASCII decimal code values; this component does not select a default
runtime text encoding or implement general format strings.
The 32- and 64-bit `Base` entry points accept an explicit radix from 2 through
36 and publish lowercase ASCII digits (`0`-`9`, `a`-`z`), with a minus sign
for negative values rather than a two's-complement rendering. The `BaseUpper`
entry points share the same length queries and publish `A`-`Z` instead.
Their workspace must hold the actual output (up to 32 unsigned or 33 signed words for 32-bit
values, or 64 unsigned or 65 signed words for 64-bit values in base 2);
an invalid radix or insufficient capacity raises the existing checked
requirement failure without publishing a partial result. The decimal entry
points accept exact output capacity, including a single word for zero; a
20-word workspace can be reused for any decimal value. This is a bounded
library operation, not a language format string or an operation-specific
formatting witness.
`FormatChar16` and `FormatChar16Base` render the numeric value of a code unit,
including surrogate values, rather than decoding a Unicode scalar.

`IntegerParsing` reads a bounded slice of the immutable `Host.Source`
capability and precomputes its destination-specific quotient and final-digit
limits once per call before checked digit accumulation.
It accepts ASCII `0`-`9`, `A`-`Z` and `a`-`z` only where the digit is in
radix 2..36; signed parsing optionally accepts a leading `-`. It consumes
the **entire** nonempty slice, with no prefix, separator, whitespace or
implicit plus sign, and returns false without publishing an output on
malformed input, invalid bounds/radix or overflow. Negative zero is zero;
`int64.MinValue` is handled by accumulating its unsigned magnitude.
`TryParseUInt32Base`, `TryParseInt32Base` and `TryParseChar16Base` reuse the
validated parsing and enforce their narrower ranges during digit accumulation;
`char16` admits code-unit values including surrogate units, not decoded Unicode
scalars; signed 32-bit parsing does not first accumulate against the 64-bit range.
These compiled library functions do not replace the scanner's contextual
integer binding or create a runtime text representation.

`IntegerSaturation` supplies all twelve explicitly named cross-type
conversions among signed and unsigned 32-/64-bit integers. They compare in
the source domain before checked destination conversion, clamping values
outside the destination range to its nearest endpoint. In-range values are
preserved, including signed minima and maxima.
No ambient conversion policy, floating-point behavior or public generic
conversion witness is implied by these library functions. A separate module
consumer binds their twelve published signatures and runs without incorporating
the producer's source.

`IntegerWrapping` supplies all twelve named conversions across the four
signed and unsigned 32-/64-bit types. They retain the low bits for the
destination width and interpret signed destinations as two's complement,
including signed minima. Each internal checked conversion occurs only after
its value is in range. They run as an independent module without changing checked
`T(value)` behavior or establishing `unchecked` expression syntax.

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
than rescanning from the head for each token. Non-consuming peeks support
bounded indexed lookahead; a kind mismatch leaves the cursor in place without
publishing a token. The reader retains its chain and checks the chain version
before every peek or read. An append or clear invalidates old
readers: both operations then return false without publishing an output, while
`IsCurrent` distinguishes invalidation from ordinary end of stream. The reader
releases its remaining link on an invalidated peek or read; it does not preserve a snapshot
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
`TokenSnapshot.TryScan` instead copies validated kind/start/length triples
into one owned native buffer. Indexed reads are constant-time, remain stable
after the host scanner buffer changes and exceed the managed chain's 64-token
limit; native buffer capacity and allocation still bound the snapshot.
The chain and snapshot share scanner-record validation, but neither stores
trivia, diagnostic or integer-value records or constitutes a parser.
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
or a general text-view lifetime contract. Explicit ASCII, UTF-8, UTF-16 and
UTF-32LE length queries measure decoded scalars without allocating; their encoders
consume caller-provided `OwnedInt32Buffer` storage and write bytes or code
units as integer words. The UTF-16LE byte and UTF-32LE length queries
count bytes; `TryGetUtf16Length` instead counts UTF-16 code units.
ASCII refuses non-ASCII scalars; UTF-16 supplementary scalars become surrogate
pairs, while escaped surrogates remain invalid.
Exact capacity works for empty and nonempty literals; invalid ASCII and
insufficient capacity fail through the existing checked requirement and clean
the consumed owner. These conversions do not allocate decoded text, normalize
it, select a default runtime or metadata encoding, or add native string
expressions.

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
