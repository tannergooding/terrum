# Candidate Terrum compiler sources

These files are parallel design candidates, not a bootstrapped compiler.
The approved [source grammar](../docs/grammar-spec.md) defines the language
target, not the execution status of these candidates or approval of their
sketched library APIs. The unlinked `TokenCursor` candidate uses `init ref<Token>`
for both sequential and indexed reads because exhaustion and out-of-range
results leave their destinations vacant; this narrow migration does not settle
the general `out`-to-`init ref<T>` migration.
They are not linked into the C# implementation. The native profile executes
the range, character, integer and nested token components and a bounded token scanner.
The full-solution build also produces
`artifacts\bin\self-host\<configuration>\terrum-compiler-primitives.dll`
from `BitOperations`, `CharacterFacts`, `Float32MathSelection`,
`Float64MathSelection`, `IntegerCheckedArithmetic`, `IntegerFormatting`,
`IntegerCheckedConversions`, `IntegerFloatConversion`, `Char16FloatConversion`,
`FloatingFormatConversion`, `IntegerMath`, `SourceRange`, `Token`,
`FloatingNegation`, `FloatingComparison`, `FloatingArithmetic`,
`FloatingIntegerConversion`, `IntegerSaturation`, `IntegerWrapping`,
`Utf16CodeUnits`, `WideIntegerFacts`, `IntegerFacts` and `CopyAlgorithms`.
The test build also includes
`tests\native\integer-formatting-abi.terrum` for workspace setup and
consumption, `tests\native\token-abi.terrum` for aggregate ABI selection,
`tests\native\integer-facts-abi.terrum` for integer-fact predicates and
`tests\native\copy-algorithms-abi.terrum` for concrete Copy specialization;
these helper exports are test-only. The build also emits a separate
provisional `terrum-compiler-primitives-source-grant.dll` from
`CharacterFacts`, `SourceCodeUnit`, `SourceLineCount`, `SourceLineEnd`,
`CharacterLiteralPacked`, `CharacterLiteralScalar`, `HexDigitValue`,
`BasedIntegerFloatLiteral`, `HexFloatLiteral`, `SourceRangeValidation`,
`SourceTextLength`, `SourceOffset`, `SourcePosition`, `StringEscapePacked`,
`StringLiteralEncodedLength`, `StringNameRowByteLength`, `StringLiteralScalarAt`,
`StringLiteralScalarCount`,
`WideIntegerFacts`, `SourceIntegerParsing`,
`Utf16CodeUnits` and `Utf16Scalar`,
with a hash-bound export manifest; it does not change the source-free
compiler-primitives DLL or establish a stable ABI.
This maintained native
compiler-primitives library exports `Terrum.CodeAnalysis.SourceRange.TryCreate`,
`Terrum.CodeAnalysis.IsAsciiDigit`,
`Terrum.CodeAnalysis.IsLineBreak`, `Terrum.CodeAnalysis.GetDecimalDigit`,
`Terrum.CodeAnalysis.TryGetHexDigit`, `Terrum.CodeAnalysis.DecodeUtf16Scalar` and
`Terrum.CodeAnalysis.Utf16ScalarWidth`, plus
`Terrum.CodeAnalysis.Numerics64.MagnitudeInt64`,
`Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal` and
`Terrum.CodeAnalysis.Numerics64.TryAccumulateBase`,
`Terrum.CodeAnalysis.Numerics64.TryAddInt64`,
`Terrum.CodeAnalysis.Numerics64.TrySubtractInt64`,
`Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64`,
`Terrum.CodeAnalysis.Numerics64.TryDivideInt64`,
`Terrum.CodeAnalysis.Numerics64.TryRemainderInt64`,
`Terrum.CodeAnalysis.Numerics64.TryNegateInt64`,
`Terrum.CodeAnalysis.Numerics64.TryAbsoluteInt64`, and the five matching
unsigned binary helpers. It also exports the nine `BitOperations` functions
for each width: leading/trailing-zero count, population count, left/right
rotation, bit/endianness reversal, power-of-two test and checked round-up.
It also exports the fifteen existing `IntegerMath` functions across `int32`
and `int64`, including conditional absolute value, min/max/clamp, widened
`int32` multiplication and unsigned-magnitude GCD.
It also exports thirteen `IntegerCheckedArithmetic` helpers for checked
signed/unsigned 32-bit arithmetic, signed negation/absolute value and signed
magnitude.
It also exports all twenty existing `IntegerCheckedConversions` helpers for
integer width/signedness conversions and `char16` narrowings/widenings.
It also exports all twenty existing `IntegerSaturation` helpers for
integer and `char16` conversion candidates.
It also exports all twenty existing `IntegerWrapping` helpers for
integer and `char16` conversion candidates.
It also exports the five existing `IntegerFormatting` radix-length queries for
32-/64-bit signed and unsigned values and `char16` code units. Internal Windows
x64 direct-call tests compare radices 2 through 36 against a `BigInteger`
oracle and check invalid-radix output preservation with adjacent canaries.
This evidence does not approve the candidate formatting API or general
`init ref` behavior.
The DLL also exports all fifteen existing `IntegerFormatting` decimal and
radix formatting functions. Direct-call tests compare every emitted digit
against hosted decimal formatting or an independent `BigInteger` radix oracle
for bases 2 through 36, including exact-capacity workspaces and a reused
workspace. Test-only helpers create and consume the opaque owner inside
Terrum; this does not establish an external owner protocol or a stable native
ABI.
It also exports the four existing `Float32MathSelection` and four
`Float64MathSelection` functions. Their Windows x64 direct-call tests pass
floating representations through integer bit carriers, matching the Terrum
internal floating-value ABI rather than the standard C floating ABI. This is
internal execution evidence only; it does not approve either candidate API or
settle NaN payload, signaling, or status behavior.
It also exports the existing `Token` constructor and a test-only selector for
four by-value Token arguments and an aggregate return. Internal Windows x64
tests check the sequential C# carrier's current 12-byte layout and field
offsets, construct tokens from valid ranges, and select each argument in turn,
including the stack-passed argument. This is rebuild-coupled ABI evidence only;
it does not approve a public layout, stable C ABI, or exact hidden-result write
extent.
The source-grant-dependent `Utf16Scalar.ReadUtf16Scalar` adapter remains
outside the source-free DLL. The separate companion exports its checked
`ReadUtf16ScalarChecked` adapter and scalar-only queries for
`GetSourceLineCount`, `GetSourceTextLength`,
`GetSourceCodeUnitOrMinusOne`, `GetSourceOffsetOrMinusOne`,
`GetSourceLineNumberOrMinusOne` and `GetSourceCharacterPositionOrMinusOne`,
`GetSourceLineContentEndOrMinusOne`, `GetSourceLineEndOrMinusOne`,
`CanGetSourceRange`, `AnalyzeCharacterLiteralPacked`,
`GetCharacterLiteralScalarOrMinusOne`,
`AnalyzeStringEscapePacked`, `GetStringLiteralScalarCountOrMinusOne`,
`GetStringLiteralScalarOrMinusOne`,
`GetStringLiteralEncodedLengthOrMinusOne`,
`GetStringNameRowByteLengthOrMinusOne`,
`GetStringNamespaceRowByteLengthOrMinusOne`,
`GetHexFloat32LiteralBitsOrMinusOne`, `GetHexFloat64LiteralBitsOrMinusOne`,
`GetBasedIntegerFloat32BitsOrMinusOne`, `GetBasedIntegerFloat64BitsOrMinusOne`,
integer-parsing query pairs and the unchanged source-free decoder. The scalar
length, code-unit, offset, position, line-end
and range-validation components isolate `SourceText`/`SourceLines` behavior;
the code-unit query preserves every UTF-16 value, the offset query returns `-1`
for invalid line/character pairs, and the position queries return `-1` for
invalid source offsets. The two line-end queries return the content and
including-break exclusive ends, respectively, or `-1` for an invalid line;
callers combine either with the existing line-start offset query on the same
unchanged snapshot. The content end is the first break unit, while the
including-break end is after the complete break and may be the next line's
start. Position and line-end queries scan independently; querying both ends
for every line is not an indexed lookup. None of these queries is the
complete `SourceText` or `SourceLines` facade.
`CanGetSourceRange` returns true only for a source-bounded half-open range,
including a zero-length range at EOF; it returns false for negative or
out-of-snapshot bounds and does not return an aggregate range.
The persisted
semantic-module JIT test also checks every BMP code unit at a one-unit source
boundary under both runtime policies, ensuring that a following code unit
cannot complete a surrogate pair outside the requested extent. These are
internal compiler implementation exports, not a language-level API or stable
native ABI; the library is not a self-host compiler or an executable
scanner/parser.
Separately, `HexFloatLiteral.terrum` is a standalone conversion helper over a
caller-delimited token slice in invocation-scoped `Host.Source`. Persisted
semantic-module tests compare its direct `float32`/`float64` bit results with
hosted bound constants across IEEE rounding boundaries, malformed spellings,
deterministic random valid spellings and 16,000 fractional hex digits
under both runtime policies. It now supplies positive raw-bit literal payloads
to the bounded scalar frontend alongside the decimal and based-integer helpers;
the host reader independently checks those payloads against the hosted parser.
Unary signs remain separate frontend operations, and verification and emission
remain hosted.
`DecimalFloatLiteral.terrum` similarly converts caller-delimited decimal
numeric slices directly to `float32` or `float64` bits, without rounding
`float32` through `float64`. Its exact midpoint comparisons use an owned
multi-limb workspace and are covered by semantic-module parity tests for ties,
subnormal and overflow boundaries, malformed spellings, deterministic random
values and long significand/exponent inputs under both runtime policies. The
maintained source-grant companion also exports its two scalar `int64`
entry points with `int32` start/length parameters. The classifier admits only
the internal by-value owner transfers, lexical views and canonical buffer
intrinsics required by their verified helper closure; the exports remain
scalar-only and the source-free compiler-primitives DLL is unchanged. Direct
DLL checks cover the exact-rounding corpus and a maximum-length source grant.
The tracked native ABI probe verifies source-context forwarding, successful
owner cleanup in both runtime policies, and checked unwinding and allocation
failure without a live owner. Allocation failure remains a native checked or
fail-fast failure, distinct from malformed input. The scalar frontend still
compiles the helper into its semantic-module component, and this does not move
verification or emission out of the hosted implementation. The host test
runner still orchestrates compilation and assertions in C#.
`ComponentTestRunner.terrum` is a bounded Terrum entry harness composing the
`SourceRange`, `CharacterFacts`, `Utf16CodeUnits`, `IntegerFacts`, `Token`,
`CopyAlgorithms`, finite `Float32MathSelection`/`Float64MathSelection`,
bounded `ManagedTokenChain`, `WideIntegerFacts`, `IntegerFormatting`,
`IntegerCheckedArithmetic`, `IntegerMath`, `IntegerSaturation`,
`IntegerWrapping`, `IntegerCheckedConversions`, `IntegerFloatConversion`,
`Char16FloatConversion`, `FloatingArithmetic`, `FloatingComparison`,
`FloatingFormatConversion`, `FloatingNegation`, `FloatingIntegerConversion`,
and `BitOperations` module tests; it does not provide general test discovery or
replace the host runner.
The hosted [TRRM scalar pipeline](../docs/implementation.md#executable-trrm-scalar-interchange)
emits approved metadata and compact-linear/indexed Bodies and loads them for
shared native execution without producer source. It supports bounded branches,
switches, loops, integer operations and module-local scalar calls; non-nearest transfers
use scalar routing codes and designated single-exit indexed regions. This
source-free path remains fail-fast-only. The separate native scalar frontend
currently supports scalar/void functions, initialized scalar locals, structured
branches and loops, named loop transfers, bounded `switch` statements over
`int32`, `uint32`, `int64`, `uint64`, `bool` or `char16` with distinct constant
arms and a final `_` arm, standalone
requirements/discards, and selected function-effect contracts. Switch arm
bodies contain only already-supported statements; guards, patterns,
fallthrough forms, and missing or misplaced defaults reject. The v85
projection carries named by-value and scalar `out`/`init ref<T>` arguments
through bounded scalar self/global calls while preserving source evaluation
order; overload selection remains unsupported. V85 is the only produced and
accepted tag for both projection families, every complete-program frame and
diagnostic records. Exact layouts
and audits are in the
[native profile](../docs/native.md) and
[implementation reference](../docs/implementation.md). General attributes,
binding and broader call forms remain outside the frontend's subset.
Complete-program projections also resolve up to eleven top-level internal,
non-generic aliases to scalar types in function return, parameter and local
type positions, including forward chains. The source reader independently
resolves aliases from the original text. Duplicate, unresolved, cyclic,
public, attributed and generic aliases reject. The hosted separate
type/function name spaces are retained, and alias names are not explicit
conversion-call targets. Rejected aliases report their offending token span.
This preserves the hosted verifier/emitter boundary and does not add aliases
to source-free TRRM.
Complete-program v85 also carries switch statement kinds 38/39 and hidden
selector local flag 8 in previously invalid record values; frame layouts and
existing record meanings do not change. The reader accepts switch records only
after independently matching them to source and lowers them to ordered hosted
conditionals that evaluate the selector once. Older readers reject these
unknown records. Switch remains complete-program-only; verification and
emission remain hosted, and bootstrap support is not claimed. CLI integration
tests compare matching and default-arm selection for all six selector types
with hosted conditional equivalents at signed extrema, unsigned maxima, both
Boolean values, and surrogate/U+FFFF code-unit boundaries under both runtime
policies, and confirm duplicate constant-label rejection for all six types.
The bounded Terrum-authored scalar frontend now projects same-format
`float32`/`float64` unary signs, target-typed decimal and hexadecimal floating
literals, based integer literals directly targeting either format, `+`, `-`,
`*`, `/` and IEEE comparisons. Its Terrum-authored conversion helpers populate
positive literal-leaf bits; the hosted reader validates token spans and those
bits independently against the hosted parser, then consumes the validated
payloads. It also projects conversions between the floating formats and from
`int32`, `uint32`, `int64` and `uint64` to either format. `char16`-to-floating
conversion is also provisionally projected through v85 under its
still-unapproved eligibility proposal;
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
The standalone `FloatingFormatConversion` helper also converts raw bits
between `float32` and `float64` with hosted nearest/ties-to-even parity for
finite values. NaN payload bits are not promised; the helper does not approve
the TRRM proposal or add frontend, DLL or TRRM support.
Unary floating negation is separately provisional under the unapproved
[TRRM floating-negation proposal](../docs/proposals/floating-trrm-negation.md).
The standalone `FloatingNegation` helper toggles only the sign bit of raw
`float32` and `float64` values and preserves all other bits; it does not
approve the TRRM proposal or add frontend, DLL or TRRM support.
Same-format floating add, subtract, multiply and divide are separately
provisional under the unapproved
[TRRM arithmetic proposal](../docs/proposals/floating-trrm-arithmetic.md) and
its separate [arithmetic-NaN proposal](../docs/proposals/floating-arithmetic-nan-encoding.md).
Float-to-integer conversion is separately provisional under the unapproved
[TRRM float-to-integer proposal](../docs/proposals/floating-to-integer-trrm-conversions.md).
Remainder and mixed-format arithmetic remain unsupported there.
The standalone `FloatingIntegerConversion` helper implements raw-bit
float-to-integer conversion internally and is checked against hosted conversion
behavior; it does not approve the pair-matrix proposal or add frontend, DLL or
TRRM support.
`char16`-to-floating conversion is provisionally encoded
under the unapproved
[TRRM char16 conversion proposal](../docs/proposals/char16-to-floating-trrm-conversions.md),
which follows the separate unsigned code-unit
[eligibility proposal](../docs/proposals/char16-floating-conversion-eligibility.md).
The bounded scalar frontend candidate also provisionally projects this
conversion. The hosted binder and emitter treat a code unit as an unsigned
numeric value; this eligibility remains unapproved.
Both current v85 projections carry
scalar non-Boolean/void `init ref<T>` declarations, including `float32` and
`float64`; definite-publication
verification and emission remain hosted. V85 includes
source-audited caller-unsafe callable declarations and explicit unsafe-block
call authorization and source-audited `unsafe(expr)` call-site scopes;
v85 maps named by-value and scalar output call arguments to formal parameters
without changing source-order evaluation. Their verifier and emitter also
remain hosted. Library integration tests compare both caller-unsafe scope
forms byte-for-byte with hosted builds and execute the exported safe wrapper
under both runtime policies; unscoped calls retain the hosted `TRM3028`
diagnostic and reject without replacing an existing library artifact.
The single-function buffer also audits self-call authorization against enclosing
unsafe scopes. Both current projections
source-audit `is init` tests for named scalar locals and parameters. Verification
and emission remain hosted; this projection does not make the frontend a
standalone compiler.

Persisted TRRM provisionally admits caller-unsafe calls only inside structurally
validated lexical unsafe regions in compact-indexed fail-fast Bodies; these
regions grant no cleanup, unwind or lifetime authority, and `unsafe(expr)`,
compact-linear calls and unsafe members remain unsupported.

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
A maximum-length program with trailing trivia is compared byte-for-byte with
the hosted build under both runtime policies, and the next source unit is
rejected by the CLI before artifact publication and by the single- and
complete-program projection readers; this remains projection parity, not an
independent-verification or bootstrap claim.
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
The general cursor and host-interface sketches still require later language
slices. The bounded `TokenCursor` integration does not add general stored views
or substitute for those host interfaces. Executing these components is not a
self-hosted compiler bootstrap.
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
The hosted compiler provisionally supports `float32` and `float64` as concrete
Copy-only generic arguments, including through the maintained selection
candidate; they do not gain symbolic arithmetic or primitive witnesses. This
also provisionally hydrates floating leaves in concrete Copy-struct
dependencies reached by hosted generic proofs, while managed-class closures
remain excluded. Neither change adds generic support to the source-free scalar
frontend or TRRM. See the [generic-argument proposal](../docs/proposals/floating-copy-generic-specializations.md),
the separate [proof-dependency proposal](../docs/proposals/floating-copy-proof-dependencies.md),
and the [implementation boundary](../docs/implementation.md#bounded-generic-functions).
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
| [BitOperations](BitOperations.terrum) | Candidate `uint32`/`uint64` leading/trailing-zero counts, population counts, rotations, bit/byte reversal, power-of-two checks and vacancy-only round-up | Independently compiled module consumers compare boundary and deterministic values with hosted bit operations under both policies; separate width-specific consumers cover every one-hot/one-cold bit position, rotation count extremes and values around every power of two for round-up; zero and unrepresentable results leave outputs vacant, and `TRM3016` compile checks cover failed-output reads for both widths and policies; the internal Windows x64 fail-fast DLL directly invokes all 18 width-specific helpers against hosted `BitOperations`, including conditional-output preservation and adjacent canaries; the Terrum `ComponentTestRunner` also executes representative boundaries for all 18 helpers; candidate API remains unapproved |
| [IntegerMath](IntegerMath.terrum) | Candidate checked absolute values, sign/min/max/clamp, exact widened `int32` multiplication and signed-input GCD returning unsigned magnitudes | AOT/JIT and independent-module boundaries include signed minima, failed `TryAbs` vacancy/reuse, reversed clamp rejection and full-magnitude GCD; revision-56 persisted semantic-module JIT compares candidate operations against integer oracles across 858 boundary cases under both policies, including signed-minimum magnitude and failed-output checks; the internal Windows x64 fail-fast DLL directly invokes all fifteen functions for valid contracts, checks `TryAbs` failure preservation/canaries, and compares GCD with BigInteger; the Terrum `ComponentTestRunner` also exercises representative `int32`/`int64` boundaries; tuple-returning `DivRem`, public API approval and conversion witnesses remain out of scope |
| [Float32MathSelection](Float32MathSelection.terrum) | Unapproved `float32` candidate for absolute value, numeric min/max, and clamp with proposed NaN propagation and signed-zero selection | AOT/JIT and independent-module tests check NaN results, infinities, zero-sign selection and clamp validation order; Windows x64 internal DLL bit-carrier calls check finite values, subnormals, adjacent representations, infinities, signed zeros, and NaN classification; payload/signaling/status effects remain open, and this is not an adopted `Float32Math` API or standard C floating ABI |
| [Float64MathSelection](Float64MathSelection.terrum) | Unapproved matching-width candidate for `float64` absolute value, numeric min/max, and clamp; exact contract remains in [its proposal](../docs/proposals/float64-math-selection.md) | AOT/JIT and independent-module tests check NaN results, infinities, zero-sign selection and clamp validation order; Windows x64 internal DLL bit-carrier calls check finite values, subnormals, adjacent representations, infinities, signed zeros, and NaN classification; payload/signaling/status effects remain open, and this is not an adopted `Float64Math` API or standard C floating ABI |
| [IntegerFloatConversion](IntegerFloatConversion.terrum) | Internal exact raw-bit conversion from signed/unsigned 32-/64-bit integers to `float32` and `float64` | Semantic-module JIT compares all eight conversions with hosted target-format results under both policies, covering signed extrema, both precision-tie directions, sticky bits and deterministic values; the independent `ComponentTestRunner` also executes boundary cases under AOT/JIT and both policies; the Windows x64 internal DLL directly invokes all eight helpers with integer carriers against hosted conversions; this does not imply scalar-frontend or TRRM support |
| [Char16FloatConversion](Char16FloatConversion.terrum) | Internal raw-bit conversion of `char16` as an unsigned UTF-16 code-unit value, composed from the existing `uint32` conversions | Semantic-module JIT exhausts all 65,536 code units for both formats under both policies against independently constructed exact encodings; the independent `ComponentTestRunner` repeats the exhaustive check under AOT/JIT and both policies; hosted native-emitter tests and the Windows x64 internal DLL exhaust the same domain; eligibility and TRRM proposals remain unapproved, with no scalar-frontend or TRRM support implied |
| [FloatingComparison](FloatingComparison.terrum) | Internal raw-bit `float32`/`float64` ordering with IEEE equality, inequality and relational predicates; NaNs are unordered and signed zeros compare equal | Semantic-module JIT compares boundary cross-products and deterministic raw-bit pairs against hosted operators under both policies; the independent `ComponentTestRunner` checks signed zero, infinities and NaNs under AOT/JIT and both policies; the Windows x64 internal DLL directly exercises all twelve predicates with integer carriers against hosted operators; the TRRM comparison proposal remains unapproved, with no scalar-frontend or TRRM support implied |
| [FloatingArithmetic](FloatingArithmetic.terrum) | Internal raw-bit `float32`/`float64` addition, subtraction, multiplication and division with nearest/ties-to-even rounding, gradual underflow and provisional canonical arithmetic NaNs | Semantic-module JIT compares boundary pairs and deterministic raw-bit inputs against the controlled hosted emitter under both policies; the independent `ComponentTestRunner` covers precision ties, subnormal transitions, infinities, NaNs and signed zeros under AOT/JIT and both policies; the Windows x64 internal DLL directly checks all eight raw-bit exports against a controlled hosted-emitter oracle, comparing finite results exactly and NaNs by classification only; arithmetic and NaN-encoding proposals remain unapproved, with no scalar-frontend or TRRM support implied |
| [FloatingFormatConversion](FloatingFormatConversion.terrum) | Internal raw-bit widening and narrowing between `float32` and `float64`; finite narrowing rounds once to nearest/ties-even with gradual underflow and overflow to infinity | Semantic-module JIT compares finite results with hosted conversions under both policies, including all float32 subnormal leading-bit positions, midpoint neighbors, underflow, overflow and NaN classification; the independent `ComponentTestRunner` exercises representative boundaries under AOT/JIT and both policies; the Windows x64 internal DLL compares finite conversions exactly and NaNs by classification only; exact NaN payload preservation is not promised, and no scalar-frontend or TRRM support is implied |
| [FloatingNegation](FloatingNegation.terrum) | Internal raw-bit `float32`/`float64` negation by toggling only the sign bit | Semantic-module JIT compares exact bit results with the sign-bit oracle under both policies, including signed zeros, infinities, noncanonical NaNs and deterministic values; the independent `ComponentTestRunner` also executes representative cases under AOT/JIT and both policies; the Windows x64 internal DLL checks exact sign-bit toggling and involution through integer carriers; the TRRM proposal remains unapproved and no frontend or TRRM support is implied |
| [FloatingIntegerConversion](FloatingIntegerConversion.terrum) | Internal checked conversion from raw `float32`/`float64` bits to signed/unsigned 32-/64-bit integers, truncating toward zero before range checks | Semantic-module JIT compares all eight conversions with hosted behavior under both policies, including boundaries, non-finite values and negative fractions; the independent `ComponentTestRunner` exercises representative boundaries under AOT/JIT and both policies; the Windows x64 internal DLL directly checks all eight status-plus-output exports against checked casts, preserving failed outputs and adjacent canaries; the pair-matrix proposal remains unapproved and no scalar-frontend or TRRM support is implied |
| [CopyAlgorithms](CopyAlgorithms.terrum) | NoThrow selection and conditional `init ref<T>` publication of complete Copy values, with an explicit forwarding `out` adapter and no operator witnesses | Composed AOT/JIT calls select scalars, nested tokens and ranges under both policies; invalid indices publish no value; existing `out` calls remain valid; revision-56 semantic-module JIT also executes the private generic specializations through an exported verifier under both policies; the Windows x64 fail-fast compiler-primitives DLL exercises all three operations through test-only wrappers over concrete Token specializations and also checks `SelectCopy` with the eight-byte SourceRange ABI, including selected fields, success/failure publication and adjacent output canaries; the Terrum `ComponentTestRunner` selects scalar and Token values through producer-local tests; sentinel-filled output storage is only for native write observation and does not relax source vacancy rules; this does not add a public generic ABI; no primitive operator conformance, borrowed views or generic runtime dispatch |
| [IntegerFacts](IntegerFacts.terrum) | Internal `[ConstEval]` eligibility predicates for checked `int32`, `uint32`, `int64` and `uint64` arithmetic, including signed negation and absolute value | Boundary predicates are proven in required static expressions and executed under both native policies; native executables compare all 24 predicates with independent `BigInteger` range, divisor and signed-minimum expectations under both policies; revision-55 persisted semantic-module JIT also compares 362 signed/unsigned boundary pairs with independent `BigInteger` expectations under both policies; the Windows x64 fail-fast compiler-primitives DLL exercises all 24 predicates through test-only exports against `BigInteger` boundary oracles; 32-bit signed checks widen safely, 64-bit checks rearrange bounds without overflowing, zero divisors reject, and signed-minimum remainder by `-1` remains valid; no public arithmetic API or compiler bootstrap |
| [WideIntegerFacts](WideIntegerFacts.terrum) | Checked signed and unsigned 64-bit addition, subtraction, multiplication, division and remainder, signed negation and absolute value with conditional `init ref` publication, total unsigned signed-magnitude extraction, plus bounded decimal/radix-2..36 accumulation through the full unsigned range | Executed as separately compiled modules with vacancy-only `init ref` outputs and accumulation under both policies; revision-56 persisted semantic-module JIT checks all ten binary helpers, both signed unary helpers and `MagnitudeInt64` against `BigInteger` across 935 boundary cases under both policies, and also checks decimal/all-radix accumulators against an independent arithmetic oracle across the unsigned range limits under both policies; Windows x64 fail-fast compiler-primitives DLL directly invokes all twelve checked arithmetic helpers, `MagnitudeInt64` and both accumulator functions against BigInteger boundary expectations; division by zero, signed-minimum quotient overflow, negation and absolute value do not publish; invalid accumulation leaves outputs unpublished; magnitude includes the signed minimum |
| [WideIntegerTests](WideIntegerTests.terrum) | Signed/unsigned arithmetic extrema, signed-minimum quotient overflow versus valid zero remainder, zero-divisor rejection, truncating signed division/remainder, invalid digits/radices, per-radix unsigned/signed limits and conditional output reuse | Executed through AOT and JIT with a shared entry wrapper under both failure policies |
| [IntegerCheckedArithmetic](IntegerCheckedArithmetic.terrum) | Checked `int32`/`uint32` addition, subtraction, multiplication, division and remainder, plus signed negation and absolute value with conditional `init ref<T>` publication and total unsigned signed-magnitude extraction | Independent semantic-module producer/consumer with AOT/JIT and both failure policies; revision-56 persisted semantic-module JIT checks all ten binary helpers, both signed unary helpers and `MagnitudeInt32` against `BigInteger` across 935 boundary cases under both policies, with negative compile checks for failed output reads; the Terrum `ComponentTestRunner` also checks signed/unsigned extrema, division failures, signed-minimum remainder and magnitude; zero divisors, signed-minimum quotient overflow, negation and absolute value do not publish, while signed-minimum remainder by `-1` is zero and magnitude includes it; the internal Windows x64 fail-fast DLL directly invokes all thirteen helpers with BigInteger boundary oracles, exact status/output-preservation checks and adjacent canaries; these are named library functions, not new operator syntax |
| [IntegerFormatting](IntegerFormatting.terrum) | Invariant signed/unsigned 32-/64-bit and `char16` code-unit decimal and explicit radix-2..36 digits (lowercase or uppercase) in reusable owned storage, with vacancy-only radix length queries for exact allocation; shares signed-magnitude extraction with `WideIntegerFacts.MagnitudeInt64` | Negative signs join the reversed digit buffer before its single reversal; AOT/JIT per-radix length, exact-capacity and repeated-use fixtures; native heap checks cover same-allocation transfer and failure cleanup; revision-56 semantic-module JIT compares decimal and all-radix formatter output with host-side expected snapshots under both policies, keeping unique workspaces local to the producer; the internal Windows x64 fail-fast DLL directly invokes all five radix-length queries for every radix 2..36 against an independent `BigInteger` digit-count oracle, checking invalid-radix preservation and adjacent canaries, and all fifteen decimal/radix formatters against hosted decimal and independent `BigInteger` digit oracles through exact-capacity and reused workspaces; test-only Terrum helpers keep owner allocation and consumption internal; no external owner protocol or stable ABI |
| [IntegerParsing](IntegerParsing.terrum) | Strict signed/unsigned radix-2..36 ASCII parsing of an exact UTF-16 source slice, with hosted-compatible digit separators for radix 2, 10 and 16, and conditional, vacancy-only `init ref` outputs for 32-/64-bit integers and unsigned `char16` code units; shares signed-minimum magnitude extraction with `WideIntegerFacts.MagnitudeInt64` | Signed parsing checks the initial sign position before delegating digit-slice bounds to magnitude parsing; AOT/JIT under both policies; full-range per-radix boundaries, separators, invalid spelling/bounds and unpublished outputs; revision-56 semantic-module JIT compares all five parser APIs against the host-side oracle across radices 2–36 and fresh grants under both policies, including maximum-length all-zero success and overflowing digit inputs for every radix; separate `SourceIntegerParsing` source-grant exports pair scalar validity queries with typed value queries, preserving the parser oracle and requiring identical arguments and an unchanged grant |
| [SourceIntegerParsing](SourceIntegerParsing.terrum) | Isolated scalar-only source-grant parser queries: `CanParse...Base` returns validity and `Parse...Base` returns a typed value or fails through `require` for each existing integer parser type | Direct source-grant DLL tests compare all five pairs with the independent `BigInteger` oracle across radices 2–36 under both policies, including exact slices, sign/separator rules, destination limits, malformed UTF-16, extreme arguments and maximum grants; checked value-query failures are direct and fail-fast failures run in subprocesses; the maintained companion binds the pairs through its image-hash manifest; paired calls scan twice and require one unchanged grant |
| [IntegerCheckedConversions](IntegerCheckedConversions.terrum) | All twelve cross-type 32-/64-bit integer conversions, four fallible `char16` narrowings and four always-successful `char16`-to-integer widenings | Independent semantic-module producer/consumer with AOT/JIT under both policies; revision-56 persisted semantic-module JIT checks all 20 helpers against independent `BigInteger` representability/output expectations under both policies and exhausts all 65,536 inputs for each widening; failed narrowing leaves `init ref` vacant while widening publishes any code unit, including surrogates, without Unicode scalar validation; the internal Windows x64 fail-fast DLL directly invokes all twenty helpers with typed `BigInteger` boundary oracles, exact status/output preservation and guarded canaries, plus all code units for each widening; the Terrum `ComponentTestRunner` checks representative success/failure boundaries and exhausts each widening under both policies; named alternatives to failing checked type-calls, not new syntax or conversion witnesses |
| [IntegerSaturation](IntegerSaturation.terrum) | All twelve cross-type 32-/64-bit integer conversions, four `char16` narrowings that clamp, and four value-preserving `char16` widenings | AOT/JIT range and code-unit boundaries plus an independent compiled-module consumer under both policies; revision-56 persisted semantic-module JIT compares all 20 helpers against a `BigInteger` clamp oracle at source/destination boundaries and code-unit edges, including surrogates, under both policies, and exhausts all 65,536 inputs for each widening; the internal Windows x64 fail-fast DLL directly invokes all twenty helpers against the same independent clamp oracle, plus all code units for each widening; the Terrum `ComponentTestRunner` exercises representative boundaries for all 20 helpers and exhausts each widening under both policies; these named functions remain provisional, not general conversion syntax or witnesses |
| [IntegerWrapping](IntegerWrapping.terrum) | All twelve cross-type signed/unsigned 32- and 64-bit conversions, four low-bit `char16` narrowings and four value-preserving `char16` widenings | AOT/JIT host-oracle cases and an independent compiled-module consumer under both policies; revision-56 persisted semantic-module JIT compares all 20 helpers against a `BigInteger` modulo/two's-complement oracle across source/destination boundaries and code-unit edges, including surrogates, under both policies; the independent consumer checks all 65,536 `char16` values for each widening under both policies; the internal Windows x64 fail-fast DLL checks all twenty helpers and exhausts each widening against hosted C# numeric conversions; the Terrum `ComponentTestRunner` exercises representative boundaries plus every code unit for all four widenings under both policies; these candidates do not add `unchecked` syntax or a primitive conversion policy |
| [SourceRange](SourceRange.terrum) | Checked UTF-16 range construction without invalid output payloads | Executed with private construction and vacancy-only `init ref` publication through independent module consumers; Windows x64 compiler-primitives DLL integration compares boundaries against hosted `TextSpan`, checks successful two-word publication, preserved false-path storage and adjacent canaries, and directly invokes both `GetEnd` and the `End` getter on valid ranges; the internal layout is not a stable or portable ABI |
| [SourceText](SourceText.terrum) | Stateless, invocation-scoped queries over immutable `Host.Source`: exact UTF-16 length/code-unit reads, source-bounded half-open ranges, and streaming line reads | Direct AOT/JIT under both policies compare all code units and line extents, including unpaired surrogates and NUL; revision-56 semantic-module JIT reuses persisted symbolic reads with fresh grants; invalid reads/ranges/line starts publish no output; does not own source, carry a path, return strings, or replace the hosted `SourceText` collection |
| [Utf16CodeUnits](Utf16CodeUnits.terrum) | Source-independent UTF-16 code-unit pair decoding and scalar width | Persisted semantic-module producer/consumer checks BMP/NUL/noncharacters, valid and invalid surrogate pairs, and the established width behavior; Windows x64 integration checks the compiler-primitives decoder for every BMP code unit, all 1,048,576 valid supplementary pairs and all 2,048 isolated surrogates, and checks scalar width for every valid Unicode scalar; the Terrum `ComponentTestRunner` exhaustively checks every BMP code unit and every supplementary scalar pair, plus unpaired surrogates; not a source reader or frontend |
| [Utf16Scalar](Utf16Scalar.terrum) | Decode one in-range UTF-16 source position to a Unicode scalar or `-1` for an unpaired surrogate; return its code-unit width | `ReadUtf16Scalar` remains an internal caller-bounded adapter; `ReadUtf16ScalarChecked` validates its half-open source range with the existing `require` policy and delegates without reading past `end`; shared by scanner, managed-token validation, character-literal, string-escape and validated string-literal decoding; revision-56 semantic-module JIT checks all BMP code units at one-unit bounds and every valid supplementary pair across fresh grants under both policies; the maintained build emits a separate provisional Windows x64 source-grant companion for this checked scalar export plus the unchanged source-free decoder; callers retain their own diagnostics and token/literal boundaries |
| [CharacterLiteral](CharacterLiteral.terrum) | Decode source-ranged single-quoted literals into one Unicode scalar or the hosted lexical diagnostic code/span | Reuses `CharacterFacts.TryGetHexDigit` for ASCII escape digits; the scalar frontend consumes successful scalars, the scanner consumes diagnostic-aware results after finding token boundaries, and the revision-56 semantic-module JIT reuses the decoder across fresh source grants; host-oracle fixtures cover simple escapes, Unicode scalar boundaries, malformed escapes, empty and multiple-scalar spellings under both policies, every scanner-delimited BMP code unit, every BMP `\u` escape including all 2,048 surrogate-valued rejections, every valid supplementary scalar in both direct and `\U`-escaped literals, and rejection of all 2,048 surrogate-valued `\U` escapes; token termination remains the scanner's responsibility, while the host reader independently validates source spelling and the scanner/frontend remain hosted-verifier/emitter-backed |
| [CharacterLiteralPacked](CharacterLiteralPacked.terrum) | Isolated scalar-only source-grant query that packs character-literal success or diagnostic code and spelling-relative UTF-16 span into `uint32` | Direct DLL and semantic-module tests compare decoded fields with aggregate `CharacterLiteralDecodeResult.TryAnalyze` under both policies, including nonzero spelling offsets, NUL, raw quote/newline, escaped apostrophe, invalid and truncated escapes, surrogate behavior, second-scalar/error precedence and maximum grants; false differs from successful NUL and zero-length diagnostics, full-range spans use unsigned bit packing, and the encoding remains provisional without changing the source-free DLL or hosted scanner/frontend boundary |
| [CharacterLiteralScalar](CharacterLiteralScalar.terrum) | Scalar-only decoded character-literal query returning the Unicode scalar or `-1` for false/diagnostic results | Reuses `AnalyzeCharacterLiteralPacked` and its success tag rather than duplicating parsing; direct DLL and persisted semantic-module tests compare with aggregate `CharacterLiteralDecodeResult.TryAnalyze` under both policies, including NUL, supplementary values, malformed spellings, invalid bounds and maximum grants; detailed errors remain on the packed query, and this provisional export does not change the source-free DLL or hosted scanner/frontend boundary |
| [StringEscape](StringEscape.terrum) | Decode one string escape and report its scalar, consumed width, and hosted diagnostic boundary | Shares `CharacterFacts.TryGetHexDigit` with character literals; used by validated `StringLiteral` decoding and scanner lexical diagnostics; escaped unpaired UTF-16 retains its separate diagnostic; revision-56 semantic-module JIT compares scalar, consumed width and hosted diagnostic span across fresh grants under both policies; host-oracle cases cover simple escapes, Unicode scalar boundaries, malformed escape/hex forms, apostrophe rejection and consumption before trailing source text, every scanner-delimited BMP escape introducer, every BMP `\u` value including all 2,048 rejected surrogate-valued escapes, every valid supplementary `\U` escape, and all 2,048 rejected surrogate-valued `\U` escapes |
| [HexDigitValue](HexDigitValue.terrum) | Source-free scalar hexadecimal-digit mapping that returns `-1` for non-hexadecimal UTF-16 units | Shared only by the isolated packed character/string escape source-grant components, avoiding output-parameter closures and leaving `CharacterFacts.TryGetHexDigit` in the source-free DLL unchanged |
| [StringEscapePacked](StringEscapePacked.terrum) | Isolated scalar-only packed source-grant query for `StringEscapeDecodeResult.TryAnalyze`, with zero for a rejected spelling and scalar, consumed width and error marker encoded in `uint32` | Direct source-grant DLL tests cover successful simple/Unicode escapes, malformed and truncated forms, scalar limits, paired/unpaired surrogates, trailing text, backslash-before-line-break direct analysis and maximum grants under both policies; semantic-module tests compare the packed fields against the aggregate analyzer across the existing hosted diagnostic corpus and exhaustive BMP/supplementary escape coverage; its bit layout depends on the current result domain and remains provisional, does not change the classifier or source-free DLL, and is not a scanner/frontend support claim |
| [SourceLines](SourceLines.terrum) | Streaming line ranges, line extents and bidirectional UTF-16 offset/line-position mapping over an immutable source grant | Direct AOT/JIT vacancy-only `init ref` outputs under both policies; revision-56 semantic-module JIT compares persisted full extents, positions and offsets with the hosted snapshot across fresh grants under both policies; invalid queries leave all outputs vacant, and CRLF, every supported line break, EOF and malformed surrogate offsets agree with the host snapshot |
| [SourceLineCount](SourceLineCount.terrum) | Scalar-only count of physical lines in the current immutable UTF-16 source grant, reusing shared line-break classification | Direct source-grant DLL tests compare with hosted `SourceText.Lines.Length` for empty, trailing-break, CRLF, Unicode line-break, NUL, malformed-surrogate and maximum-length inputs under both policies; the maintained fail-fast companion also binds the export and checks its edge and maximum-length behavior; this does not enable aggregate-output call closures or the complete `SourceLines` facade |
| [SourceLineEnd](SourceLineEnd.terrum) | Scalar queries for the exclusive ends of a zero-based line's content and including-break extents, or `-1` for an invalid line | Direct source-grant DLL tests reconstruct both hosted `SourceText.Lines` extents with the existing scalar line-start offset under both policies; they cover empty/final lines, all line-break kinds, every UTF-16 code unit after CR and maximum grants; the maintained fail-fast companion binds both exports through its image-hash manifest; each query scans linearly, so this is not indexed extent lookup or the complete `SourceLines` facade |
| [SourceRangeValidation](SourceRangeValidation.terrum) | Scalar `CanGetSourceRange` predicate for source-bounded half-open UTF-16 ranges, including zero-length EOF ranges | Direct and maintained source-grant tests compare the Boolean result with hosted `SourceText.GetSubText(TextSpan)` across negative, overflow, source-boundary and maximum-grant cases under both policies; does not export aggregate `SourceRange` values or validate raw grant preconditions |
| [SourceTextLength](SourceTextLength.terrum) | Scalar source-grant adapter for the existing `SourceText.GetSourceTextLength` contract | Direct source-grant DLL tests compare with hosted `SourceText.Length` for empty, edge and maximum-length grants under both policies; the maintained fail-fast companion binds the export through its image-hash manifest; isolated from the complete `SourceText` module because classifier checks include unselected source-dependent functions |
| [SourceCodeUnit](SourceCodeUnit.terrum) | Isolated scalar query for one raw UTF-16 source code unit, returning `-1` for an out-of-range position | Direct tests exhaust all 65,536 code-unit values under both policies and compare edge/maximum grants with hosted `SourceText`; the maintained fail-fast companion binds the export through its image-hash manifest; callers check the sentinel before conversion, and this does not support the complete `SourceText` facade |
| [SourceOffset](SourceOffset.terrum) | Isolated scalar line/character-to-source-offset query, returning `-1` for invalid pairs | Direct tests compare every short-fixture line/character boundary with hosted `SourceText.TryGetOffset` under both policies and exercise maximum plain, LF, CRLF and trailing-CR grants; the maintained fail-fast companion binds the export through its manifest; aggregate `SourceRange` outputs and the complete `SourceLines` facade remain unsupported |
| [SourcePosition](SourcePosition.terrum) | Two isolated scalar queries for the hosted line number and UTF-16 character position of a source offset, returning `-1` for invalid offsets | Direct tests compare every short-fixture offset and maximum-grant boundaries with hosted `SourceText.GetLinePosition` under both policies and round-trip through `GetSourceOffsetOrMinusOne`; the maintained fail-fast companion binds the exports through its manifest; both CRLF code units stay on the preceding line; each query scans independently and must use the same snapshot when combined; aggregate `LinePosition` and the complete `SourceLines` facade remain unsupported |
| [SourceLineIndex](SourceLineIndex.terrum) | Owned one-word-per-line table; the sign bit of a next-line offset records a preceding CRLF, alongside random-access extents and bidirectional UTF-16 offset/line-position lookup | Executed as AOT/JIT under both failure policies; vacancy-only `init ref` construction and lookups, exact offset round-trips, every hosted line-break form including trailing-break empty lines and the all-break source fast path, CRLF marker boundaries, source-mutation-stable extents, unpublished invalid outputs and owner-copy rejection; revision-56 semantic-module JIT compares line counts, content/full-break extents, position mappings and offset lookups with the hosted snapshot across fresh grants under both policies, including every line and code-unit position of a maximum-length all-CRLF source |
| [SourceLineTests](SourceLineTests.terrum) | Shared line-range/position fixtures, invalid offsets and repeated queries | Executed natively with host-supplied expected records; no source normalization or default output values |
| [StringLiteral](StringLiteral.terrum) | Validated quoted spelling and scalar decoding from the immutable UTF-16 source grant, with explicit ASCII, ISO-8859-1 (Latin-1), UTF-8, UTF-16 unit/UTF-16LE and BE byte, and UTF-32LE and BE byte length measurement and encoding into owned storage | Executed with vacancy-only `init ref` construction and scalar/cursor reads, all supported escapes and exact source boundaries; cursor queries reject positions inside escape spellings and surrogate pairs and report the exact next scalar boundary; revision-56 semantic-module JIT reuses the leaf decoder across fresh grants and checks every scanner-delimited BMP code unit, every BMP `\u` escape including all 2,048 rejected surrogate-valued escapes, every valid supplementary scalar in both direct and `\U`-escaped spellings, scalar-read rejection at every interior offset in valid spellings, sequential cursor scalar/next-offset parity for every valid scalar spelling, and all 2,048 invalid surrogate-valued `\U` escapes; encoding lengths and output match host encoders for all supported formats under both runtime policies for every valid Unicode scalar, including strict ASCII/Latin-1 rejection and successful encoding across all ASCII and Latin-1 scalar values; a maximum-length quoted ASCII source checks exact-capacity output for all eight encodings, including UTF-32 byte output, and a maximum-length U+0800 source checks exact-capacity 49,146-byte UTF-8 output; ordinary non-surrogate source units decode directly while surrogate pairs use the shared UTF-16 decoder; single-byte encoders reject unrepresentable scalars rather than substitute, while UTF bytes/units agree with host encoders without a byte-order mark; Latin-1 is not a StringRow selector or default runtime representation |
| [StringLiteralScalarCount](StringLiteralScalarCount.terrum) | Isolated scalar source-grant validation/count query for an exact quoted UTF-16 spelling, returning `-1` on `StringLiteral.TryCreate` failure and scalar count otherwise | Direct DLL cases compare with hosted lexer validity; semantic-module tests compare count and failure with aggregate `StringLiteral.TryCreate` across exhaustive single-scalar inputs plus empty, NUL, mixed/multi-scalar, quote/backslash, raw/escaped linebreak and surrogate cases; it uses packed escape success/consumed-width fields, does not expose a literal, cursor or encoding operation, and does not widen the classifier, alter the source-free DLL or establish scanner/frontend support |
| [StringLiteralScalarAt](StringLiteralScalarAt.terrum) | Isolated scalar lookup by zero-based decoded-scalar index, returning the value or `-1` for invalid spelling/index | Validates the complete slice through `StringLiteralScalarCount` before rescanning to the selected scalar; direct DLL validity matches one exact hosted lexer token and semantic-module values match aggregate `StringLiteral.TryReadScalar`, including malformed suffixes, exhaustive single scalars, supplementary values, U+0000/U+FFFF and maximum grants; each lookup is linear and enumerating all indexes is quadratic, so this is not a cursor replacement; the query does not widen the classifier, alter the source-free DLL or establish scanner/frontend support |
| [StringLiteralEncodedLength](StringLiteralEncodedLength.terrum) | Isolated scalar-only length query for strict ASCII/Latin-1 bytes, UTF-8 bytes, UTF-16 code units/bytes and UTF-32 bytes, selected by provisional selectors 0–5; `-1` means invalid spelling/selector or unrepresentable single-byte scalar | Count validation precedes one measurement scan; direct DLL lengths match strict host encoders and exact hosted-lexer validity, including maximum-grant UTF-8 length of 49,146 bytes for U+0800 and the maximum Latin-1 grant, while semantic-module results match all existing aggregate length methods across the exhaustive scalar corpus and maximum grants; no BOM/terminator is counted, endianness does not affect byte length, and the query does not emit buffers, widen the classifier, alter the source-free DLL or establish scanner/frontend support |
| `GetStringNameRowByteLengthOrMinusOne` | Isolated scalar-only measurement of a validated nonempty UTF-8 `StringNameRow`, returning its header/content/terminator/four-byte-aligned total size or `-1` for invalid spelling, empty text, embedded NUL or an unrepresentable row | Reuses the strict UTF-8 scalar scan, including escape and surrogate validation; direct DLL comparisons use host UTF-8 lengths and the existing row framing rules across empty/NUL cases, exact slices, malformed spellings and maximum grants; reports measurement only, allocates/emits no row, and does not implement registered target binding or widen the classifier/source-free DLL |
| `GetStringNamespaceRowByteLengthOrMinusOne` | Isolated scalar-only measurement of a validated UTF-8 `StringNamespaceRow`, returning the same framed byte length or `-1` for invalid spelling, embedded NUL or an unrepresentable row; empty text measures eight bytes | Reuses strict UTF-8 scalar validation/measurement and the existing row framing; direct DLL comparisons use the hosted namespace-row sizing and writer across empty/NUL cases, exact slices, malformed spellings and maximum grants; reports measurement only, allocates/emits no row, and does not widen the classifier/source-free DLL or implement registered target binding |
| [StringRow](StringRow.terrum) | Provisional tagged ASCII/UTF-8/UTF-16LE/UTF-32LE String-row measurement and emission from validated literal scalars into reusable owned byte words, with nonempty metadata-name and empty-permitted namespace paths that both reject embedded zero scalars, plus ordinal decoded-scalar name and namespace comparison | Name/namespace validation shares the encoding-size pass, comparison validates while comparing, emission reuses measured extents, and power-of-two header/alignment fields use shifts and masks; UTF-8 name and namespace row builders revalidate the exact source range against the current `Host.Source` invocation, then preflight text and workspace capacity before initializing output with a complete row, retaining the name-only nonempty rule; AOT/JIT fixtures check exact unit counts, encoding-width terminators, four-byte alignment, zero padding, invalid flags, rejected name/namespace text, escape-equivalent spelling, byte parity with the host String-row writer and checked undersized cleanup; revision-56 semantic-module JIT compares UTF-8/UTF-16LE/UTF-32LE name and namespace rows with the host writer for every nonzero Unicode scalar in bounded batches, using the success-only builders for UTF-8 and the existing encoders for other formats, exhaustively checking the ASCII range and rejecting non-ASCII contents in ASCII batches, and compares name/namespace equality with hosted ordinal string equality for every nonzero Unicode scalar using case-varied `\U` spellings; empty-name and embedded-zero rejection remain covered; this component remains a candidate, not a TRRM section writer, reader, or literal-reference contract |
| [StringRowLayout](StringRowLayout.terrum) | Structural validation of one indexed TRRM String row's byte-word range, assigned encoding flags, count-derived extent, zero terminator and four-byte padding; returns only encoding and content byte length | AOT/JIT under both runtime policies compare physical-shape acceptance and outputs with independent fixtures anchored to the hosted row reader; malformed encoded text can have valid physical shape and remains the reference decoder's responsibility; it does not decode or allocate text, enforce name-versus-namespace policy, or replace `TrRmStringRowReader` |
| [StringRowText](StringRowText.terrum) | Candidate strict, allocation-free content validation and scalar access layered on `StringRowLayout`; counts Unicode scalars and reads by zero-based scalar index for name/namespace rows | AOT/JIT under both policies compare all scalar reads with hosted decoded names/namespaces across ASCII, UTF-8, UTF-16LE and UTF-32LE; strict malformed-encoding, embedded-NUL, malformed-suffix, empty-name/namespace, terminator, padding and bounds cases reject; every query validates the complete selected row, so repeated indexed reads rescan it; no normalization, grammar, or string allocation |
| [StringTableIndex](StringTableIndex.terrum) | Candidate validation of a TRRM String variable-table offset index using [VariableTableIndex](VariableTableIndex.terrum), then each row's physical layout; returns the requested row's data-relative start, length, encoding and content byte length | AOT/JIT under both policies compare valid rows with the hosted variable-table and String-row writers, and reject invalid counts, byte words, offsets, extents, alignment, and physical row shapes; malformed UTF-8 with valid framing remains accepted as physical layout; each lookup rescans and validates every table row, and does not decode text or replace the hosted readers |
| [StringTableText](StringTableText.terrum) | Candidate selected-row name/namespace scalar counts and zero-based reads composed from the variable-table index and `StringRowText` | AOT/JIT under both policies compare with `TrRmStringTableReader` for valid/empty rows and malformed selected rows; the complete offset index is checked, but unselected row text and physical shape are not, matching hosted selected-row decoding; malformed selected suffixes publish no output |
| [TypeSpecTableValidation](TypeSpecTableValidation.terrum) | Candidate structural validation of a complete TRRM TypeSpec variable table, including exact row forms/lengths, flags, reference bounds and canonical inline/indexed family forms | AOT/JIT under both policies compare valid and malformed tables with `TrRmTypeSpecTableReader`; generic TypeArg ownership, arity, constraints and execution remain outside this structural reader, and provisional family code `0x89` has no resolved executable meaning |
| [TypeArgTableValidation](TypeArgTableValidation.terrum) | Candidate fixed-row TypeArg ownership and range validation composed with `TypeSpecTableValidation` | AOT/JIT under both policies compare with `TrRmTypeArgTableReader`; checks constructed owners, row ordinals, earlier TypeSpec arguments and exact partitioning of the table by declared ranges; the reader entry point rejects an empty table and does not establish generic arity, constraints or execution |
| [MethodSigParamTableValidation](MethodSigParamTableValidation.terrum) | Candidate structural validation of MethodSig and Param tables composed with `TypeSpecTableValidation` | AOT/JIT under both policies compare with `TrRmMethodSigTableReader` and `TrRmParamTableReader`; checks Param range partitioning, row ownership/ordinal, type references, modes and reserved fields; the entry point requires a nonempty MethodSig table but permits zero Params; generic ranges are bounds-only as in the hosted MethodSig reader, and generic ownership/arity/constraints remain separate |
| [ModuleTableValidation](ModuleTableValidation.terrum) | Candidate structural validation of fixed Module rows, cross-table cursors, Feature ranges, entry-method ownership and unique decoded identity names using caller-provided writable `IdentitySlots` of exactly `recordCount + 1` words (slot zero unused; contents not preserved) | AOT/JIT under both policies compare with `TrRmModuleTableReader`; deterministic heapsort groups strict decoded scalar-name equality without hash-collision behavior, including duplicate spellings across String encodings; validates the complete String index but only referenced identity rows, so malformed unreferenced row contents remain accepted as in the hosted reader; module linking/execution and replacement of the hosted reader are not implied |
| [StringLiteralTests](StringLiteralTests.terrum) | Quoted-form validation, scalar values and rejection of offsets inside escapes or surrogate pairs | Shared host-lexer/compiled fixtures under both native runtime policies; failed calls publish no output |
| [VariableTableIndex](VariableTableIndex.terrum) | Candidate generic TRRM variable-table index validation for one- or four-byte row alignment and a declared minimum row extent; returns the requested row's data-relative start and length | AOT/JIT under both policies compare Blob and Body row extents with hosted readers, including empty Blob rows; malformed count, index, byte word, minimum-size, offset, alignment and row-bound cases reject; row contents are uninterpreted and each query rescans the full index |
| [CharacterFacts](CharacterFacts.terrum) | Shared ASCII digit, hex-digit and newline decisions | Executed with `char16` comparisons, checked numeric type-call conversions and verified NoThrow classification contracts; revision-56 semantic-module JIT checks ASCII-digit, hex-digit and line-break behavior across every UTF-16 code unit, including successful digit publication and failed-output vacancy/reuse, and verifies `GetDecimalDigit` for all ten digits under both policies; Windows x64 compiler-primitives DLL integration invokes every export, exhaustively checks digit/newline classification, and checks decimal/hexadecimal mapping, rejection, output preservation and canaries; the Terrum `ComponentTestRunner` now exhaustively checks all three classifications/mappings over UTF-16 code units |
| [Token](Token.terrum) | Token category and nested source range | Executed as a 12-byte aggregate with independent producer/consumer artifacts |
| [ComponentTestRunner](ComponentTestRunner.terrum) | Explicit entry harness composing bounded range, character-fact, UTF-16, IntegerFacts, token, CopyAlgorithms, finite float math selection, wide and checked integer arithmetic, IntegerFormatting, IntegerFloatConversion, Char16FloatConversion, FloatingArithmetic, FloatingComparison, FloatingFormatConversion, FloatingNegation, FloatingIntegerConversion, IntegerMath, IntegerSaturation, IntegerWrapping, checked-conversion, and bit-operation tests | Persisted consumer executes the independently compiled producer through both JIT and AOT under both runtime policies; char16 conversion exhausts every code unit, floating addition/subtraction/multiplication/division covers rounding/subnormal/special-value boundaries, floating comparisons cover signed zeros and unordered NaNs, float-format checks cover subnormal, overflow and NaN-classification boundaries, and negation checks preserve exact raw payloads; test selection remains explicit and host-orchestrated |
| [TokenCursor](TokenCursor.terrum) | Read-only traversal and repeatable indexed reads of a validated `TokenSnapshot` without copying or owning its backing; indexed reads do not advance the remaining view | Hosted binding, mandatory verification and Windows x64 emission support only `Span<Token>`; `uint64` is the internal unsigned native-width index carrier, with full-width bounds checks before address calculation. `[Captures(nameof(tokens))]` preserves the owner dependency through copies, moves, joins and unwind. The scalar frontend passes the unadvanced snapshot view through parser methods and reads bounded indexed windows without storing the view. AOT/JIT traverses every token in a maximum-length scanner snapshot under both policies while the owner remains live. No source-level `nuint`, mutable/writable spans, general slicing, arbitrary stored views, exports or persistence; see [the bounded cursor proposal](../docs/proposals/span-backed-token-cursor.md#implemented-bounded-indexed-parser-integration). |
| [RangeTests](RangeTests.terrum) | Small success/failure and overflow boundary cases | Executed by `ComponentTestRunner` alongside `TokenTests` against an independent multi-source producer; C# still owns test discovery and execution orchestration |
| [CharacterFactsTests](CharacterFactsTests.terrum) | Decimal digits, exhaustive ASCII-digit/line-break/hex classification and mapping across all UTF-16 code units | Executed by the explicitly composed `ComponentTestRunner` against the independent multi-source producer |
| [Utf16CodeUnitsTests](Utf16CodeUnitsTests.terrum) | Exhaustive BMP decoding/width, every valid supplementary surrogate pair, and unpaired-surrogate rejection | Executed by the explicitly composed `ComponentTestRunner` against the independent multi-source producer |
| [IntegerFactsTests](IntegerFactsTests.terrum) | Representative signed/unsigned overflow, division and remainder eligibility across 32- and 64-bit widths | Executed by the explicitly composed `ComponentTestRunner` against test-only mask exports; internal `[ConstEval]` predicates remain non-exported candidate helpers |
| [IntegerFactsTestExports](IntegerFactsTestExports.terrum) | Test-only mask wrappers for exercising internal `IntegerFacts` predicates across the module boundary | Included only in the composed test producer; does not change candidate visibility or compiler-primitives exports |
| [CopyAlgorithmsTests](CopyAlgorithmsTests.terrum) | Scalar, bool, `char16`, standalone `SourceRange` and nested Token selection; aggregate `SourceRange` success/failure publication through `init ref<T>` and `out`; existing-destination `out` reuse after a failed call | Executes in the producer and exports only its boolean test wrapper; generic candidate functions and aggregate values do not cross the module boundary |
| [FloatMathSelectionTests](FloatMathSelectionTests.terrum) | Finite `float32`/`float64` absolute value, min/max, and clamp boundaries | Executes in the producer and exports only its boolean test wrapper; NaN, signed-zero selection, payload, signaling, status behavior, and candidate API approval remain outside this coverage |
| [ManagedTokenChainTests](ManagedTokenChainTests.terrum) | Bounded capacity, append validation, indexed token reads, cursor lookahead/consumption and mutation invalidation; an empty-chain cursor starts current and is invalidated by append, a rejected full-chain append preserves count, version, the last token and cursor validity, and clear invalidates its cursor and reclaims all 64 links | Executes inside the producer and exports only a boolean test result; owners, links, cursors, and copied tokens remain producer-local, with no general owning-graph claim |
| [IntegerFormattingTests](IntegerFormattingTests.terrum) | Signed-minimum radix formatting, uppercase hex digit mapping, decimal output, invalid-radix length-output vacancy/reuse, and workspace reuse | Runs inside the producer and exports only its boolean test wrapper, keeping the unique workspace local; formatter candidates remain unapproved |
| [IntegerFloatConversionTests](IntegerFloatConversionTests.terrum) | Signed/unsigned conversion extrema, nearest-even tie directions, and float32/float64 sticky-bit boundaries | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [Char16FloatConversionTests](Char16FloatConversionTests.terrum) | Exhaustive float32/float64 bit encoding for all 65,536 unsigned `char16` code units | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [FloatingComparisonTests](FloatingComparisonTests.terrum) | Signed-zero equality/order, infinities, and unordered quiet/signaling NaN comparisons for both widths | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [FloatingArithmeticTests](FloatingArithmeticTests.terrum) | Addition/subtraction/multiplication/division precision ties, subnormal transitions, sticky rounding, signed zeros, infinities and canonical NaNs for both widths | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [FloatingFormatConversionTests](FloatingFormatConversionTests.terrum) | Exact float32 widening, float32 subnormal normalization, nearest-even narrowing midpoints, gradual underflow, overflow and NaN classification | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [FloatingNegationTests](FloatingNegationTests.terrum) | Signed zeros, infinities, subnormals and noncanonical NaN payloads for both format widths | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [FloatingIntegerConversionTests](FloatingIntegerConversionTests.terrum) | Representative float32/float64 signed and unsigned integer boundaries, truncation of negative fractions, and non-finite rejection | Executes inside the independent producer/consumer `ComponentTestRunner` under both runtime policies and AOT/JIT |
| [IntegerMathTests](IntegerMathTests.terrum) | Representative signed extrema, conditional absolute-value failure, widened `int32` multiplication, clamp, and full-magnitude GCD cases | Executed through the independent multi-source producer/consumer by the explicitly composed `ComponentTestRunner`; candidate API remains unapproved |
| [IntegerSaturationTests](IntegerSaturationTests.terrum) | Representative signed/unsigned conversion limits and `char16` clamping; exhaustive widening parity for every code unit | Executed through the independent multi-source producer/consumer by the explicitly composed `ComponentTestRunner`; candidate API remains unapproved |
| [IntegerWrappingTests](IntegerWrappingTests.terrum) | Representative two's-complement width changes, low-bit narrowing, and every `char16`-to-integer widening input | Executed through the independent multi-source producer/consumer by the explicitly composed `ComponentTestRunner` under both runtime policies; candidate API remains unapproved |
| [IntegerCheckedConversionsTests](IntegerCheckedConversionsTests.terrum) | Representative checked 32-/64-bit integer conversions and failed `init ref` vacancy/reuse; exhaustive successful widening parity for every `char16` code unit | Executed through the independent multi-source producer/consumer by the explicitly composed `ComponentTestRunner`; candidate helpers remain unapproved |
| [BitOperationsTests](BitOperationsTests.terrum) | Representative `uint32`/`uint64` zero counts, population counts, rotations, reversals, power-of-two checks, and round-up failure/vacancy reuse at width boundaries | Executed through the independent multi-source producer/consumer by the explicitly composed `ComponentTestRunner`; candidate API remains unapproved |
| [TokenTests](TokenTests.terrum) | Nested value copies and reconstruction with a vacancy-only initialized replacement range | Executed through the Windows indirect aggregate ABI; token and range fields stay read-only; no retained backing views |
| [ManagedTokenChain](ManagedTokenChain.terrum) | Bounded linked storage for complete Copy tokens and vacancy-only validated source ranges, with a sequential managed cursor, non-consuming indexed lookahead and conditional kind consumption | Matching-kind reads check version and consume one node in a single read; AOT/JIT fixtures under both policies cover 64-link capacity, count/version changes, vacancy-only `init ref` indexed lookup/lookahead/sequential reads, public `out` and internal `init ref` cursor creation, mismatch does not advance, mutation invalidation and collection after clear; revision-56 semantic-module JIT reloads producer and consumer artifacts and checks records, cursor behavior and collection under both policies; producer-local component-runner coverage exports only its boolean result, not the owner graph |
| [TokenSnapshot](TokenSnapshot.terrum) | Owned, indexed copy of a completed compiled scanner stream using three words per token, without the managed chain's 64-link limit | Scalar operator-spacing checks retain a rolling adjacent-token window; AOT/JIT vacancy-only `init ref` indexed reads beyond 64 tokens, exact EOF and checked invalid indices; a maximum-length source verifies every identifier record and EOF through JIT and AOT under both policies; buffer capacity and allocation still bound the snapshot, trivia requires the separate snapshot, and diagnostics, general parser integration and general CST parsing remain outside this bounded scalar use |
| [TokenTriviaSnapshot](TokenTriviaSnapshot.terrum) | Separately owned scanner-6 leading-trivia records with kind, range and owning token index, atomically published with `TokenSnapshot` by `TryScanLossless` | The lossless composite builds both owned buffers together and publishes only after token records pass the shared snapshot-validation gate; AOT/JIT host-lexer parity, contiguous source coverage, vacancy-only `init ref` reads, invalid record/owner rejection and input mutation independence under both policies; a maximum-length source verifies every trivia record and owning token; no diagnostic recovery or general CST parser |
| [TokenIntegerSnapshot](TokenIntegerSnapshot.terrum) | Separately owned scanner-6 numeric-token indices and raw unsigned magnitude words, published with tokens and trivia by `TryScanCompilerInput` | Compiler-input construction reuses the one-pass lossless token/trivia builder, then requires one ordered record per integer token and reparses each source slice to check the full `uint64` magnitude or overflow status; AOT/JIT full-width, radix and overflow fixtures, vacancy-only `init ref` indexed lookup/find, missing-record/mismatched-magnitude/status/tag/owner rejection and atomic failure; a maximum-length source checks all densest integer, token and trivia records together; general binding remains hosted |
| [TokenDiagnosticSnapshot](TokenDiagnosticSnapshot.terrum) | Independently owned lexical diagnostic codes and positive source-bounded ranges, available after a scan with errors even when token snapshots cannot publish | AOT/JIT host diagnostic parity under both policies, vacancy-only `init ref` reads, invalid buffer/range rejection, stable reads after host mutation and no success-shaped empty token stream on lexical failure; a maximum-length malformed-UTF-16 source retains and validates every diagnostic after token publication rejects |
| [ScalarParserTypes](ScalarParserTypes.terrum) | Shared scalar type recognition, token-name equality, scalar comparison and scanner-integer decoding for separately compiled bounded parser probes | Vacancy-only `init ref` helper outputs; scanner-owned source ranges, UTF-16 spelling and integer records, with no independent syntax or semantic ABI |
| [ScalarFrontend](ScalarFrontend.terrum), [ScalarFrontendProgram](ScalarFrontendProgram.terrum) | Bounded v85 frontend over a successful scanner snapshot: source-bound scalar/void functions, up to three scalar parameters, supported expressions including single-scalar character literals only in representable `char16` contexts, and structured control flow, visible-local lifetime ending, explicit/inferred vacant locals, scalar constants, nested cleanup/recovery and foldable-true static proofs. Complete snapshots inventory global declarations and direct forward/self/mutual calls. They also resolve up to eleven top-level internal, non-generic scalar aliases, including forward chains, in return/parameter/local type positions; duplicate, unresolved, cyclic, public, attributed and generic aliases reject, and alias names are not explicit conversion-call targets. Scalar out/init-ref modes support matching explicit/declared destinations and normal-return publication for non-Boolean/void results. Caller-unsafe declarations and explicit unsafe blocks/wrappers are source-audited. Named by-value and scalar output calls preserve source-order evaluation. Result/error objects and internal handled/header-end values use conditional `init ref` outputs; handled parse diagnostics return a result, while false returns leave outputs vacant. Non-scalar non-Boolean outputs, namespaces/imports, overload search, callable values, general references, typed catches, filters and unproven proofs remain unsupported. See the [native profile](../docs/native.md). | Produces independently audited source-bound projections for the shared hosted verifier/emitter; aliases are reconstructed from original source, scanner integer records supply integer constants, and `CharacterLiteral` decodes character-token spans from `Host.Source`. The Terrum decimal, hexadecimal-float and based-integer helpers populate positive raw-bit floating literal leaves; the host reader independently checks floating payloads against the hosted parser. Inventory and parsing use a borrowed indexed `Span<Token>` with bounded per-function windows; inventory/function windows add one synthetic EOF, while the synchronous single-function fallback retains the scanner's real EOF. The snapshot owner stays live through inventory, parser retries and fallback; no managed token-chain copy or rescan is used. The persisted primitives DLL and `build-component` do not consume its typed result. |
| [ScalarFrontendProjection](ScalarFrontendProjection.terrum) | Host-owned buffers with completion status written last: 392-word single-function and 4,284-word complete-program buffers. Only v85 is produced and accepted, including diagnostics and every function frame; all v1–v84 tags reject. Existing compact-expression and tree/statement layouts remain fixed. Unused extensions are zero; absent call targets are `-1`. | `ScalarFrontendHost` independently checks source anchors, types, literals, node order/ownership, precedence, roots, modes, local binding/initialization/liveness, scopes, control flow, returns, mutability, conversions and exact declaration effects. Complete-program audits also check declaration sets, duplicate names, frame coverage, callees and argument ownership. Void-call statement roots, DoesNotReturn continuation, scalar out/init-ref destinations, unsafe call scopes and named argument mappings retain their exact validations. Shared verification checks effects, definite liveness/publication and reachable completion under the selected policy. Limits remain 64 tokens per snapshot; per function 8 total named/hidden locals, 12 statements, 32 nodes, 16 expression-recursion levels and 6 structural levels. See the [implementation reference](../docs/implementation.md). This is not a standalone compiler or machine-code encoder. |
| [ScalarFunctionParser](ScalarFunctionParser.terrum) | Provisional scalar function probe: grouped/unary literals, two-literal checked arithmetic and bitwise operations, typed integer/`char16` comparisons, and one named scalar parameter in comparisons or integer checked addition | Success-only parse helpers and every scalar evaluator output use vacancy-only `init ref`. Hosted-span and executed-value parity with AOT/JIT at decimal/based boundaries across all four integer widths and `char16` comparisons; names bind to declarations, invalid magnitudes and checked addition overflow reject; requires `ScalarParserTypes`; other declarations still require the host parser |
| [ScalarParameterPairParser](ScalarParameterPairParser.terrum) | Separate bounded probe of two named scalar parameters returned through checked integer addition/subtraction, `x + y + literal` with both checked steps, or a Boolean comparison (including `char16`) | Vacancy-only `init ref` outputs; AOT/JIT under both policies across signed/unsigned 32-/64-bit extrema, code-unit comparisons and decimal/based literal boundaries; both identifiers bind to distinct declarations, either overflow or unsigned subtraction underflow rejects; requires `ScalarParserTypes`, not a bootstrap compiler |
| [ScalarConditionalParser](ScalarConditionalParser.terrum) | Separately compiled probe of a `bool` parameter selecting between two named, same-typed scalar parameters with explicit `if`/`else` returns | Vacancy-only `init ref` parse/evaluator outputs; AOT/JIT under both policies check both reachable arms across six scalar types and reject swapped/unbound names and missing branches; uses scanner-owned tokens and `ScalarParserTypes`, not the general hosted parser or a bootstrap compiler |
| [ScalarConditionalAddParser](ScalarConditionalAddParser.terrum) | Separate bounded probe of a Boolean-controlled checked addition of two named integer parameters with a fallback return of the first parameter | Vacancy-only `init ref` parse/evaluator outputs; AOT/JIT under both policies check successful addition, both overflow directions, and that a skipped addition cannot fail; scanner-owned tokens and shared scalar range guards, not general branch parsing or a bootstrap compiler |
| [ScalarComparisonConditionalParser](ScalarComparisonConditionalParser.terrum) | Separate bounded probe of two named integer/`char16` parameters used in a six-way comparison predicate selecting one parameter per `if`/`else` return | Vacancy-only `init ref` parse/evaluator outputs; AOT/JIT under both policies check signed/unsigned and code-unit extrema, selected versus discarded arms, and rejected unbound/swapped identifiers; scanner-owned token records, not an adopted Body encoding or a bootstrap parser |
| [Scanner](Scanner.terrum) | UTF-16 decoding, pinned Unicode identifiers, exact token kinds, lossless trivia, lexical diagnostics and bounded integer magnitudes | Escaped identifier scans reuse each decoded scalar; comment trivia reads non-surrogate units directly and decodes surrogate units only when needed; quoted-token loops and literal decoders likewise advance non-surrogate units directly and decode surrogate pairs only when needed; trivia and punctuation loops reuse current code units, numeric scans cache each current unit, keyword/operator matching filters by one cached leading unit, and hexadecimal integer/float classification shares one mantissa scan while valid floats resume at their exponent; explicitly compiled with `SourceRange`, `CharacterFacts`, `Utf16CodeUnits`, `Utf16Scalar`, `CharacterLiteral` and `StringEscape`; `init ref` comment-scalar widths plus vacancy-only punctuation and magnitude helper outputs; executed through fixed checked host buffers with overflow-safe record-capacity checks; the `scanner-10` persisted JIT profile is provisionally implemented under the [scanner invocation-profile proposal](../docs/proposals/scanner-invocation-profile.md) and compares host token/diagnostic parity across every BMP `\u` escape, every valid supplementary `\U` scalar and all 2,048 rejected surrogate-valued `\U` escapes in bounded source grants, plus above-maximum boundaries, every raw supplementary quoted scalar, every raw surrogate code unit in an isolated quoted token, every supplementary scalar in both identifier-start and identifier-continue positions, a maximum-length quoted source ending in U+10FFFF and maximum-length sources ending in each unpaired surrogate; general source-grant semantic-module execution remains outside this profile; see the [precise subset](../docs/scanner.md) |
| [ManagedScanner](ManagedScanner.terrum) | Copy a completed compiled scanner token stream into newly published managed storage or a sequential cursor | AOT/JIT parity with host token kinds/ranges under both policies; lexical failure, undefined token kinds, empty non-EOF records, other malformed host records and more than 64 tokens leave the output vacant; only one final zero-length EOF is valid; independent copies survive clear and collection |
| [Host APIs](host.api.terrum) | Explicit source acquisition, cancellation, digest and output boundary | Interfaces, immutable publication, operation failures; host adapters |

### Source and managed component contracts

`SourceRange` deliberately mirrors the current host's signed 32-bit UTF-16
coordinates, including the checked end bound. This is an interchange limit for
the syntax protocol, not a decision that Terrum collection extents are signed or that
runtime text must use UTF-16. A later larger-source protocol needs an explicit
schema change. Source file UTF-8 decoding remains separate from coordinates.
The range's explicit `End` getter is exported alongside the existing `GetEnd`
method for independent module consumers; both compute the checked end.
Persisted semantic-module JIT coverage also exercises construction, field
reads, both end accessors, and rejected negative/overflowing ranges from an
independent consumer under both runtime policies. This verifies persistence,
not a broader coordinate or range contract.

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

`SourceText` exposes only source-grant queries: UTF-16 code-unit length,
checked code-unit reads, validation of half-open ranges, and streaming line
reads against the current immutable `Host.Source`. It retains no pointer or
source text, creates no runtime string, and does not carry a diagnostic path.
Persisted semantic-module JIT coverage checks exact reads of all 65,536 UTF-16
code units, including surrogate units, across fresh grants under both runtime
policies.
Its ranges remain offsets for the current invocation's source grant; this
facade is not the hosted, owning `SourceText` collection.

`SourceLines` builds coordinate and ordinal queries on the `SourceText` facade,
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
an executed bounded component, not an efficient indexed replacement. Together
these source-grant helpers still do not implement the hosted `SourceText`
collection.

Semantic-module revision 59 can persist these four source reads symbolically
and propagate the reserved `Host.Source` requirement to an independent
consumer. `NativeJit.ExecuteWithSourceGrant` links and executes that
source-dependent program with one readonly `char16` snapshot of at most 16,384
code units, copied into fresh invocation storage. Source contents, sample
lengths and addresses are not stored in artifacts. This is provisional support
under the still-unapproved source-grant proposal; graphs, module sets, native
DLLs and persisted AOT images remain unsupported. The scanner also has
provisional persisted semantic-module JIT support under revision 59:
`scanner-10` artifacts retain the fingerprinted profile identity and canonical
buffer descriptors but no sample contents or capacities. The dedicated
`NativeJit.ExecuteWithScannerProfile` adapter supplies fresh source, readonly
tables and output buffers for every invocation, then returns only copied and
validated token, diagnostic, trivia and integer results after normal
completion. Regression coverage reuses the same producer and consumer
artifacts across empty, differently sized, malformed-UTF-16 and maximum-length
inputs under both runtime policies. It also compares every BMP `\u` escape and
every valid supplementary `\U` scalar with the hosted lexer, including
all 2,048 surrogate-valued `\U` rejections, above-maximum boundaries,
malformed lengths/hex digits and an unterminated escape. Every
valid supplementary scalar is also checked as raw quoted-string content and
in both identifier-start and identifier-continue positions; every raw
surrogate code unit is tested in an isolated quoted token. A maximum-length
quoted source ends with the valid U+10FFFF surrogate pair, and maximum-length
identifier inputs end with each unpaired surrogate code unit. The
[scanner-profile proposal](../docs/proposals/scanner-invocation-profile.md)
remains unapproved; this support does not make the scanner or scalar frontend
independent of hosted verification and emission. Integer-value records are
also checked against binary, decimal and hexadecimal `uint64` maximum and
overflow expectations. A maximum-length alternating identifier/whitespace
source stresses dense token and trivia publication; a maximum-length newline
source fills the trivia record bound, while a maximum-length unpaired-surrogate
source fills the token and diagnostic bounds. A maximum-length alternating
integer/whitespace source validates the densest integer-record sequence under
the source bound.

`SourceLineIndex` builds on that streaming reader. It owns a fixed-capacity native
table of line starts, publishes the completed index through a conditional output,
and answers position queries by binary search over a scoped readonly slice of
the initialized table. The slice borrows the owning field only for that query;
it is not returned or stored in the index. The index retains only copied offsets
and the source length; queries do not retain or reread the source grant. It
also supports the inverse line/character-to-offset lookup from the same table.
It returns a line's full extent, including its break, using adjacent decoded
starts without rereading the source; the final line ends at the stored source
length. Line positions include break units on their preceding line; a
character at the next line's start must use the next line number, and only the
final line admits the EOF offset. Invalid line/character pairs return false
without publishing an offset, including values that would overflow a signed
offset if added first. Construction counts lines before reserving exactly one
word per line start, including the final empty line after a trailing break.
The sign bit on a noninitial encoded start marks whether its preceding break
was CRLF; masking recovers the nonnegative offset across the full `int32`
source-length domain. The fill pass reuses the known first and final starts,
so one- and two-line sources need no second scan, and captures each other
break's marker while filling starts. If every code unit ends a line, starts are
filled directly from their ordinal positions. This is not a resizing collection,
stored source view, managed line collection, or general allocator API.

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
radix 2..36; signed parsing optionally accepts a leading `-`. It accepts
repeated digit separators for radix 10 only after a digit, and for radix 2
and 16 wherever the corresponding prefixed literal permits them, including
at the start of the digit slice. Separators are rejected for other radices.
It consumes the **entire** nonempty slice, with no prefix, whitespace or
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
It accepts only the contiguous `TokenKind` ordinals 0 through 106; every
non-EOF record must have positive length, and exactly one zero-length EOF must
be last.
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
Terrum-authored conversion helpers populate positive raw-bit literal leaves;
the hosted reader validates their token spans and bits independently against
the hosted parser. Conversion projection covers the two floating formats and the four
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
