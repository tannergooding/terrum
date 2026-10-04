# Candidate Terrum compiler sources

These files are parallel design candidates, not a bootstrapped compiler.
The approved [source grammar](../docs/grammar-spec.md) defines the language
target, not the execution status of these candidates or approval of their
sketched library APIs.
They are not linked into the C# implementation. The native profile executes
the range, character, integer and nested token components and a bounded token scanner.
The full-solution build also produces
`artifacts\bin\self-host\<configuration>\terrum-compiler-primitives.dll`
from `CharacterFacts`, `SourceRange` and `Token`. This maintained native
compiler-primitives library exports `Terrum.CodeAnalysis.IsAsciiDigit`,
`Terrum.CodeAnalysis.IsLineBreak` and `Terrum.CodeAnalysis.GetDecimalDigit`;
it is not a self-host compiler or an executable scanner/parser.
The host test runner remains C#, even when it compiles and executes a candidate.
The hosted [TRRM scalar pipeline](../docs/implementation.md#executable-trrm-scalar-interchange)
emits approved metadata and compact-linear/indexed Bodies and loads them for
shared native execution without producer source. It supports bounded branches,
switches, loops, integer operations and module-local scalar calls; non-nearest transfers
use scalar routing codes and designated single-exit indexed regions. This
source-free path remains fail-fast-only. The separate native scalar frontend
currently supports scalar/void functions, initialized scalar locals, structured
branches and loops, named loop transfers, standalone requirements/discards,
and selected function-effect contracts. Projection v85 carries named by-value
and scalar `out`/`init ref<T>` arguments through bounded scalar self/global
calls while preserving source evaluation order; overload selection remains
unsupported. V85 is the only produced and accepted tag for both projection
families, every complete-program frame and diagnostic records. Exact layouts
and audits are in the
[native profile](../docs/native.md) and
[implementation reference](../docs/implementation.md). General attributes,
binding and broader call forms remain outside the frontend's subset.
The bounded Terrum-authored scalar frontend now projects same-format
`float32`/`float64` unary signs, target-typed decimal and hexadecimal floating
literals, based integer literals directly targeting either format, `+`, `-`,
`*`, `/` and IEEE comparisons. The hosted reader validates token spans and
reconstructs exact target-format literal bits. It also projects conversions
between the floating formats and from `int32`, `uint32`, `int64` and `uint64`
to either format. `char16`-to-floating conversion is also provisionally
projected through v85 under its still-unapproved eligibility proposal;
verification and emission remain hosted. Float-to-integer conversions are
provisionally projected through v85 under the still-unapproved pair-matrix
proposal; verification and emission remain hosted. Source-free TRRM
same-format comparisons and exact-bit constants are provisionally implemented
under the unapproved
[TRRM floating-comparison proposal](../docs/proposals/floating-trrm-comparisons.md);
explicit conversion between the floating formats is separately provisional
under the unapproved
[TRRM floating-conversion proposal](../docs/proposals/floating-trrm-format-conversions.md).
Fixed-width integer-to-floating conversion is separately provisional under
the unapproved
[TRRM integer-to-floating proposal](../docs/proposals/integer-to-floating-trrm-conversions.md).
Unary floating negation is separately provisional under the unapproved
[TRRM floating-negation proposal](../docs/proposals/floating-trrm-negation.md).
Same-format floating add, subtract, multiply and divide are separately
provisional under the unapproved
[TRRM arithmetic proposal](../docs/proposals/floating-trrm-arithmetic.md) and
its separate [arithmetic-NaN proposal](../docs/proposals/floating-arithmetic-nan-encoding.md).
Float-to-integer conversion is separately provisional under the unapproved
[TRRM float-to-integer proposal](../docs/proposals/floating-to-integer-trrm-conversions.md).
Remainder and mixed-format arithmetic remain unsupported there.
`char16`-to-floating conversion is provisionally encoded
under the unapproved
[TRRM char16 conversion proposal](../docs/proposals/char16-to-floating-trrm-conversions.md),
which follows the separate unsigned code-unit
[eligibility proposal](../docs/proposals/char16-floating-conversion-eligibility.md).
The bounded scalar frontend candidate also provisionally projects this
conversion. The hosted binder and emitter treat a code unit as an unsigned
numeric value; this eligibility remains unapproved.
Both current v85 projections carry
scalar non-Boolean/void `init ref<T>` declarations; definite-publication
verification and emission remain hosted. V85 includes
source-audited caller-unsafe callable declarations and explicit unsafe-block
call authorization and source-audited `unsafe(expr)` call-site scopes;
v85 maps named by-value and scalar output call arguments to formal parameters
without changing source-order evaluation. Their verifier and emitter also
remain hosted.
The single-function buffer also audits self-call authorization against enclosing
unsafe scopes. Both current projections
source-audit `is init` tests for named scalar locals and parameters. Verification
and emission remain hosted; this projection does not make the frontend a
standalone compiler.
Initialized six-primitive locals in lexical blocks, branches and loops preserve
scope visibility, outer mutations and per-iteration initialization through
existing indexed values, without adding executable lexical regions or places.
Indexed `do`/`for` emission preserves post-test ordering and one-time header
initialization. Normal completion or `continue` targeting a `for` reaches its
iterator once; `continue` targeting a `do` reaches its condition. Acyclic loops
without a reachable
backedge remain outside this emitter; no synthetic edge or wire revision is added.
Its `uint32` checked-count instructions use audited equal-width bit
reinterpretations to preserve the source's `int32` count semantics.
It is implemented in C#,
supports fail-fast only, and does not make these broader Terrum compiler
candidates self-hosting or change their provisional frontend projections.
Current scanner behavior and record shapes are specified in the
[scanner contract](../docs/scanner.md); hosted parser and native projection
boundaries are in the [implementation reference](../docs/implementation.md).
Scanner-10/schema-10 appends `DotDot`, `DotDotCaret`, and `QuestionDot` token
kinds 104–106 after Scanner-9/schema-9 added `CharacterLiteral` kind 103.
Scanner-8/schema-8 diagnostic identities 11/12 for invalid and unsupported
maximal runs remain unchanged. These punctuation tokens do not imply parser
consumption or binding in the native scalar frontend. The hosted parser and
binder consume constant and dynamic `^offset` bounds for compiler-supplied
host-buffer indexing and dynamic absolute or end-relative `..`/`..^` ranges for
readonly `OwnedInt32Buffer` lexical views;
`?.` remains token-only. Hosted
generic closers and optional suffixes require parser consumption; native
scalar spacing permits only the `init ref<T>` parameter delimiters, not
general generic type contexts. Raw buffers have no schema marker and current
adapters interpret their records only with current identities and shapes; no
historical-schema reader is retained. Scanner,
projection and ABI formats are distinct. Projection tags are alpha
implementation/validation details, not roadmap or product versions. Both scalar
frontend readers reject every v1–v84 tag; no obsolete reader support is retained.
V85 covers the existing compact expression and tree/statement layouts without
changing either buffer layout. Current producers use both layouts, so neither
is a historical comparison format. Every rebuild uses v85, independent of source
features; readers have no historical selectors or per-feature version gates.
Progress does not require a version bump per
increment; a preview version, if any, is decided explicitly when shipping.
V85 uses a separate 4,284-word
multi-function buffer alongside the 392-word single-function buffer. It admits
void-target calls as direct expression-statement roots.

The native scalar frontend remains a bounded projection, not a standalone
compiler: it shares the hosted verifier/emitter and is limited to 16K source
units and 64 tokens per snapshot, with 8 total named/hidden local identities,
12 statement records, 32 expression nodes and 3 parameters per function.
V85 inventories top-level global declarations before parsing bodies, resolves
direct scalar-returning calls in declaration order regardless of source order,
and rejects duplicate normalized names, local/parameter shadowing at call sites,
and mismatched argument counts/types. Its ten-function capacity follows from
the existing token bound: even an empty void function needs six tokens, and
one token is reserved for EOF. Functions may have zero through three scalar
parameters, including the existing scalar `out` and `init ref<T>` modes with
matching explicit or declared destinations. Void callers may invoke scalar
callees. Calls to void functions are admitted only as direct expression-statement roots,
including supported `for` clauses;
void calls remain invalid in value contexts. V85 admits the existing void-only
`[DoesNotReturn]` contract and accounts for its continuation effect at direct
call-statement roots. Older tags are rejected even when their payloads would
otherwise describe an accepted source shape; tests use current-tag fixtures
and explicit obsolete-tag rejection.
The bounded generic, ownership, initialization
and constant-evaluation contracts are detailed in the
[implementation reference](../docs/implementation.md);
candidate library declarations do not become executable APIs by appearing here.

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
including `operator` and `where`, without a second unescaped keyword table. `abstract`
is an ordinary identifier; its retired token slot is not reused.
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
nominal constraints. The hosted compiler supports six separately constrained
nominal comparison protocols over homogeneous Copy structs with `bool`
results; see the [bounded generic contract](../docs/implementation.md#bounded-generic-functions).
No comparison law or universal equality/hash behavior is inferred. Unary
protocols have independent `Copy` results, including non-`bool` `!`; these
protocol sets still exclude heterogeneous binary, shift and `true`/`false`
witnesses. Nominal `++`/`--` calls are a separate update contract,
not an addition to those protocol sets.
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
Compilation errors in the bundled frontend sources report their own source
locations (including source indexes in the multi-source JSON schema).
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
Two hundred and thirty-seven shared scalar/conditional/Boolean/integer-literal fixtures have byte-identical DLL output from
the hosted and Terrum-authored frontend paths under both runtime policies;
both paths still use the hosted native emitter.
The bounded parameter/literal operands accept grouping inside or outside
unary `+`/`-`; decimal signed minima still require the sign directly
on the contextual literal. The compact expression layout retained by v85
supports grouped signed-minus literals, grouped Boolean negation of
parameter/literal binaries and numeric comparisons with literal operands.
Grouped integer binaries support unary `~`, unary `+` and checked `-`.
Nested encodings support complement over signed parameters/literals or
binaries, signs over complemented parameters/literals or binaries, Boolean
double negation and complement over complemented integer binaries.
A third named parameter uses a disjoint operation offset for its
parameter/literal expressions. These compact encodings do not represent
arbitrary nested expressions; bounded tree records cover the broader subset.
Thirty-one bounded `int32 Main()` fixtures likewise emit byte-identical Windows
executables and run under both policies; this does not establish an independent
native emitter or compiler self-build.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [CopyAlgorithms](CopyAlgorithms.terrum) | NoThrow selection and conditional `init ref<T>` publication of complete Copy values, with an explicit forwarding `out` adapter and no operator witnesses | Composed AOT/JIT calls select scalars, nested tokens and ranges under both policies; invalid indices publish no value; existing `out` calls remain valid; no primitive operator conformance, borrowed views or generic runtime dispatch |
| [IntegerFacts](IntegerFacts.terrum) | Internal `[ConstEval]` eligibility predicates for checked `int32`, `uint32`, `int64` and `uint64` arithmetic, including signed negation and absolute value | Boundary predicates are proven in required static expressions and executed under both native policies; 32-bit signed checks widen safely, 64-bit checks rearrange bounds without overflowing, zero divisors reject, and signed-minimum remainder by `-1` remains valid; no public arithmetic API or compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed and unsigned 64-bit addition, subtraction, multiplication, division and remainder, signed negation and absolute value with conditional `init ref` publication, total unsigned signed-magnitude extraction, plus bounded decimal/radix-2..36 accumulation through the full unsigned range | Executed as separately compiled modules with vacancy-only `init ref` outputs and accumulation under both policies; division by zero, signed-minimum quotient overflow, negation and absolute value do not publish; magnitude includes the signed minimum |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed arithmetic extrema, invalid digits/radices, per-radix unsigned/signed limits and conditional output publication | Executed through AOT and JIT with a shared entry wrapper under both failure policies |
| [IntegerCheckedArithmetic](IntegerCheckedArithmetic.terrum) | Checked `int32`/`uint32` addition, subtraction, multiplication, division and remainder, plus signed negation and absolute value with conditional `init ref<T>` publication and total unsigned signed-magnitude extraction | Independent semantic-module producer/consumer with AOT/JIT and both failure policies; zero divisors, signed-minimum quotient overflow, negation and absolute value do not publish, while signed-minimum remainder by `-1` is zero and magnitude includes it; these are named library functions, not new operator syntax |
| [IntegerFormatting](IntegerFormatting.terrum) | Invariant signed/unsigned 32-/64-bit and `char16` code-unit decimal and explicit radix-2..36 digits (lowercase or uppercase) in reusable owned storage, with vacancy-only radix length queries for exact allocation | Negative signs join the reversed digit buffer before its single reversal; AOT/JIT per-radix length, exact-capacity and repeated-use fixtures; native heap checks cover same-allocation transfer and failure cleanup |
| [IntegerParsing](IntegerParsing.terrum) | Strict signed/unsigned radix-2..36 ASCII parsing of an exact UTF-16 source slice with conditional, vacancy-only `init ref` outputs for 32-/64-bit integers and unsigned `char16` code units | Signed parsing checks the initial sign position before delegating digit-slice bounds to magnitude parsing; AOT/JIT under both policies; full-range per-radix boundaries, invalid spelling/bounds and unpublished outputs |
| [IntegerCheckedConversions](IntegerCheckedConversions.terrum) | All twelve cross-type 32-/64-bit integer conversions, four fallible `char16` narrowings and four always-successful `char16`-to-integer widenings | Independent semantic-module producer/consumer with AOT/JIT under both policies; failed narrowing leaves `init ref` vacant while widening publishes any code unit, including surrogates, without Unicode scalar validation; named alternatives to failing checked type-calls, not new syntax or conversion witnesses |
| [IntegerSaturation](IntegerSaturation.terrum) | All twelve cross-type 32-/64-bit integer conversions, four `char16` narrowings that clamp, and four value-preserving `char16` widenings | AOT/JIT range and code-unit boundaries plus an independent compiled-module consumer under both policies; these named functions are provisional, not general conversion syntax or witnesses |
| [IntegerWrapping](IntegerWrapping.terrum) | All twelve cross-type signed/unsigned 32- and 64-bit conversions, four low-bit `char16` narrowings and four value-preserving `char16` widenings | AOT/JIT host-oracle cases and an independent compiled-module consumer under both policies; no `unchecked` syntax or new primitive conversion policy |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and vacancy-only `init ref` publication through independent module consumers |
| [Utf16Scalar](Utf16Scalar.terrum) | Decode one in-range UTF-16 source position to a Unicode scalar or `-1` for an unpaired surrogate; return its code-unit width | Shared by scanner, managed-token validation, character-literal, string-escape and validated string-literal decoding; callers retain their own diagnostics and token/literal boundaries |
| [CharacterLiteral](CharacterLiteral.terrum) | Decode source-ranged single-quoted literals into one Unicode scalar or the hosted lexical diagnostic code/span | The scalar frontend consumes successful scalars; the scanner consumes diagnostic-aware results after finding token boundaries; the host reader independently validates source spelling |
| [StringEscape](StringEscape.terrum) | Decode one string escape and report its scalar, consumed width, and hosted diagnostic boundary | Shared by validated `StringLiteral` decoding and scanner lexical diagnostics; escaped unpaired UTF-16 retains its separate diagnostic |
| [SourceLines](SourceLines.terrum) | Streaming line ranges, line extents and bidirectional UTF-16 offset/line-position mapping over an immutable source grant | AOT/JIT vacancy-only `init ref` outputs under both policies; invalid queries leave all outputs vacant, and CRLF, every supported line break, EOF and malformed surrogate offsets agree with the host snapshot |
| [SourceLineIndex](SourceLineIndex.terrum) | Owned line-start table, random-access line content and including-break extents, and bidirectional UTF-16 offset/line-position lookup | Executed as AOT/JIT under both failure policies; vacancy-only `init ref` construction and lookups, exact offset round-trips, unpublished invalid outputs and owner-copy rejection |
| [SourceLineTests](SourceLineTests.terrum) | Shared line-range/position fixtures, invalid offsets and repeated queries | Executed natively with host-supplied expected records; no source normalization or default output values |
| [StringLiteral](StringLiteral.terrum) | Validated quoted spelling and scalar decoding from the immutable UTF-16 source grant, with explicit ASCII, ISO-8859-1 (Latin-1), UTF-8, UTF-16 unit/UTF-16LE and BE byte, and UTF-32LE and BE byte length measurement and encoding into owned storage | Executed with vacancy-only `init ref` construction and scalar/cursor reads, all supported escapes and exact source boundaries; single-byte encoders reject unrepresentable scalars rather than substitute, while UTF bytes/units agree with host encoders without a byte-order mark; Latin-1 is not a StringRow selector or default runtime representation |
| [StringRow](StringRow.terrum) | Provisional tagged ASCII/UTF-8/UTF-16LE/UTF-32LE String-row measurement and emission from validated literal scalars into reusable owned byte words, with nonempty metadata-name and empty-permitted namespace paths that both reject embedded zero scalars, plus ordinal decoded-scalar name and namespace comparison | Name/namespace validation shares the encoding-size pass, comparison validates while comparing, emission reuses measured extents, and power-of-two header/alignment fields use shifts and masks; AOT/JIT fixtures check exact unit counts, encoding-width terminators, four-byte alignment, zero padding, invalid flags, rejected name/namespace text, escape-equivalent spelling, byte parity with the host String-row writer and checked undersized cleanup; not a TRRM section writer, reader, or literal-reference contract |
| [StringLiteralTests](StringLiteralTests.terrum) | Quoted-form validation, scalar values and rejection of offsets inside escapes or surrogate pairs | Shared host-lexer/compiled fixtures under both native runtime policies; failed calls publish no output |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Executed with `char16` comparisons, checked numeric type-call conversions and verified NoThrow classification contracts |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Readonly `Copy` indexing, checked non-owning dependency forwarding and a remaining-view cursor layout are accepted for the initial slice; implementation remains pending. See the [bounded proposal](../docs/proposals/span-backed-token-cursor.md); broader stored-view support remains open. |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed with a host-supplied entry wrapper; a Terrum test runner remains future work |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a vacancy-only initialized replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [ManagedTokenChain](ManagedTokenChain.terrum) | Bounded linked storage for complete Copy tokens and vacancy-only validated source ranges, with a sequential managed cursor, non-consuming indexed lookahead and conditional kind consumption | Matching-kind reads check version and consume one node in a single read; AOT/JIT fixtures under both policies cover 64-link capacity, count/version changes, vacancy-only `init ref` indexed lookup/lookahead/sequential reads, public `out` and internal `init ref` cursor creation, mismatch does not advance, mutation invalidation and collection after clear |
| [TokenSnapshot](TokenSnapshot.terrum) | Owned, indexed copy of a completed compiled scanner stream using three words per token, without the managed chain's 64-link limit | Scalar operator-spacing checks retain a rolling adjacent-token window; AOT/JIT vacancy-only `init ref` indexed reads beyond 64 tokens, exact EOF and checked invalid indices; buffer capacity and allocation still bound the snapshot, trivia requires the separate snapshot, and diagnostics and general parser integration remain future work |
| [TokenTriviaSnapshot](TokenTriviaSnapshot.terrum) | Separately owned scanner-6 leading-trivia records with kind, range and owning token index, atomically published with `TokenSnapshot` by `TryScanLossless` | The lossless composite validates each token once while building both owned snapshots; AOT/JIT host-lexer parity, contiguous source coverage, vacancy-only `init ref` reads, invalid record/owner rejection and input mutation independence under both policies; no diagnostic recovery, integer-value ownership or general CST parser |
| [TokenIntegerSnapshot](TokenIntegerSnapshot.terrum) | Separately owned scanner-6 numeric-token indices and raw unsigned magnitude words, published with tokens and trivia by `TryScanCompilerInput` | Compiler-input construction reuses the one-pass lossless token/trivia builder before validating magnitudes; AOT/JIT full-`uint64`, radix and overflow fixtures, vacancy-only `init ref` indexed lookup/find, invalid index/status/tag/owner rejection and atomic failure; shape checks do not prove a producer's arithmetic, and general binding remains hosted |
| [TokenDiagnosticSnapshot](TokenDiagnosticSnapshot.terrum) | Independently owned lexical diagnostic codes and source ranges, available after a scan with errors even when token snapshots cannot publish | AOT/JIT host diagnostic parity under both policies, vacancy-only `init ref` reads, invalid buffer rejection, stable reads after host mutation and no success-shaped empty token stream on lexical failure |
| [ScalarParserTypes](ScalarParserTypes.terrum) | Shared scalar type recognition, token-name equality, scalar comparison and scanner-integer decoding for separately compiled bounded parser probes | Vacancy-only `init ref` helper outputs; scanner-owned source ranges, UTF-16 spelling and integer records, with no independent syntax or semantic ABI |
| [ScalarFrontend](ScalarFrontend.terrum), [ScalarFrontendProgram](ScalarFrontendProgram.terrum) | Bounded v85 frontend over a successful scanner snapshot: source-bound scalar/void functions, up to three scalar parameters, supported expressions including single-scalar character literals only in representable `char16` contexts, and structured control flow, visible-local lifetime ending, explicit/inferred vacant locals, scalar constants, nested cleanup/recovery and foldable-true static proofs. Complete snapshots inventory global declarations and direct forward/self/mutual calls. Scalar out/init-ref modes support matching explicit/declared destinations and normal-return publication for non-Boolean/void results. Caller-unsafe declarations and explicit unsafe blocks/wrappers are source-audited. Named by-value and scalar output calls preserve source-order evaluation. Result/error objects are vacancy-only `init ref` outputs; handled parse diagnostics return a result, while false paths publish none. Non-scalar non-Boolean outputs, namespaces/imports, overload search, callable values, general references, typed catches, filters and unproven proofs remain unsupported. See the [native profile](../docs/native.md). | Produces independently audited source-bound projections for the shared hosted verifier/emitter; scanner integer records supply numeric constants, and the shared Terrum `CharacterLiteral` component decodes character token spans from `Host.Source` while the host reader independently validates them. Uses managed tokens; the persisted primitives DLL and `build-component` do not consume its typed result. |
| [ScalarFrontendProjection](ScalarFrontendProjection.terrum) | Host-owned buffers with completion status written last: 392-word single-function and 4,284-word complete-program buffers. Only v85 is produced and accepted, including diagnostics and every function frame; all v1–v84 tags reject. Existing compact-expression and tree/statement layouts remain fixed. Unused extensions are zero; absent call targets are `-1`. | `ScalarFrontendHost` independently checks source anchors, types, literals, node order/ownership, precedence, roots, modes, local binding/initialization/liveness, scopes, control flow, returns, mutability, conversions and exact declaration effects. Complete-program audits also check declaration sets, duplicate names, frame coverage, callees and argument ownership. Void-call statement roots, DoesNotReturn continuation, scalar out/init-ref destinations, unsafe call scopes and named argument mappings retain their exact validations. Shared verification checks effects, definite liveness/publication and reachable completion under the selected policy. Limits remain 64 tokens per snapshot; per function 8 total named/hidden locals, 12 statements, 32 nodes, 16 expression-recursion levels and 6 structural levels. See the [implementation reference](../docs/implementation.md). This is not a standalone compiler or machine-code encoder. |
| [ScalarFunctionParser](ScalarFunctionParser.terrum) | Provisional scalar function probe: grouped/unary literals, two-literal checked arithmetic and bitwise operations, typed integer/`char16` comparisons, and one named scalar parameter in comparisons or integer checked addition | Success-only parse helpers and every scalar evaluator output use vacancy-only `init ref`. Hosted-span and executed-value parity with AOT/JIT at decimal/based boundaries across all four integer widths and `char16` comparisons; names bind to declarations, invalid magnitudes and checked addition overflow reject; requires `ScalarParserTypes`; other declarations still require the host parser |
| [ScalarParameterPairParser](ScalarParameterPairParser.terrum) | Separate bounded probe of two named scalar parameters returned through checked integer addition/subtraction, `x + y + literal` with both checked steps, or a Boolean comparison (including `char16`) | Vacancy-only `init ref` outputs; AOT/JIT under both policies across signed/unsigned 32-/64-bit extrema, code-unit comparisons and decimal/based literal boundaries; both identifiers bind to distinct declarations, either overflow or unsigned subtraction underflow rejects; requires `ScalarParserTypes`, not a bootstrap compiler |
| [ScalarConditionalParser](ScalarConditionalParser.terrum) | Separately compiled probe of a `bool` parameter selecting between two named, same-typed scalar parameters with explicit `if`/`else` returns | Vacancy-only `init ref` parse/evaluator outputs; AOT/JIT under both policies check both reachable arms across six scalar types and reject swapped/unbound names and missing branches; uses scanner-owned tokens and `ScalarParserTypes`, not the general hosted parser or a bootstrap compiler |
| [ScalarConditionalAddParser](ScalarConditionalAddParser.terrum) | Separate bounded probe of a Boolean-controlled checked addition of two named integer parameters with a fallback return of the first parameter | Vacancy-only `init ref` parse/evaluator outputs; AOT/JIT under both policies check successful addition, both overflow directions, and that a skipped addition cannot fail; scanner-owned tokens and shared scalar range guards, not general branch parsing or a bootstrap compiler |
| [ScalarComparisonConditionalParser](ScalarComparisonConditionalParser.terrum) | Separate bounded probe of two named integer/`char16` parameters used in a six-way comparison predicate selecting one parameter per `if`/`else` return | Vacancy-only `init ref` parse/evaluator outputs; AOT/JIT under both policies check signed/unsigned and code-unit extrema, selected versus discarded arms, and rejected unbound/swapped identifiers; scanner-owned token records, not an adopted Body encoding or a bootstrap parser |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Escaped identifier and line-comment scans reuse each decoded scalar; quoted-token loops and literal decoders advance non-surrogate units directly and decode surrogate pairs only when needed; trivia and punctuation loops reuse current code units, numeric scans cache each current unit, keyword/operator matching filters by one cached leading unit, and hexadecimal integer/float classification shares one mantissa scan while valid floats resume at their exponent; explicitly compiled with `SourceRange`, `Utf16Scalar`, `CharacterLiteral` and `StringEscape`; vacancy-only `init ref` punctuation and magnitude helper outputs; executed through fixed checked host buffers with overflow-safe record-capacity checks; see the [precise subset](../docs/scanner.md) |
| [ManagedScanner](ManagedScanner.terrum) | Copy a completed compiled scanner token stream into newly published managed storage or a sequential cursor | AOT/JIT parity with host token kinds/ranges under both policies; lexical failure, malformed host records and more than 64 tokens leave the output vacant; independent copies survive clear and collection |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

### Scalar frontend implementation history (retired tags)

The v1–v84 labels below identify former feature milestones only. Their feature
selection and servicing statements describe retired implementations: none of
those tags is produced or accepted now. V85 includes all currently implemented
source subsets, with the existing layouts, limits and independent source audits.

The scalar frontend now also parses and binds bounded expression trees in
Terrum, including `=>` scalar functions and nested arithmetic, bitwise,
comparison and Boolean expressions with grouping and short-circuiting.
It retains the six scalar types and zero-to-three-parameter function envelope;
calls and floating values remain outside this expression subset. Projection
v49 adds primitive compounds and updates on writable named integer locals.
Projection v21 adds up to eight initialized typed/inferred scalar locals and
twelve ordered declaration/assignment statements before the terminal return
envelope. Only `mut` locals permit ordinary reassignment; forward/self
references and untyped numeric inference are rejected. Projection v61 adds
explicit typed `init T` locals, and v62 adds `init var` locals whose scalar
type is established by the first assignment's already-typed expression.
Untyped numeric literals do not infer a vacant local; unresolved inference and
incompatible later assignments reject. Projection v63 adds literal-initialized
scalar `const` locals. Projection v64 adds foldable typed scalar constant
expressions, including references to earlier const locals and
constant-selected conditional or Boolean short-circuit paths;
runtime-dependent evaluated expressions, unsupported forms and inferred const
declarations remain excluded.
Projection v65 adds nested `try { ... } finally { ... }` regions. Its source
reader validates separate body and cleanup extents, and shared verification
enforces NoThrow cleanup and existing cleanup-flow restrictions. `catch`
remains outside this component.
Projection v66 adds explicit `unsafe { ... }` scopes and bare `try`/`catch`
inside those scopes. Recovery is restricted to the checked-exceptions runtime
policy; typed catches, filters and a `finally` attached to catch remain
unsupported. The source-bound reader audits both extents before the shared
verifier/emitter path. Projection v67 adds the optional NoThrow `finally` for
this bounded catch shape; the reader validates its separate extent and shared
verification enforces cleanup and failure-edge rules.
Projection v68 adds bounded `static require` and `static ensure` proofs for
conditions the shared scalar folder reduces to true. `static require` may use
earlier const locals; `static ensure` is restricted to closed outer-body
conditions.
Projection v69 stores scalar parameter `out` modes in header word 16 as a
three-bit ordinal mask. The reader matches each set bit to the source keyword,
then binds the parameter as mutable output state and relies on hosted definite
assignment/publication verification; this adds declaration support only, not
general output call migration.
Single-function projection v79 stores scalar `init ref<T>` modes in header
word 17, distinct from the `out` mask; operations 107/108 encode existing and
declared init destinations. Complete-program v78 uses the same mode word in
each function frame. Complete-program v80 currently uses the v78 frame
arrangement and admits non-Boolean scalar `out` declarations. Single-function
v79 uses the same 392-word buffer as earlier single-function tags and admits
those declarations for bounded recursive self-calls. Those tags are retired;
the same supported source shapes now publish v85.
The host buffer has 392 words: the original 37-word header,
five tree-header words, at most 32 eight-word postorder nodes, two counts,
eight four-word local records and twelve five-word statement records. Recursive
descent is limited to 16 levels and input still uses the bounded token chain.
V85 retains both compact expression records and tree/statement records with
zero unused extensions, but no earlier tag is accepted.
The host validates the native nodes against source tokens and types without
running the hosted parser/binder, then uses the shared verifier/emitter.
Projection v22 adds checked numeric type-calls without local records; v23
combines them with local statements. All 25 pairs over `int32`, `uint32`,
`int64`, `uint64` and `char16` use existing checked conversion lowering.
Nested conversions and literal target context are source-validated, including
name shadowing and signed/based literal rules. `char16` remains a code unit,
not a validated Unicode scalar. The buffer size and node/depth limits stay
unchanged; Boolean conversions and ordinary calls remain unsupported.
Projections v24/v25 add right-associative target-checked `?:` trees without/with
locals; v26/v27 combine them with numeric conversions. Operation 84 uses its
left/right children for the condition/true arm and its low word for the false
arm, with a zero high word. Only the new versions admit this operation; older
tags may be retired once they no longer serve validation. Literal-only arms require a real result context,
not a predicate's numeric type, and only the selected arm executes.
DLL byte-parity, JIT results, invalid expression/projection cases and atomic
output retention and actual EXE execution are tested under both runtime policies. This does not add
the full approved expression grammar or a standalone native compiler.

Projections v28–31 add ordinary `while` loops with assignment-only bodies,
including up to six nested loops. Outer initialized declarations and
assignments may precede/follow loops before the prior terminal return
envelope; loop-local declarations, `break`/`continue` and early returns
remain unsupported in v28–31. Existing limits remain eight locals,
twelve statement records (including loop headers), thirty-two expression
nodes and a 392-word buffer. A zero-local empty loop is valid.
New statement kind 3 carries a Boolean condition root, a full-loop source span
and an exclusive body-end index in its slot field; body records follow in
preorder. Versions 28–31 admit it, respectively without extra expression
features, with conversions, with `?:`, or with both. The independent host
reader validates nested extents and source tokens before shared `BoundWhile`
lowering. Both-policy DLL byte parity, JIT/EXE execution, overflow failure,
structural-limit rejection and atomic failed-output retention are exercised.

Projections v32–35 add braced `if`/optional `else` assignment bodies and nested
branches; v36–39 additionally admit loops. Within each group, +1 adds numeric
conversions and +2 adds conditional expressions. Kind 4 stores the Boolean
condition root, whole-if span and exclusive true-arm end. An optional kind-5
else marker at that index has root -1, an else keyword/body span and an
exclusive else-arm end. The reader independently checks both source-bound
arms and derives their braced spans before shared `BoundIf` lowering.
Branches/loops share the six-level depth bound; headers and else markers
count toward twelve statement records. Only the selected arm runs. All six
scalar types, outer-only declarations, source binding and existing limits
are preserved. Body-local declarations, early transfers, unbraced arms and
`else if` shorthand remain unsupported in v32–39. Old terminal if-return shapes keep
their previous payloads; this is still not a standalone compiler.

Projections v40–43 add bare `break` and `continue` targeting the nearest
enclosing `while`, in loop bodies without branches. Projections v44–47 add
those transfers to braced branch bodies, including loops inside branches and
branches inside loops. Each four-version group uses its base for transfers,
+1 for numeric conversions, +2 for `?:`, and +3 for both. Statement kind 6 is
`break`; kind 7 is `continue`. `Slot` stores the statement-record index of the
nearest while header, root is `-1`, and the source span is exactly the keyword
and semicolon. The source reader independently checks nesting, target, type
and source before lowering the existing verified transfer nodes. Each
projection admits exactly its listed additions and leaves unused extensions
zero. Named transfers, loop-body declarations, early returns in v40–47,
general calls and native updates remain unsupported in v40–47. Transfers,
loop headers and else markers count toward the twelve statement records; all
versions retain the 392-word buffer, eight locals, thirty-two expression
nodes, sixteen expression-recursion levels and six shared structural levels.

Projection v48 adds structured scalar returns to branch and loop bodies.
Statement kind 8 carries slot 0, a typed result root and the
keyword-through-semicolon span; header words 38–40 are `-1`. The independent
reader consumes every expression root and applies the verifier's continuation
rules to reject reachable non-void fallthrough. Older return shapes may be
retired or revised; selected prior tags are validation aids only. There is no hosted-parser fallback or synthesized
terminal return. The input bound is 64 tokens; the 392-word buffer, 32 nodes,
12 statements, eight locals, 16 expression-recursion levels and six
structural levels remain fixed. Branch/loop-local declarations, general calls
and native updates are not added.

Projection v49 adds ordinary `<<`, `>>`, `>>>`, all eleven primitive compound
assignments, and prefix/postfix `++`/`--` expressions and statements.
Destinations are initialized writable named integer locals, including grouped
names; types are `int32`, `uint32`, `int64` and `uint64`. Counts are `int32`
with the hosted checked 0–31/0–63 range and cannot establish a shifted literal's
type. Shared update/compound lowering preserves old-value capture before RHS,
once-only ordered evaluation, checked-before-writeback and prefix/postfix
results. These operations compose with existing branches, loops, lazy
expressions and returns under both policies.
The node record keeps its eight words: operations 86/87 are updates (`Low`
selects prefix), 88–90 are shifts and 91–101 are compounds. Statement kinds
9/10 carry compounds/updates; only compounds carry a destination slot.
The independent reader validates source, typing, active writable locals and
record ownership; older versions reject the added operations. Versions 1–48,
all existing bounds, scanner records and native ABI remain unchanged.
This does not add calls, field/nominal mutations, parameter mutation, floating
values, vacant locals or a standalone compiler.

Projection v50 adds initialized declarations of the existing six scalar types
inside braced branch/loop bodies. Sequential binding and lexical visibility
match the hosted binder: an initializer cannot read its own or a later
declaration, active enclosing/parameter names cannot be shadowed, and closed
sibling scopes may reuse names with distinct monotonic slots. Reads and writes
cannot refer to expired slots. Loop initializers run on each entered iteration;
outer mutations, nearest transfers and structured returns retain their effects.
The native parser and independent source-bound projection reader enforce these
rules before shared verified lowering, without hosted parsing/binding fallback.
Nested declaration placement alone selects v50, including bodies with existing
mutations. Versions 1–49 remain serviced and reject nested declarations.
The eight-local bound counts all identities, not live names; 392 words,
64 tokens, 12 statements, 32 nodes, three parameters, 16 expression-recursion
levels and six structural levels remain unchanged. Both policies use the
existing ABI and semantic/native persistence. This does not add standalone
block statements, vacant locals, general calls or richer values.

Projection v51 adds braced `do`/`for` loops with the hosted scalar ordering and
scope rules. Nearest `continue` reaches the `do` condition or `for` iterator;
break/return skip those steps. A for initializer runs once and may be empty,
an initialized scalar declaration, named-local assignment, or supported
integer compound/update. Its iterator accepts the same statement forms except
declarations; empty condition means true. Header locals expire after `for`;
body locals are unavailable to its clauses or the trailing `do` condition.
Kind 11 is a post-tested header. Kind 12 is a for header followed by exactly
two initializer/iterator records, then its body. Both headers carry body-end
indices; for root `-1` means omitted condition. Empty clauses use kind 13,
slot 0, root `-1` and the delimiter span; nonempty clause spans omit delimiters.
All records share the twelve-record budget. Only new loop headers select v51.
The source-bound reader validates roles/scopes/spans before existing verified
lowering under both policies. Terminal-return envelopes remain serviced;
proven non-fallthrough loops need no synthetic return, and v51 permits zero
nodes for an empty infinite for. Current upper bounds and ABI/persistence
contracts remain; earlier projection tags are not compatibility targets.
Named transfers, calls,
standalone blocks and vacant/richer locals are not added.

Projection v52 adds standalone lexical blocks as kind 14 with an exclusive
body-end index, root `-1`, exact brace span and preorder children. The
independent source-bound reader validates scope and extent before shared
lowering; failure does not publish a partial projection. Versions 1–51 retain
their prior feature restrictions. See the
[implementation reference](../docs/implementation.md) for scope, resolution
and compatibility rules.

Provisional projection v53 appends kind 15 for non-void `_ = expression;`
over the existing six primitive scalar types. Discard has no contextual target
type (`_ = 1;` rejects; `_ = int32(1);` is valid). Its hidden readonly local
captures the expression once and counts toward the eight total named/hidden
identities. The independent source-bound audit and atomic publication rules
remain required; versions 1–52 retain their discard restrictions. This does
not change source grammar, scanner records, semantic/native formats or ABI;
later projections may carry kind 15 alongside newer records.

Projection v54 adds labeled loop headers and named nearest/outer transfers
under both policies; the source-bound record and lowering contract is in the
[implementation reference](../docs/implementation.md). It does not change
the source-free indexed-TRRM protocol.

Provisional projection v55 adds standalone `require condition;` using the
existing Boolean frontend and checked-failure behavior under both policies. It
adds no hidden local, source scope, grammar or metadata.

Projection v56 adds braced void functions with empty/implicit completion and
bare or grouped unit-literal returns. Source returns lower to null returns;
hosted implicit completion adds no projection record. Parameterless void
`Main` exits zero, and normalized public void exports keep the existing
zero-through-three scalar-parameter ABI under both policies. Unit expression
nodes/storage, void expression bodies in v56 and general calls remain excluded;
source/resource limits and historical projection restrictions are unchanged.

Projection v57 admits exactly one requirement/discard statement after a void
function's arrow. It reuses Boolean-target binding or a self-determined scalar
discard with one hidden readonly identity, preserves hosted spans and lexical
wrapping, and appends implicit null completion at the semicolon end. Earlier
projection contracts, all caps and the existing entry/export ABI remain.
Unit expressions, calls, assignment/update arrows and declarations remain
excluded; void arrow bodies do not implicitly return unit.

Projection v58 adds kind-23 markers for existing `else if` chains, with one
direct nested conditional false arm rather than a synthetic lexical block.
The source-bound reader audits association, subtree extents, roots, spans,
local membership and loop ancestry. Selected conditions, scope restoration,
outer mutations, transfers and failures retain hosted behavior under both
policies. Chains count toward existing structural/record/node limits; older
payloads and v57 arrow restrictions remain. No grammar, metadata or ABI
contract changes.

Projection v59 uses header word 2 bits 0/1/2 for public/NoThrow/DoesNotReturn
and requires actual effect flags. Exact raw parameterless names precede
visibility, in either order; unknown, duplicate, escaped or argument-bearing
attributes reject. Annotated braced bodies use structured records without
terminal roots, scalar arrows retain a typed envelope and void arrows retain
one requirement/discard record. V59 takes precedence over v56–58; older
payloads retain their restrictions. Shared folding and verification govern
effect proofs, void-only nonreturning declarations and suppressed implicit
completion, with hosted lexical/function/body/return spans. Unsupported
inactive source still rejects. Attribute tokens count against existing limits;
the 392-word buffer and 100,000-visit verification budget are unchanged.
Nonterminating test programs are compiled and audited, not executed.

Projection v60 adds kind 24 for `name = _;` on a visible named scalar local.
It carries the local slot, root `-1`, and source span; the shared hosted
verifier proves definite liveness and retains existing cleanup and assignment
authority. It composes with v59 effects without changing the ABI or native
profile, and keeps earlier projection gates intact.

Projection v61 adds explicit `init T name;` declarations for six scalar types.
Kind 25 declares the vacant local with root `-1`, while local flag `0x2`
preserves initialization authority through the shared hosted verifier. Existing
assignment records may initialize only a definitely vacant binding. The source
audit checks the exact scalar type/name and lexical scope, including `for`
initializers. Projection v62 adds `init var` for scalar locals when the first
assignment expression already has a scalar type; untyped numeric literals and
unresolved inference remain unsupported. Projection v63 adds block-local
`const T name = literal;` declarations for direct scalar literals; v64 adds
foldable typed scalar expression initializers and references to earlier const
locals. Unsupported expressions, inferred const declarations and const
declarations in `for` initializers remain unsupported.

Projection v65 represents nested `try` and `finally` blocks as separate
extents. The reader binds only the source-anchored cleanup body and rejects
catch clauses; shared verification enforces the hosted NoThrow and cleanup
control-flow contract.

Projection v66 represents unsafe scopes and bare catch handlers as separate
source-anchored extents. The reader binds recovery only under the explicit
unsafe block and checked-exceptions policy; typed catches, filters and
catch-plus-finally remain outside this projection.
Projection v67 represents an optional handler cleanup as a third extent and
binds it to `BoundTryCatch.Finally`; shared verification remains authoritative
for cleanup effects and failure edges.
Projection v68 represents static proof statements as source-anchored condition
roots and erases them after closed/foldable-true validation; only body
`static require` and outer-body `static ensure` are in this subset.

The hosted parser requires a single loop label and its loop introducer on the
same physical source line. `TokenSnapshot` rejects line breaks in the raw
colon-to-keyword gap before native parser selection, including comment
interiors; independent projection audits enforce the same boundary. Same-line
trivia remains optional. The
[implementation reference](../docs/implementation.md) records this contract
and the hosted semantic-version gates.

The hosted compiler additionally supports post-tested loops, named transfers,
vacant-local initialization, lifetime ending, target-checked conditionals,
managed-handle coalescing, scalar and nominal updates, primitive shifts and
compound assignments. Their verified behavior and semantic-reader gates are
in the [implementation reference](../docs/implementation.md). The scalar
frontend supports named loop transfers, vacant initialization and local
lifetime ending, but excludes optional types and managed handles; its accepted
operations and projection gates are in the [native profile](../docs/native.md).

### Source and managed component contracts

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
`TrySelectCopyInit<T>` accepts index zero or one and publishes the corresponding
value through vacancy-only `init ref<T>`; other indices return false without
publishing an output. Inputs remain ordinary eagerly evaluated values.
`TrySelectCopy<T>` preserves the explicit existing `out` contract by forwarding
to that `init ref<T>` implementation and publishing only after successful
selection.

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
preserved, including signed minima and maxima. Four `char16`-to-integer
widenings preserve every 16-bit code unit, including surrogates, without
interpreting it as a Unicode scalar.
No ambient conversion policy, floating-point behavior or public generic
conversion witness is implied by these library functions. A separate module
consumer binds the published signatures and runs without incorporating
the producer's source.

`IntegerWrapping` supplies all twelve named conversions across the four
signed and unsigned 32-/64-bit types. They retain the low bits for the
destination width and interpret signed destinations as two's complement,
including signed minima. Each internal checked conversion occurs only after
its value is in range. Four `char16`-to-integer widenings preserve every
code unit, including surrogates. They run as an independent module without
changing checked `T(value)` behavior or establishing `unchecked` expression syntax.

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
UTF-32 length queries measure decoded scalars without allocating; their encoders
consume caller-provided `OwnedInt32Buffer` storage and write bytes or code
units as integer words. UTF-16 and UTF-32 byte encoders support both LE and BE
without a byte-order mark. Their shared byte-length queries
count bytes; `TryGetUtf16Length` instead counts UTF-16 code units.
UTF encoders extract power-of-two fields with shifts and masks rather than
repeated division and remainder.
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
token boundaries (including valid hexadecimal floating `p`-exponent forms),
keywords, leading trivia and lexical diagnostic decisions execute in Terrum.
Untagged decimal, hexadecimal (`0x`/`0X`) and binary
(`0b`/`0B`) integer magnitudes, including digit separators, through the
unsigned 64-bit range are decoded natively without assigning a default numeric
type. Empty hexadecimal and binary prefixes
report `TRM0007` and `TRM0008`, respectively. Hexadecimal and binary literals
construct bits in their destination type; nonzero bits beyond its width are
diagnosed, and decimal signed-range and negative-minimum rules are unchanged.
Attached width suffixes are lexical errors (`TRM0006`); scanner-6 retains
recognized suffix-tag metadata only in the existing record for those rejected
tokens, while valid integer records are untagged. Larger integer magnitudes
are reported as beyond the decoder's range, not lexical errors. The compiled
scanner preserves decimal and valid hexadecimal floating tokens without
publishing integer magnitudes; the hosted binder constructs target-typed
`float32`/`float64` values and performs the supported numeric conversions,
including provisional checked conversions to fixed-width integers. Same-format
floating arithmetic/comparisons are projected by the Terrum-authored scalar
frontend for typed values, as are target-typed decimal/hexadecimal floating
literals and based integer literals directly targeting those formats. The
hosted reader validates literal token spans and reconstructs their target
bits. Conversion projection covers the two floating formats and the four
fixed-width integer types. `char16`-to-floating, floating-to-integer and
custom literal parser support remain outside this subset; those authored
components still rely on the hosted verifier/emitter. Bounded scalar
`[ConstEval]` is implemented separately from literal registration.
Contextual integer literal typing/range checks
and parser routines remain hosted, while
`build-component` binds the scanner's validated integer records without a host
magnitude-decoding fallback. The string-literal candidate
decodes scalars independently; it is not wired into the scanner side tables or
the hosted semantic binder. Keep shared
fixture expectations about observable ranges and contracts, not host object
layouts. An independent compiler bootstrap requires rebuilding and comparing
outputs, not merely parsing these files.
