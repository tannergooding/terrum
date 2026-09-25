# Compiled Terrum scanner component

`self-host/Scanner.terrum` is an executable lossless scanner component, not a
bootstrapped compiler. It implements scalar decoding, Unicode identifier
classification, keyword and operator selection, numeric token boundaries,
quoted strings and escapes, leading trivia and lexical diagnostics in Terrum.
The C# host supplies buffers and pinned lexical data; it does not pretokenize
or make source-dependent classification decisions for the component.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll scan-component self-host\Scanner.terrum input.txt --runtime fail-fast --json
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build-component self-host\Scanner.terrum samples\native\factorial.terrum -o artifacts\scanned-factorial.exe --runtime fail-fast --json
```

Both are execution-worker commands: compiled scanner code runs inside that CLI
process. Supervising clients must launch a bounded subprocess and treat abnormal
termination as a runtime failure. Ordinary `build` still uses the host lexer.
When `scan-component`
scanning completes, JSON uses `schemaVersion: 3`, `component: "scanner-3"`,
`coordinateSystem: "utf-16"`, the pinned `unicodeVersion`, `sourceLength`,
`success`, and arrays of `tokens` and `diagnostics`. Each token has an exact
lexical `kind`, `start`, `length` and its ordered `leadingTrivia` array.
Each trivia has `kind`, `start`, `length`; each diagnostic has `code`,
`start`, `length`. EOF owns any trailing trivia.
Lexically valid input returns `success: true` and exit code 0. Lexical errors
return `success: false` and exit code 1 but still publish the full token stream
and diagnostics on stdout. Without `--json`, token rows go to stdout and
diagnostic code/spans go to stderr. Input, compilation, or invalid-result
failures retain the standard schema-1 error response rather than producing a
scanner-3 token response.

`build-component` instead adapts the native records to `LexResult`, feeds that
result into the official lossless parser, binds and verifies the resulting tree,
and emits a native executable. It never re-lexes the target source or falls back
to the host lexer when the scanner fails. The scanner's own source is still
compiled by the host front end. This is a Terrum-implemented lexical phase in
the real compiler pipeline, not a self-hosted parser or complete bootstrap.
Its JSON is the ordinary schema-1 compilation response with command
`build-component`. A completed successful build returns 0; diagnostics return
1 and input/output errors return 2. Native scanner fail-fast terminates the
worker with its reserved runtime status. Lexical, syntax, binding, runtime and
record-validation failures leave an existing output artifact unchanged.

`ScannerHost.ReadLexResult` materializes source-owned token/trivia text and
standard diagnostic objects from validated buffer records. `SyntaxTree.Parse`
accepts this lexical result without invoking the host lexer. These boundaries
validate shape, source coverage and source identity, not the supplied scanner's
classification algorithm. Use a trusted compiler component; shape validation
cannot establish that an arbitrary replacement scanner implements Terrum.
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
the diagnostic count; subsequent triples are numeric code (1–5 maps to
`TRM0001`–`TRM0005`), UTF-16 start and UTF-16 length, in reporting order.
The adapter validates published counts, kinds, ranges, owner ordering,
nonempty diagnostic/trivia spans, and exact contiguous coverage by token and
trivia records. No source unit may be omitted, duplicated or reordered.
A malformed token remains a `Bad`, `NumericLiteral`, or `StringLiteral` token
with its raw source span; diagnostics do not replace source characters.

Keyword records are generated from the host `TokenKind` catalog's `*Keyword`
members. Operator records use their lexical spellings, with fixture comparisons
covering the entire punctuation/operator catalog. Packed lexeme records contain
`(kind, UTF-16 length, code units...)` and end with kind `-1`; native code
compares source code units against these readonly records and selects the
longest matching operator. The host never chooses a token from the source.
Output capacities are bounded by `sourceLength + 1` tokens, at most
`sourceLength` trivia records, and at most `sourceLength` diagnostics; the
native scanner requires capacity before publishing each record.

Identifiers use Unicode 17.0.0 XID_Start/XID_Continue plus underscore, including
supplementary scalars and the established Join_Control continuation policy.
Binary searches and UTF-16 decoding execute as compiled Terrum. Data comes from
the same generated `UnicodeXid` ranges as the host lexer; the public read-only
`UnicodeIdentifierProfile` carries the version and pinned input SHA-256.
No normalization, case folding or ambient host Unicode categories are involved.

Whitespace runs, individual line breaks (with CRLF as one trivia item), and
non-nesting `//` and `/* ... */` comments follow the host lexer and are
preserved as leading trivia; malformed surrogates within comments and unterminated
block comments are diagnosed. Strings preserve raw spans across escapes;
unterminated strings stop before a newline, and invalid/incomplete escapes and
malformed surrogates produce matching diagnostic codes and spans. Numeric
fractions require a digit after `.`, exponents follow the host lexer's
identifier-disambiguation and incomplete-exponent rules, and defined primitive
width tags attach only at an identifier boundary. Spans retain original source
positions. The component does not decode string values, type numeric values or
construct host objects. Its adapter materializes those objects after execution.
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
