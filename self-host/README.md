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
now emits approved metadata and compact-linear/indexed Bodies and loads them for
shared native execution without producer source, including bounded branches,
loops, integer shifts, initialized writable-local compounds/updates and
module-local scalar calls with the existing direct-call ABI.
Initialized six-primitive locals in lexical blocks, branches and loops preserve
scope visibility, outer mutations and per-iteration initialization through
existing indexed values, without adding executable lexical regions or places.
Indexed `do`/`for` emission preserves post-test ordering, one-time header
initialization and iterator execution on normal completion/nearest continue,
using the existing loop-region contract. Acyclic loops without a reachable
backedge remain outside this emitter; no synthetic edge or wire revision is added.
Its `uint32` checked-count instructions use audited equal-width bit
reinterpretations to preserve the source's `int32` count semantics.
It is implemented in C#,
supports fail-fast only, and does not make these broader Terrum compiler
candidates self-hosting or change their provisional frontend projections.
Syntax-37/scanner-6 adopts whole-star ordinary/documentation comment fences,
opaque nonnesting contents and exact one-/two-slash closers. Documentation
trivia kinds 4/5 and overlong-fence diagnostic 10 append to existing records
without widening them. The native snapshots/frontend consume those records
within unchanged bounds and projection versions; historical fixed-fence
outputs cannot masquerade as current parser input. No extraction schema,
other punctuation adoption, semantic format or native ABI is added.
Syntax-38/scanner-7 adds approved contiguous `@keyword` identifiers, preserving
raw token/trivia ranges but using the underlying ordinal name for identity.
Its separate shared `EscapeKeywords` catalog is closed over grammar §2's
reserved/contextual vocabulary, including currently unsupported constructs;
it excludes arbitrary names, `_`, primitives and legacy-only `out`, `foreach`,
`abstract`. The existing unescaped keyword behavior remains bounded and
unchanged. Scanner code recognizes escapes using the catalog, and typed
snapshot adapters reject contradictory identifier records. Native scalar
parameter/local comparisons and host reconstruction normalize the same name,
without changing projections 1–51, record widths or resource budgets.
The read-only catalog appends host grant 9, moving the frontend output grant
to index 10; the earlier nine scanner grants retain their indices.
This is source-profile adoption, not future construct support, a self-hosting
claim or a semantic/native/Contract format revision.
Syntax-39 requires receiver/name-attached opening delimiters in supported
hosted forms. Scalar frontend headers and scalar type calls, and the maintained
scalar parser/evaluator probes, use raw `Token.Range` ends/starts for the same
boundary. Ordinary, line and documentation comments all break attachment;
interior trivia and structural grouping remain legal. The host independently
audits current syntax before reconstructing successful projections, including
historical projection shapes. This does not add native general calls, indexing
or generics, or change scanner-7/schema-7, projections 1–51 or any bounds.
Syntax-40 enforces both-sided qualified type/namespace/import dots and
either-sided expression member dots before lookup. This applies when hosted
parsing compiles these candidates, including their ordinary/static member
expressions. The native scalar frontend and maintained scalar probes have no
supported dot-bearing input forms; no native namespace/import, qualified type
or member syntax is added. Existing successful-projection current-syntax and
source/record audits reject contradictory or unrepresentable dot shapes.
Scanner-7/schema-7, projections 1–51 and all resource bounds are unchanged.
Qualified attributes, optional member access and other spacing remain outside
this adoption.
The host parser's current language version is syntax-42. Supported symbolic
expression binary/binding/assignment operators and expression-body arrows
require trivia on both sides; symbolic prefixes and postfix updates attach
to the immediate operand token boundary. Native scalar frontend shape
selection and maintained probes validate raw snapshot ranges before fallback,
and successful projection reconstruction independently audits current syntax.
Legacy projection publication shares monotone operation-threshold checks to
retain the whole-program verification budget. Scanner-7/schema-7, projections
1–51 and the 16K-source/64-token/8-local/12-statement/32-node/3-parameter
limits remain unchanged. This adds no general calls/member access or new
constructs or operator-run scanning.
Syntax-42 also requires both-sided trivia for conditional `?`/`:` in native
scalar input, directly checked before modern/legacy selection by the shared
snapshot check. Hosted struct-conformance/generic-constraint colons require
both-sided trivia; loop-label/named-argument colons attach to the name with
optional trivia afterward, and optional-type `?` attaches to the final type
token. These hosted roles do not introduce native labels, named arguments or
optional types. Hosted named arguments retain syntax-only support and their
existing binding rejection. Interface-base/enum-underlying lists, tuple names and attribute
targets remain unsupported. Historical/current projection audits enforce
current syntax; string/format interiors, scanner/projection identities, all
budgets and native formats remain unchanged.
Syntax-18's qualified
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
ABI change. Designated Core primitive declarations use the same readonly
by-value getter syntax, property identity and accessor effects with semantic
v38, retaining their private compiler-owned backing and native ABI. Core
constructors, operators, setters and general library bootstrap remain
unsupported.
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
Syntax-28 adds expression-bodied supported functions, methods, operators,
constructors and direct/explicit getters. The compiled scanner recognizes
`=>` from the shared operator catalog without changing scanner-5/schema-5;
`build-component` preserves hosted-parser/binder behavior and native output.
This is not an implementation of lambdas, new accessor kinds or conversion
declarations, nor a promotion of design candidates to executable libraries.

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
nominal constraints. Syntax-19 also implements six separately constrained
nominal comparison protocols over homogeneous Copy structs with `bool`
results; see the [bounded generic contract](../docs/implementation.md#bounded-generic-functions).
No comparison law or universal equality/hash behavior is inferred. Unary
protocols have independent `Copy` results, including non-`bool` `!`; these
protocol sets still exclude heterogeneous binary, shift and `true`/`false`
witnesses. Syntax-35's nominal `++`/`--` calls are a separate update contract,
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
on the contextual literal. Standalone grouped signed-minus literals use
provisional projection version 7; grouped `!` around a two-parameter
Boolean binary operation uses version 8, and grouped `!` around a
Boolean binary operation with Boolean literal operands uses version 9;
numeric comparisons with literal operands use version 10; version 11
admits grouped integer binaries under unary `~`; version 12 admits the same
bounded integer binaries under unary `+` or checked `-`; version 13
admits unary `~` over a grouped unary `+` or checked `-` parameter
or contextual integer literal.
Version 14 admits unary `+` or checked `-` over a grouped `~` parameter
or contextual integer literal.
Version 15 admits unary `!` over a grouped `!` parameter/literal or a
grouped `!` Boolean binary.
Version 16 admits unary `~` over a grouped unary `+` or checked `-`
integer binary.
Version 17 admits unary `+` or checked `-` over a grouped complemented
integer binary.
Version 18 admits unary `~` over a grouped complemented integer binary.
Version 19 admits a third named parameter and encodes its parameter/literal
expressions with a disjoint operation offset.
This does not extend
the provisional projection to arbitrary nested expressions.
Thirty-one bounded `int32 Main()` fixtures likewise emit byte-identical Windows
executables and run under both policies; this does not establish an independent
native emitter or compiler self-build.

| Candidate | Purpose | Gates before execution |
|---|---|---|
| [CopyAlgorithms](CopyAlgorithms.terrum) | NoThrow selection and conditional `out` or `init ref<T>` output of complete Copy values without operator witnesses | Composed AOT/JIT calls select scalars, nested tokens and ranges under both policies; invalid indices publish no value; existing `out` calls remain valid; no primitive operator conformance, borrowed views or generic runtime dispatch |
| [IntegerFacts](IntegerFacts.terrum) | Internal `[ConstEval]` eligibility predicates for checked `int32`, `uint32`, `int64` and `uint64` arithmetic, including signed negation and absolute value | Boundary predicates are proven in required static expressions and executed under both native policies; 32-bit signed checks widen safely, 64-bit checks rearrange bounds without overflowing, zero divisors reject, and signed-minimum remainder by `-1` remains valid; no public arithmetic API or compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed and unsigned 64-bit addition, subtraction, multiplication, division and remainder, signed negation and absolute value with conditional `init ref` publication, total unsigned signed-magnitude extraction, plus bounded decimal/radix-2..36 accumulation through the full unsigned range | Executed as separately compiled modules with conditional wide outputs and both `out` and vacancy-only `init ref` accumulation under both policies; division by zero, signed-minimum quotient overflow, negation and absolute value do not publish; magnitude includes the signed minimum |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed arithmetic extrema, invalid digits/radices, per-radix unsigned/signed limits and conditional output publication | Executed through AOT and JIT with a shared entry wrapper under both failure policies |
| [IntegerCheckedArithmetic](IntegerCheckedArithmetic.terrum) | Checked `int32`/`uint32` addition, subtraction, multiplication, division and remainder, plus signed negation and absolute value with conditional `init ref<T>` publication and total unsigned signed-magnitude extraction | Independent semantic-module producer/consumer with AOT/JIT and both failure policies; zero divisors, signed-minimum quotient overflow, negation and absolute value do not publish, while signed-minimum remainder by `-1` is zero and magnitude includes it; these are named library functions, not new operator syntax |
| [IntegerFormatting](IntegerFormatting.terrum) | Invariant signed/unsigned 32-/64-bit and `char16` code-unit decimal and explicit radix-2..36 digits (lowercase or uppercase) in reusable owned storage, with vacancy-only radix length queries for exact allocation | AOT/JIT per-radix length, exact-capacity and repeated-use fixtures; native heap checks cover same-allocation transfer and failure cleanup |
| [IntegerParsing](IntegerParsing.terrum) | Strict signed/unsigned radix-2..36 ASCII parsing of an exact UTF-16 source slice with conditional 32-/64-bit and unsigned `char16` code-unit `out` or `init ref` outputs | AOT/JIT under both policies; full-range per-radix boundaries, invalid spelling/bounds and unpublished outputs; existing `out` entry points remain unchanged |
| [IntegerCheckedConversions](IntegerCheckedConversions.terrum) | All twelve cross-type 32-/64-bit integer conversions, four fallible `char16` narrowings and four always-successful `char16`-to-integer widenings | Independent semantic-module producer/consumer with AOT/JIT under both policies; failed narrowing leaves `init ref` vacant while widening publishes any code unit, including surrogates, without Unicode scalar validation; named alternatives to failing checked type-calls, not new syntax or conversion witnesses |
| [IntegerSaturation](IntegerSaturation.terrum) | All twelve cross-type 32-/64-bit integer conversions, four `char16` narrowings that clamp, and four value-preserving `char16` widenings | AOT/JIT range and code-unit boundaries plus an independent compiled-module consumer under both policies; these named functions are provisional, not general conversion syntax or witnesses |
| [IntegerWrapping](IntegerWrapping.terrum) | All twelve cross-type signed/unsigned 32- and 64-bit conversions, four low-bit `char16` narrowings and four value-preserving `char16` widenings | AOT/JIT host-oracle cases and an independent compiled-module consumer under both policies; no `unchecked` syntax or new primitive conversion policy |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and conditional `out` or vacancy-only `init ref` publication through independent module consumers |
| [SourceLines](SourceLines.terrum) | Streaming line ranges, line extents and bidirectional UTF-16 offset/line-position mapping over an immutable source grant | AOT/JIT `out` and vacancy-only `init ref` outputs under both policies; CRLF, every supported line break, EOF and malformed surrogate offsets agree with the host snapshot |
| [SourceLineIndex](SourceLineIndex.terrum) | Owned line-start table, random-access line content and including-break extents, and bidirectional UTF-16 offset/line-position lookup | Executed as AOT/JIT under both failure policies; existing `out` APIs and vacancy-only `init ref` construction/lookups, exact offset round-trips, invalid output and owner-copy rejection |
| [SourceLineTests](SourceLineTests.terrum) | Shared line-range/position fixtures, invalid offsets and repeated queries | Executed natively with host-supplied expected records; no source normalization or default output values |
| [StringLiteral](StringLiteral.terrum) | Validated quoted spelling and scalar decoding from the immutable UTF-16 source grant, with explicit ASCII, ISO-8859-1 (Latin-1), UTF-8, UTF-16 unit/UTF-16LE and BE byte, and UTF-32LE and BE byte length measurement and encoding into owned storage | Executed with conditional `out` and vacancy-only `init ref` creation/reads, all supported escapes and exact source boundaries; single-byte encoders reject unrepresentable scalars rather than substitute, while UTF bytes/units agree with host encoders without a byte-order mark; Latin-1 is not a StringRow selector or default runtime representation |
| [StringRow](StringRow.terrum) | Provisional tagged ASCII/UTF-8/UTF-16LE/UTF-32LE String-row measurement and emission from validated literal scalars into reusable owned byte words, with nonempty metadata-name and empty-permitted namespace paths that both reject embedded zero scalars, plus ordinal decoded-scalar name and namespace comparison | AOT/JIT fixtures check exact unit counts, encoding-width terminators, four-byte alignment, zero padding, invalid flags, rejected name/namespace text, escape-equivalent spelling, byte parity with the host String-row writer and checked undersized cleanup; not a TRRM section writer, reader, or literal-reference contract |
| [StringLiteralTests](StringLiteralTests.terrum) | Quoted-form validation, scalar values and rejection of offsets inside escapes or surrogate pairs | Shared host-lexer/compiled fixtures under both native runtime policies; failed calls publish no output |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII numeric and newline decisions | Executed with `char16` comparisons, checked numeric type-call conversions and verified NoThrow classification contracts |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [TokenCursor](TokenCursor.terrum) | Read-view traversal without copying backing storage or cleanup | Span indexing, matched access, stored backing dependencies, capture contract, constructor initialization |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed with a host-supplied entry wrapper; a Terrum test runner remains future work |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a vacancy-only initialized replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [ManagedTokenChain](ManagedTokenChain.terrum) | Bounded linked storage for complete Copy tokens and vacancy-only validated source ranges, with a sequential managed cursor, non-consuming indexed lookahead and conditional kind consumption | AOT/JIT fixtures under both policies; 64-link capacity, count/version changes, `out` and vacancy-only `init ref` cursor creation, lookahead and indexed/sequential Copy results, mismatch does not advance, mutation invalidation and collection after clear |
| [TokenSnapshot](TokenSnapshot.terrum) | Owned, indexed copy of a completed compiled scanner stream using three words per token, without the managed chain's 64-link limit | AOT/JIT indexed-read parity beyond 64 tokens, exact EOF and checked invalid indices; buffer capacity and allocation still bound the snapshot, trivia requires the separate snapshot, and diagnostics and general parser integration remain future work |
| [TokenTriviaSnapshot](TokenTriviaSnapshot.terrum) | Separately owned scanner-6 leading-trivia records with kind, range and owning token index, atomically published with `TokenSnapshot` by `TryScanLossless` | AOT/JIT host-lexer parity, contiguous source coverage, invalid record/owner rejection and input mutation independence under both policies; no diagnostic recovery, integer-value ownership or general CST parser |
| [TokenIntegerSnapshot](TokenIntegerSnapshot.terrum) | Separately owned scanner-6 numeric-token indices and raw unsigned magnitude words, published with tokens and trivia by `TryScanCompilerInput` | AOT/JIT full-`uint64`, radix and overflow fixtures, indexed token lookup, invalid index/status/tag/owner rejection and atomic failure; shape checks do not prove a producer's arithmetic, and general binding remains hosted |
| [TokenDiagnosticSnapshot](TokenDiagnosticSnapshot.terrum) | Independently owned lexical diagnostic codes and source ranges, available after a scan with errors even when token snapshots cannot publish | AOT/JIT host diagnostic parity under both policies, invalid buffer rejection, stable reads after host mutation and no success-shaped empty token stream on lexical failure |
| [ScalarParserTypes](ScalarParserTypes.terrum) | Shared scalar type recognition, token-name equality, scalar comparison and scanner-integer decoding for separately compiled bounded parser probes | Scanner-owned source ranges, UTF-16 spelling and integer records, with no independent syntax or semantic ABI |
| [ScalarFrontend](ScalarFrontend.terrum) | Reusable bounded frontend phase over a successful scanner snapshot: one scalar function with zero to three named parameters, parameter references, Boolean literals or unary `!` over a Boolean literal/parameter (including grouped operands), `~` over a contextual integer literal/parameter (including grouped operands), `+` over a contextual integer literal/parameter or named parameter (including grouped operands), or signed `-` over a named integer parameter (including grouped operands), contextual unsuffixed integer literals (including unary signed minus and decimal minima), either order of parameter/literal integer arithmetic or bitwise operations and integer/`char16` comparisons (including unary `+` or `-` on supported literal operands), two contextual 32-bit integer literals in arithmetic or bitwise operations, Boolean equality, bitwise or short-circuit operations between parameters and/or literals, conditional returns or trailing fallthrough where valid, whole-expression grouping, and explicit `public`/`internal` visibility; v48 adds structured returns and v49 adds integer shifts, compounds and local updates across branches/loops | Bounded scalar type, expression, source-span and control-flow projection consumed by the shared host verifier/emitter; scanner integer records and destination bounds determine integer constants without rescanning digit text. Uses `Host.Source` and managed tokens, so neither the persisted primitives DLL nor `build-component` consumes its typed result yet |
| [ScalarFrontendProjection](ScalarFrontendProjection.terrum) | Provisional 392-word host-owned buffer with completion status written last. Version 21 adds initialized locals after v20 trees; v22/v23 add checked numeric conversions without/with locals; v24–27 add conditional trees; v28–31 add assignment/nested-while bodies; v32–39 add braced if/else bodies and combinations with loops/conversions/conditional expressions; v40–43 add nearest-while transfers without branches; v44–47 add transfers with branches; v48 adds structured returns; v49 adds integer shifts and local mutations. Earlier payloads remain supported and unused extensions are zero | `ScalarFrontendHost` checks source anchors, scalar types, literal context/bits, node order, precedence, conditional children, roots, sequential local binding, loop/branch-body extents, transfer/return source, type and target, mutability and conversion shadowing before lowering to verified `BoundProgram` without the hosted parser/binder pass. Reachable non-void fallthrough is rejected. The 392-word buffer, 64-token input, eight locals, twelve statement records, thirty-two nodes, sixteen expression-recursion levels and six shared structural levels remain fixed; no standalone native compiler or machine-code encoder |
| [ScalarFunctionParser](ScalarFunctionParser.terrum) | Provisional scalar function probe: grouped/unary literals, two-literal checked arithmetic and bitwise operations, typed integer/`char16` comparisons, and one named scalar parameter in comparisons or integer checked addition | Hosted-span and executed-value parity with AOT/JIT at decimal/based boundaries across all four integer widths and `char16` comparisons; names bind to declarations, invalid magnitudes and checked addition overflow reject; requires `ScalarParserTypes`; other declarations still require the host parser |
| [ScalarParameterPairParser](ScalarParameterPairParser.terrum) | Separate bounded probe of two named scalar parameters returned through checked integer addition/subtraction, `x + y + literal` with both checked steps, or a Boolean comparison (including `char16`) | AOT/JIT under both policies across signed/unsigned 32-/64-bit extrema, code-unit comparisons and decimal/based literal boundaries; both identifiers bind to distinct declarations, either overflow or unsigned subtraction underflow rejects; requires `ScalarParserTypes`, not a bootstrap compiler |
| [ScalarConditionalParser](ScalarConditionalParser.terrum) | Separately compiled probe of a `bool` parameter selecting between two named, same-typed scalar parameters with explicit `if`/`else` returns | AOT/JIT under both policies check both reachable arms across six scalar types and reject swapped/unbound names and missing branches; uses scanner-owned tokens and `ScalarParserTypes`, not the general hosted parser or a bootstrap compiler |
| [ScalarConditionalAddParser](ScalarConditionalAddParser.terrum) | Separate bounded probe of a Boolean-controlled checked addition of two named integer parameters with a fallback return of the first parameter | AOT/JIT under both policies check successful addition, both overflow directions, and that a skipped addition cannot fail; scanner-owned tokens and shared scalar range guards, not general branch parsing or a bootstrap compiler |
| [ScalarComparisonConditionalParser](ScalarComparisonConditionalParser.terrum) | Separate bounded probe of two named integer/`char16` parameters used in a six-way comparison predicate selecting one parameter per `if`/`else` return | AOT/JIT under both policies check signed/unsigned and code-unit extrema, selected versus discarded arms, and rejected unbound/swapped identifiers; scanner-owned token records, not an adopted Body encoding or a bootstrap parser |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Executed through fixed checked host buffers with overflow-safe record-capacity checks for tokens, trivia, integers and diagnostics; see the [precise subset](../docs/scanner.md) |
| [ManagedScanner](ManagedScanner.terrum) | Copy a completed compiled scanner token stream into newly published managed storage or a sequential cursor | AOT/JIT parity with host token kinds/ranges under both policies; lexical failure, malformed host records and more than 64 tokens leave the output vacant; independent copies survive clear and collection |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

The scalar frontend now also parses and binds bounded expression trees in
Terrum, including `=>` scalar functions and nested arithmetic, bitwise,
comparison and Boolean expressions with grouping and short-circuiting.
It retains the six scalar types and zero-to-three-parameter function envelope;
calls and floating values remain outside this expression subset. Projection
v49 adds primitive compounds and updates on writable named integer locals.
Projection v21 adds up to eight initialized typed/inferred scalar locals and
twelve ordered declaration/assignment statements before the terminal return
envelope. Only `mut` locals permit reassignment; forward/self references and
untyped numeric inference are rejected. Standalone vacant locals remain
hosted features, not part of this native component.
The host buffer has 392 words: the original 37-word header,
five tree-header words, at most 32 eight-word postorder nodes, two counts,
eight four-word local records and twelve five-word statement records. Recursive
descent is limited to 16 levels and input still uses the bounded token chain.
Older shapes retain their tags/payloads with zero unused extensions.
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
arm, with a zero high word. Only the new versions admit this operation; v1–23
payloads remain unchanged. Literal-only arms require a real result context,
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
rules to reject reachable non-void fallthrough. Earlier projections keep their
existing return shapes. There is no hosted-parser fallback or synthesized
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
nodes for an empty infinite for. All existing upper bounds, scanner IDs,
ABI/persistence and v1–50 shapes remain unchanged. Named transfers, calls,
standalone blocks and vacant/richer locals are not added.

The host's syntax-29 post-tested loops and named enclosing-loop transfers
also execute through existing checked lowering; semantic v39 is selected
when those retained nodes require it. Scanner record/schema and native ABI
remain unchanged.
Syntax-30 additionally supports standalone typed/inferred vacant locals in the
host, with feature-selected semantic v40 persistence and existing checked
publication, cleanup and managed rooting.
Host syntax-31 adds target-checked `?:` with selected-arm execution, conditional
output facts and feature-selected semantic v41. The compiled scanner supplies
the existing punctuation tokens; the native scalar subset is described above.
Host syntax-32 adds managed-handle `??` and statement-form `??=` through
feature-selected semantic v42. The scanner's shared catalog recognizes both
new operator tokens without changing its records or schema. Managed optionals
remain outside the native scalar frontend.
Host syntax-33 additionally implements checked integer `++`/`--` and scalar
ConstEval updates; retained nodes select semantic v43. The compiled scanner
recognizes their appended token IDs, but the scalar frontend does not accept
update expressions in its loop bodies.
Host syntax-34 adopts bodyless static operator requirements and an optional
first `<this T>` implementing-type formal for its bounded local protocols.
Conformances and generic constraints supply self, not an explicit argument;
ordinary no-self protocols remain supported. This source change does not
alter the scanner record/schema, semantic formats or native ABI.
Host syntax-35 additionally supports nominal static `++`/`--` calls for eligible
writable `Copy`-struct places. Existing static requirements, constrained
generic definition proofs, witness identity and self/no-self protocols apply;
the exact result type equals the operand, writeback occurs only after operator
success, and earlier effects are not rolled back. Instance mutation, owning
updates, arbitrary `char16` arithmetic and a new witness ABI are not added.
Retained nominal calls select semantic v44 while primitive updates retain
v43. Private operator bodies and closed specializations are replaceable behind
unchanged semantic/native consumers. This does not add public generic witnesses
or cross-module source-operator discovery. Nominal updates are outside the
scalar-only ConstEval interpreter and remain unsupported in this native
scalar frontend.
Host syntax-36 adds primitive `<<`, `>>` and `>>>`, and statement-form
`+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=` and `>>>=`
for initialized writable `int32`, `uint32`, `int64` and `uint64` places.
Arithmetic and bitwise operands match the destination type; shift counts are
`int32`. The place and old value are captured once before the right-hand side,
which runs once; checked computation precedes writeback, while earlier effects
remain on failure. Primitive scalar-slot compounds are supported by bounded
ConstEval. These operations reuse existing update-place/root machinery;
nominal compounds, `char16`, unique-value update destinations and new
destination loans remain unsupported. Shift and compound-assignment features
select semantic v45; compound statements use tag 26. The shared scanner
catalog appends their token identities without changing scanner-5 records or
schema-5.

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
Attached width suffixes are lexical errors (`TRM0006`); scanner-6 retains
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
