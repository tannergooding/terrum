# Compiled Terrum scanner component

The `self-host` source bundle (`SourceRange.terrum`,
`CharacterLiteral.terrum`, `StringEscape.terrum`, then `Scanner.terrum`)
contains an executable
lossless scanner component, not a bootstrapped compiler. `Scanner.terrum`
implements scalar decoding, Unicode identifier
classification, keyword and operator selection, numeric token boundaries,
quoted strings, leading trivia and lexical diagnostics in Terrum. Character
literal validation and its diagnostic spans come from the shared
`CharacterLiteralDecodeResult`; string escapes use the shared
`StringEscapeDecodeResult`. The scanner retains responsibility for finding
token boundaries and reports each malformed escape in source order.
The C# host supplies buffers and pinned lexical data; it does not pretokenize
or make source-dependent classification decisions for the component.
`_` remains a distinct token, while underscore-prefixed names remain
identifiers. The scanner recognizes contextual `init`, loop labels and named
transfers using the existing identifier, colon, `break` and `continue`
spellings. It also recognizes `??`/`??=`, prefix/postfix `++`/`--` (token IDs
86/87), shifts `<<`, `>>`, `>>>`, and compound assignments `+=`, `-=`, `*=`,
`/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=` and `>>>=`. Slot 77 remains
reserved; `abstract` is an ordinary identifier and `this` remains contextual.
Hosted and compiled scanners share longest-match selection. Adjacent runs such
as `x+++y` diagnose instead of being reinterpreted, while trivia distinguishes
`x++ +y` from `x+ ++y`. In generic type
context, the parser partitions `>>`/`>>>` into CST closers, retaining leading
trivia on the first part; `SyntaxTree.Tokens` keeps the original lexical
stream. The internal match reserves three width bits for `>>>=`; published
records remain unchanged through scanner-5.

Scanner-6/schema-6 supports generalized ordinary and documentation comment
fences. Maximal opening star runs select
whole equal-length closing runs with one slash for ordinary blocks and two
for documentation blocks. Shorter runs and missing slash suffixes are payload;
overlong runs with the required suffix diagnose `TRM0010`. Payload is opaque,
not nested, and extra closer slashes remain for later lexing.
Trivia IDs 0–3 retain their meanings; 4/5 append documentation line/block
comments. Diagnostic 10 is added without changing any record width, token ID,
source/token budget or native/semantic protocol.

Scanner-7/schema-7 supports contiguous `@keyword` identifiers. The escape
catalog is grammar §2's exact reserved and
contextual vocabulary, independent of legacy unescaped classification.
It includes future unsupported words without implementing their constructs,
and excludes `_`, primitive spellings and legacy-only `out`, `foreach`,
`abstract`. Names are ordinal, case-sensitive and not normalized; `@await`
is an identifier named `await`, while `@Await`, `@ordinaryName`, `@_`,
`@int32` and `@ await` reject. A contiguous invalid identifier candidate is
one Bad token with `TRM0001` over its entire raw extent. An incomplete `@`
is a one-unit Bad token and does not consume following trivia.
Raw token text, spans and trivia retain `@`. Name lookup uses the shared
identifier identity; escaping suppresses syntactic contextual recognition.
The compiled scanner consults a separate read-only host escape catalog,
not a host re-lexing fallback. No token IDs, record widths, integer magnitudes,
frontend projection versions or resource budgets change.

The shared operator catalog includes `=>` for expression bodies; no scanner
record or schema revision is needed. Attached numeric width suffixes diagnose
as `TRM0006`; missing hexadecimal and binary prefix digits diagnose as
`TRM0007` and `TRM0008`, and malformed digit separators diagnose as
`TRM0009`. Valid decimal, hexadecimal and binary integer records
are untagged, while recognized rejected suffixes retain their tag value in the
existing integer-record field for diagnostic fidelity. For example,
`0x1f32` is an untagged hexadecimal integer; `0x1u32` is rejected as a
width-suffixed token. This error metadata is not valid target syntax or a type
selector. The five-word integer record shape is unchanged; component and
schema identifiers advance to scanner-5/schema-5. The executable numeric
decoder handles decimal, hexadecimal and binary integers with digit
separators. It precomputes each token's `uint64` overflow cutoff by radix, so
digit accumulation does not repeat a division or make a per-digit helper
call. The hosted binder separately constructs `float32`/`float64` literals;
decimal and valid hexadecimal floating tokens retain their source spelling
in the compiled scanner but produce no integer value record. Floating
arithmetic/comparisons, typed floating conversions, custom literal parsing,
registered string targets and `[ConstEval]` remain unsupported.

