# Compiled Terrum scanner component

`self-host/Scanner.terrum` is a small executable scanner component, not a second
complete front end or a bootstrapped compiler. It implements scalar decoding,
Unicode identifier classification, numeric token boundaries, quoted strings
and escapes, comment/whitespace skipping, punctuation and lexical diagnostics
in Terrum. The C# host supplies buffers and the pinned data; it does not make
token-boundary decisions for this component.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll scan-component self-host\Scanner.terrum input.txt --runtime fail-fast --json
```

The command executes compiled code in its own process. Supervising clients must
apply a deadline and treat abnormal termination as a runtime failure. When
scanning completes, JSON uses `schemaVersion: 2`, `component: "scanner-2"`,
`coordinateSystem: "utf-16"`, the pinned `unicodeVersion`, `sourceLength`,
`success`, and arrays of `tokens` and `diagnostics`. Each token has `kind`,
`start`, and `length`; each diagnostic has `code`, `start`, and `length`.
Lexically valid input returns `success: true` and exit code 0. Lexical errors
return `success: false` and exit code 1 but still publish the full token stream
and diagnostics on stdout. Without `--json`, token rows go to stdout and
diagnostic code/spans go to stderr. Input, compilation, or invalid-result
failures retain the standard schema-1 error response rather than producing a
scanner-2 token response.

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

Each output record is three initialized `int32` words: kind, UTF-16 start,
UTF-16 length. `Scan` returns the number of published records, including EOF.
Words beyond that count are not token records. `Host.Diagnostics[0]` contains
the diagnostic count; subsequent triples are numeric code (1–5 maps to
`TRM0001`–`TRM0005`), UTF-16 start and UTF-16 length, in reporting order.
The adapter validates published counts, codes, ranges and nonempty diagnostic
spans before returning them. A malformed token remains a token with its raw
source span; diagnostics do not replace source characters.

| Kind | Meaning |
|---|---|
| 0 | Zero-width EOF at source length |
| 1 | Identifier spelling, including keyword spellings without keyword classification |
| 2 | ASCII numeric spelling, including fractions, exponents and defined width tags |
| 3 | Punctuation/operator, including `== != <= >= && ||` |
| 4 | Unrecognized source scalar or malformed single surrogate unit |
| 5 | Raw quoted string spelling, including malformed/unterminated strings |

Identifiers use Unicode 17.0.0 XID_Start/XID_Continue plus underscore, including
supplementary scalars and the established Join_Control continuation policy.
Binary searches and UTF-16 decoding execute as compiled Terrum. Data comes from
the same generated `UnicodeXid` ranges as the host lexer; the public read-only
`UnicodeIdentifierProfile` carries the version and pinned input SHA-256.
No normalization, case folding or ambient host Unicode categories are involved.

Whitespace, newlines and non-nesting `//` and `/* ... */` comments follow the
host lexer and are skipped; malformed surrogates within comments and unterminated
block comments are diagnosed. Strings preserve raw spans across escapes;
unterminated strings stop before a newline, and invalid/incomplete escapes and
malformed surrogates produce matching diagnostic codes and spans. Numeric
fractions require a digit after `.`, exponents follow the host lexer's
identifier-disambiguation and incomplete-exponent rules, and defined primitive
width tags attach only at an identifier boundary. Spans retain original source
positions. This component still does not emit lossless trivia, classify
keywords, decode string values, type numeric values or construct the host's
full diagnostic objects/messages. It is not a full self-hosted lexer.

Shared fixtures compare the native scanner's token categories, exact spans and
lexical diagnostic codes/spans against the host lexer. They cover pinned-range
boundaries, supplementary identifiers, combining marks, non-ASCII digits,
Join_Control, comments, quoted escapes, malformed strings, numeric boundaries,
whitespace, malformed surrogate pairs and operators. Output-capacity
requirements are checked; the scanner does not silently truncate records.
