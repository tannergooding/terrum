# Compiled Terrum scanner component

`self-host/Scanner.terrum` is a small executable scanner component, not a second
complete front end or a bootstrapped compiler. It implements scalar decoding,
Unicode identifier classification, decimal-integer scanning, whitespace
recognition and punctuation boundaries in Terrum. The C# host supplies buffers
and the pinned data; it does not make token-boundary decisions for this component.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll scan-component self-host\Scanner.terrum input.txt --runtime fail-fast --json
```

The command executes compiled code in its own process. Supervising clients must
apply a deadline and treat abnormal termination as a runtime failure. Successful
JSON uses schema 1 and component `scanner-1`; it includes the pinned Unicode
version and token records. Invalid source and invalid result records produce
diagnostics rather than a successful token response.

## Storage and host boundary

The compilation host explicitly grants named fixed-buffer capabilities through
`HostBuffer` descriptors. The scanner adapter grants:

| Capability | Element type | Access | Purpose |
|---|---|---|---|
| `Host.Source` | `char16` | Read-only | Exact source snapshot code units, including malformed surrogates |
| `Host.XidStart` | `int32` | Read-only | Sorted inclusive range pairs from the host lexer's pinned XID_Start table |
| `Host.XidContinue` | `int32` | Read-only | The corresponding XID_Continue ranges |
| `Host.Tokens` | `int32` | Read/write | Initialized words for token records |

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
Words beyond that count are not token records.

| Kind | Meaning |
|---|---|
| 0 | Zero-width EOF at source length |
| 1 | Identifier spelling, including keyword spellings without keyword classification |
| 2 | Untagged ASCII decimal-integer spelling |
| 3 | Punctuation/operator, including `== != <= >= && ||` |
| 4 | Unrecognized source scalar or malformed single surrogate unit |

Identifiers use Unicode 17.0.0 XID_Start/XID_Continue plus underscore, including
supplementary scalars and the established Join_Control continuation policy.
Binary searches and UTF-16 decoding execute as compiled Terrum. Data comes from
the same generated `UnicodeXid` ranges as the host lexer; the public read-only
`UnicodeIdentifierProfile` carries the version and pinned input SHA-256.
No normalization, case folding or ambient host Unicode categories are involved.

Whitespace uses the host source-whitespace profile and is skipped. Spans retain
their positions in the original source. This component does not emit lossless
trivia, classify keywords, recognize comments or strings, parse numeric tags,
fractions or exponents, or emit the host lexer's diagnostic objects. Those
inputs are outside its tokenization contract, not evidence that the full lexer
is self-hosted. Malformed surrogates and otherwise disallowed scalars are within
the contract and produce invalid records without replacing the input.

Shared fixtures compare the native scanner's output against normalized host
lexer token kinds and exact spans **only within this documented subset**.
Fixtures include pinned-range boundaries, supplementary identifiers, combining
marks, non-ASCII digits, Join_Control, whitespace, malformed surrogate pairs and
operator boundaries. The scanner's output-capacity requirement is checked; it
does not silently truncate records.