Scanner-10/schema-10 appends `DotDot`, `DotDotCaret`, and `QuestionDot` as
token kinds 104–106 after Scanner-9/schema-9 added `CharacterLiteral` as kind
103. Existing token IDs, record widths, diagnostic IDs and integer records
remain unchanged. These punctuators are lexically recognized; parser
consumption and binding remain separate support boundaries. The component
diagnoses empty or multi-scalar character literals with `TRM0001`,
unterminated literals with `TRM0002` and invalid escapes with `TRM0005`; it
validates one scalar but leaves target-type representability to the hosted
binder.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll scan-component self-host input.txt --runtime fail-fast --json
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build-component self-host samples\native\factorial.terrum -o artifacts\scanned-factorial.exe --runtime fail-fast --json
```

For these commands a directory argument explicitly selects the ordered
`SourceRange.terrum`, `CharacterLiteral.terrum`, `StringEscape.terrum`, and
`Scanner.terrum` bundle. The files compile as separate syntax trees so
component diagnostics retain their owning file paths and source indexes.
Single-file `scan-component`/`build-component` inputs remain available for
self-contained scanner components; `Scanner.terrum` itself requires the bundle.

Both are execution-worker commands: compiled scanner code runs inside that CLI
process. Supervising clients must launch a bounded subprocess and treat abnormal
termination as a runtime failure. Ordinary `build` still uses the host lexer.
When `scan-component`
scanning completes, JSON uses `schemaVersion: 10`, `component: "scanner-10"`,
`coordinateSystem: "utf-16"`, the pinned `unicodeVersion`, `sourceLength`,
`success`, and arrays of `tokens`, `integerValues`, and `diagnostics`. Each token has an exact
lexical `kind`, `start`, `length` and its ordered `leadingTrivia` array.
Each trivia has `kind`, `start`, `length`; each diagnostic has `code`,
`start`, `length`. EOF owns any trailing trivia.
Each `integerValues` entry has `tokenIndex`, `tag` (an empty string when
untagged), and `status` (`"exact"` or `"overflow"`). Exact entries also include
`magnitude` as a decimal string; overflow entries omit it.
Lexically valid input returns `success: true` and exit code 0. Lexical errors
return `success: false` and exit code 1 but still publish the full token stream
and diagnostics on stdout. Without `--json`, token rows go to stdout and
diagnostic code/spans go to stderr. Input, compilation, or invalid-result
failures retain the standard schema-1 error response rather than producing a
scanner-10 token response.

`build-component` instead adapts the native records to `LexResult`, pairs the
validated integer values with that same source and token stream, feeds them
into the official lossless parser and binder, verifies the resulting program,
and emits a native executable. Binding consumes the compiled scanner's
integer magnitudes, including signed-minimum operands, rather than decoding
their source digit sequences again. Missing, duplicate or mismatched integer
records reject the component result; unsupported numeric forms retain their
existing binding diagnostics. Ordinary `build` still uses the host lexer and integer
decoder. `build-component` validates current trivia extents and comment
diagnostics without re-lexing target tokens or falling back to
host decoding when the scanner fails. The scanner's own source is still
compiled by the host front end. This is a Terrum-implemented lexical and
integer-decoding phase in the real compiler pipeline, not a self-hosted parser
or complete bootstrap.
Its JSON is the ordinary schema-1 compilation response with command
`build-component`. A completed successful build returns 0; diagnostics return
1 and input/output errors return 2. Native scanner fail-fast terminates the
worker with its reserved runtime status. Lexical, syntax, binding, runtime and
record-validation failures leave an existing output artifact unchanged.

`ScannerHost.ReadLexResult` materializes source-owned token/trivia text and
standard diagnostic objects from validated buffer records. `SyntaxTree.Parse`
accepts this lexical result and its complete integer-value records without
invoking the host lexer. These boundaries validate shape, source coverage,
record ordering, suffixes, keyword escape spelling/extents/classification,
current comment rules and integer-token coverage, not every supplied
scanner classification or its arithmetic. Raw buffers have no schema marker
and are interpreted using current token identities and record shapes; there is
no historical-schema reader contract. A split `@`/keyword or contradictory
escape identifier cannot masquerade as current parser input. A record with a
valid shape but an incorrect magnitude would affect binding. Use a trusted compiler component;
host/component differential fixtures check actual numeric behavior, while
shape validation alone cannot establish that an arbitrary replacement scanner
implements Terrum.
Lexical diagnostic codes/spans match the host lexer; their human messages are
component-specific summaries.

## Storage and host boundary

The compilation host explicitly grants named fixed-buffer capabilities through
`HostBuffer` descriptors. The scanner adapter grants:

| Capability | Element type | Access | Purpose |
|---|---|---|---|
| `Host.Source` | `char16` | Read-only | Exact source snapshot code units, including malformed surrogates |
| `Host.XidStart` | `int32` | Read-only | Sorted inclusive range pairs from the host lexer's pinned XID_Start table |
| `Host.XidContinue` | `int32` | Read-only | The corresponding XID_Continue ranges |
| `Host.Tokens` | `int32` | Read/write | Initialized words for token records |
| `Host.Diagnostics` | `int32` | Read/write | Initialized count and lexical diagnostic records |
| `Host.Trivia` | `int32` | Read/write | Initialized count and leading-trivia records |
| `Host.Keywords` | `int32` | Read-only | Keyword spellings and exact `TokenKind` values generated from the host catalog |
| `Host.Operators` | `int32` | Read-only | Punctuation/operator spellings and exact `TokenKind` values |
| `Host.IntegerValues` | `int32` | Read/write | Initialized count and unsigned integer-magnitude decoder records |
| `Host.EscapeKeywords` | `int32` | Read-only | Closed canonical escape spellings, packed in the same kind/length/code-unit format with Identifier kind |

The first nine grants retain their indices; the appended escape catalog is
grant 9. The scalar frontend output grant follows at index 10, without changing
its 392-word projection or historical payload versions.
V74/V75 append `Host.FrontendProgram` at index 11 for complete multi-function
snapshots; scanner grants and records are unchanged.

An index is evaluated once, must be `int32`, and is checked against the fixed
length before access. Negative indices fail as well as indices past the end.
Read-only descriptors cannot be written. The source cannot manufacture a
pointer/length pair, resize a buffer, rebind its backing, retain or return its
capability, or turn it into a struct field. Only checked element access and
`Length` are available. Host names require explicit grants; they are not ambient
filesystem or process access.

This is a deliberately restricted buffer/span-like surface, **not** the general
`Span<T>` library, its generic ABI or a completed lifetime checker. The owning
artifact/JIT invocation keeps backing allocations alive for the entire
execution. No source operation can dispose them, and no loan can escape that
scope. More general span parameters, slices, overlapping mutable loans and
owner-derived stored views still require the full access/lifetime model.

The adapter copies source units into one 32-bit word per element and grants at
most 16,384 source units. This is an explicit compiler-host interchange format;
it chooses neither UTF-16 as runtime text's default nor a general `char16[]`
storage ABI. Code units are not assumed to be valid Unicode scalars.

## Observable scanning contract

Each token record is three initialized `int32` words: exact `TokenKind` numeric
value, UTF-16 start, UTF-16 length. `Scan` returns the number of published
records, including EOF. Words beyond that count are not token records.
`Host.Trivia[0]` contains a count, followed by quadruples of exact `TriviaKind`
numeric value, UTF-16 start, UTF-16 length, and zero-based owning token index.
Leading trivia remains attached to EOF when there is no following token.
`Host.Diagnostics[0]` contains
the diagnostic count; subsequent triples are numeric code (1–12 maps to
`TRM0001`–`TRM0012`), UTF-16 start and UTF-16 length, in reporting order.
Scanner-8/schema-8 introduced diagnostic identities 11 for an invalid maximal
punctuation run and 12 for a defined but unsupported spelling; Scanner-10
retains those identities and record widths. Raw buffers have no schema marker; adapters
interpret them with current record identities and validate the current source
profile without replacing supplied classifications.
Overlong suffixed fences report their whole run plus the required suffix;
recovery keeps them in the comment until a valid closer or EOF. EOF without
a valid closer also reports `TRM0003`, retaining all source text.
`Host.IntegerValues[0]` contains a count followed by five-word records:
zero-based numeric-token index, status (0 = decoded; 1 = exceeds `uint64`),
unsigned magnitude low 32 bits, high 32 bits, and suffix-tag metadata.
The magnitude is exactly zero on overflow, not a truncated value. Tag 0 means
no suffix; 1–5 map to `i8`, `i16`, `i32`, `i64`, `i128`, and 6–10 to `u8`,
`u16`, `u32`, `u64`, `u128`. Valid integer literals use tag 0. Recognized
attached width suffixes are rejected with `TRM0006` but preserve their tag in
this field; that legacy metadata does not make a suffixed token valid. The
low/high buffer words retain raw unsigned bits even when represented by
negative `int32` values. Integer records are strictly ordered by token index;
fractional, exponent, floating-suffix and malformed-exponent numeric tokens
have no record. The record magnitude is an unsigned radix-decoded value;
target-width signed interpretation is applied later by the binder. Leading
zeroes and separator underscores do not change that magnitude.

`ScannerHost.ReadIntegerValues(source, tokens, bytes)` returns
`ImmutableArray<ScannerIntegerValue>` with `int TokenIndex`, `string Tag`,
`bool IsOverflow`, and `ulong? Magnitude`. `Tag` is the lowercase suffix or
`""`; rejected width-suffix tokens may also carry their lowercase suffix as
diagnostic metadata. `Magnitude` is null when `IsOverflow` is true. It verifies
capacity, ordering, referenced token kind, integer-form source spelling and
any retained suffix metadata, defined status/tag, and the canonical overflow
payload. This is structural validation, **not** host-side
magnitude decoding. The compiled Terrum scanner determines numeric form and
performs checked decimal, hexadecimal or binary accumulation, ignoring
separator underscores. Values above `ulong.MaxValue` get an overflow record;
overflow itself is not a lexical error. A recognized `i128`/`u128` suffix
still produces `TRM0006`, independently of the retained overflow metadata.
The exact `2^63` magnitude is preserved for decimal negative-minimum and
based signed-bit-pattern construction, and `ulong.MaxValue` is preserved.
Rejected integer suffixes retain diagnostic tag metadata; they do not select
the literal's type.
Untagged integer magnitudes are not assigned a default type; minus remains a
separate operator. `ReadLexResult` does not consume this side table by itself. `build-component`
passes both results to `SyntaxTree.Parse`, which requires a record for every
integer-form numeric token and binds from those decoded magnitudes. Numeric
forms without integer records (such as floats) remain unsupported in the
native profile. This is not full self-hosting.
The reader accepts either `ImmutableArray<ScannerToken>` or the
`ImmutableArray<SyntaxToken>` already produced by `ReadLexResult`; neither
overload re-lexes or duplicates the token stream.

The adapter validates published counts, kinds, ranges, owner ordering,
nonempty diagnostic/trivia spans, and exact contiguous coverage by token and
trivia records. No source unit may be omitted, duplicated or reordered.
Current parser adaptation additionally audits leading trivia against current
comment rules, including required diagnostics; token classification and numeric
decoding remain native results. The current component emits scanner-10/schema-10
only. Raw buffer adapters apply current identities and shapes and do not
provide compatibility for historical scanner schemas.
A malformed token remains a `Bad`, `NumericLiteral`, `StringLiteral`, or `CharacterLiteral` token
with its raw source span; diagnostics do not replace source characters.

Hosted parsing and supported native scalar shapes enforce current delimiter
attachment, role-specific dot/operator spacing and contextual-punctuation
boundaries over raw token spans; these checks do not change tokenization. See the
[implementation reference](implementation.md) for exact roles and
diagnostics.

Scanner-10 classifies maximal ASCII punctuation runs in the compiled scanner.
Unlisted runs are one Bad token with whole-run `TRM0011`; `..`, `..^` and `?.`
are recognized as single tokens, while their parser consumption and binding
remain unsupported. Other defined-but-unsupported spellings remain one Bad
token with `TRM0012`. Deferred `>`/`?`
type-boundary candidates retain existing splitting only when actual hosted
parser consumption certifies them; missing/recovery tokens cannot certify a
boundary, and repeated optional marking retains `TRM1005`. Native scalar
snapshots check whole runs before spacing/fallback, with no type exemptions.
Scanner-10/schema-10 retains Scanner-8's diagnostic identities 11/12 and
appends token IDs 104–106; record widths, projections, bounds and semantic/native
ABI remain fixed. Raw buffer records are interpreted only with the current
identities and shapes. See the
[implementation reference](implementation.md) for current parser and
unsupported-spelling boundaries.

Keyword records are generated from the host `TokenKind` catalog's `*Keyword`
members. Operator records use their lexical spellings, with fixture comparisons
covering the entire punctuation/operator catalog. Packed lexeme records contain
`(kind, UTF-16 length, code units...)` and end with kind `-1`; native code
compares source code units against these readonly records and selects the
longest matching operator. The host never chooses a token from the source.
The scanner first measures the maximal glyph run, stopping at comments,
and accepts a catalog match only when it covers the entire run. Deferred
type candidates cache the run end rather than rescanning every suffix.
The shared managed classifier also audits completed records. See
[implementation status](implementation.md) for parser certification and
current unsupported spelling recovery.
Output capacities are bounded by `sourceLength + 1` tokens, at most
`sourceLength` trivia, integer-value, and diagnostic records; the
native scanner requires capacity before publishing each record.

Identifiers use Unicode 17.0.0 XID_Start/XID_Continue plus underscore, including
supplementary scalars and the established Join_Control continuation policy.
Binary searches and UTF-16 decoding execute as compiled Terrum. Data comes from
the same generated `UnicodeXid` ranges as the host lexer; the public read-only
`UnicodeIdentifierProfile` carries the version and pinned input SHA-256.
No normalization, case folding or ambient host Unicode categories are involved.

Whitespace runs, individual line breaks (with CRLF as one trivia item), ordinary
and documentation line comments, and generalized nonnesting block fences follow the host lexer and are
preserved as leading trivia; malformed surrogates within comments and unterminated
block comments are diagnosed. Strings preserve raw spans across escapes;
unterminated strings stop before a newline, and invalid/incomplete escapes and
malformed surrogates produce matching diagnostic codes and spans. Numeric
hexadecimal integer tokens begin with `0x` or `0X`, binary tokens with `0b` or
`0B`; each requires at least one radix digit (`TRM0007` and `TRM0008` when
missing). `a`–`f` remain hex digits, so `0x1f32` is one complete integer.
Runs of `_` may separate digits or follow a based prefix before its first
digit; malformed placements produce `TRM0009`. Numeric fractions require
a digit after `.`, exponents follow the host lexer's
identifier-disambiguation and incomplete-exponent rules, and recognized
numeric width suffixes attach only at an identifier boundary before producing
`TRM0006`. Spans retain original source positions. The component does not
decode string values, type numeric values or construct host objects. Its
adapter materializes those objects after execution.
It is not a complete
self-hosted front end.

Shared fixtures compare native exact token kinds, trivia kinds/ownership/spans,
reconstruction and lexical diagnostic codes/spans against the host lexer.
They cover every keyword and operator spelling, every trivia kind, pinned-range
boundaries, supplementary identifiers, combining marks, non-ASCII digits,
Join_Control, comments, quoted escapes, malformed strings, numeric boundaries,
whitespace, malformed surrogate pairs and operators. Output-capacity
requirements are checked; the scanner does not silently truncate records.
End-to-end build fixtures compare the native-scanned and host-scanned emitted
images, execute generated programs, and verify that scanner failures neither
fall back nor replace prior artifacts.
Native integer-decoder fixtures compare every published index, status, magnitude
half, and suffix against a test-only independent `BigInteger` oracle, including
zero padding, `2^63`, `ulong.MaxValue`, overflow, width tags, and noninteger
numeric forms; repeated scans reset the published count.

## Quoted-literal decoding candidate

`self-host/StringLiteral.terrum` separately validates and decodes quoted source
spelling against the same fixed immutable `Host.Source` grant. It is not a new
scanner side table or a runtime `Text` implementation. `StringLiteral.TryCreate`
checks the complete range, delimiters, raw scalars and escapes before publishing
a compact spelling/count value. `TryReadScalar(position, out scalar, out next)`
returns an `int32` Unicode scalar and the next absolute UTF-16 offset only at a
scalar boundary. Opening/closing quotes, offsets inside escapes or surrogate
pairs, and out-of-range positions fail without publishing outputs.

The supported escapes are exactly `\n`, `\r`, `\t`, `\\`, `\"`, `\0`, four-digit
`\u` and eight-digit `\U`. Both Unicode escape forms must individually denote
scalars; two escaped surrogate values cannot form a valid pair. Raw paired UTF-16
surrogates decode to one scalar. Embedded zero, noncharacters and combining
sequences retain their values, without normalization. Invalid wide escapes
are rejected without overflowing the native unsigned accumulator.

The shared fixtures compare whole-literal acceptance with the host lexer and
native decoded values/offsets with explicit spelling/scalar pairs under both
runtime policies. They inspect every offset, repeat construction and reading,
and check failed-output use is rejected. Arbitrary-position reads rescan the
prefix to establish boundaries. `StringLiteralCursor.Create(literal)` and
`cursor.TryRead(out scalar, out next)` provide linear sequential traversal;
each next cursor is constructed only from a validated scalar boundary. Both
forms agree on every scalar and source offset. This bounded compiler component
is not an indexed or allocated string library. It retains no borrowed address
and cannot change the immutable source grant on which its validation depends.
