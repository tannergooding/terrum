# Source input and coordinates

This chapter owns the compilation-input transformation `source-input/1`.
The [lexical grammar](../lexical-grammar.md) owns tokens, trivia, identifier
properties and lexical recovery after a source snapshot exists.
Source file encoding does not select a default runtime text encoding.
These are logical rules, not new native records or CLI schema allocations.

## UTF-8 file input

A compilation file, including a byte stream supplied as standard input, is
UTF-8. Capture its bytes and source identity without locale/code-page
conversion. Do not detect an alternative encoding from a filename, host
setting, UTF-16/32 BOM or parser success.

Consume exactly one leading `EF BB BF` sequence at original byte offset zero
as the UTF-8 signature. Its three bytes do not become source characters.
A second such sequence, or one later in the input, is the scalar U+FEFF
and remains source text. An incomplete signature is ordinary decoder input,
not an ignored prefix. Record the consumed signature length, zero or three,
for origin coordinates.

The remaining input must consist entirely of the following scalar encodings.
Ranges are inclusive hexadecimal byte values; each row is one complete
scalar, not permission to terminate at an intermediate byte.

| First byte | Following bytes |
|---|---|
| `00..7F` | None |
| `C2..DF` | `80..BF` |
| `E0` | `A0..BF`, `80..BF` |
| `E1..EC` | `80..BF`, `80..BF` |
| `ED` | `80..9F`, `80..BF` |
| `EE..EF` | `80..BF`, `80..BF` |
| `F0` | `90..BF`, `80..BF`, `80..BF` |
| `F1..F3` | `80..BF`, `80..BF`, `80..BF` |
| `F4` | `80..8F`, `80..BF`, `80..BF` |

This is shortest-form scalar UTF-8: no overlong sequences, encoded
surrogates, values above U+10FFFF, isolated continuation bytes or five/six-byte
forms. Unicode noncharacters and U+0000 are valid decoder scalars; their
subsequent source uses still need lexical/literal validity. CESU-8, WTF-8 and
modified UTF-8 are not this input contract.
[RFC 3629 sections 3-4](https://www.rfc-editor.org/rfc/rfc3629.html#section-3)
gives the encoding definition and byte-range prior art.

Validate the complete input before publishing a source snapshot for lexing.
Malformed input is an input diagnostic, not text containing substituted
U+FFFD, an alternate encoding attempt or a successful partial source.
No compilation result or recovered token stream from that file is published.
Internal pipelining cannot change this externally observed boundary.
Chunk boundaries are not scalar boundaries: retain a possible incomplete
sequence until additional input or end of stream determines its validity.

## Malformed-byte diagnostics

The primary diagnostic identifies the first malformed subsequence in original
file byte order, after the optional signature. It supplies the source-input
identity, `source-input/1`, the invalid-UTF-8 category and a byte extent.
Byte coordinates are zero-based, end-exclusive, and include the original
signature in their offset basis.

For a byte not admitted as a first byte, the extent is that one byte.
For an admitted multibyte first byte, include it and the following bytes
that satisfy that row up to the first missing or disallowed byte. Exclude
that disallowed byte; if the input ends, include the available valid prefix.
No extent is empty. This locates the malformed prefix without consuming a
following ASCII character or valid scalar merely to enlarge the error.

| Input bytes | Primary byte extent |
|---|---|
| `80` or `C0 AF` | `[0, 1)` |
| `E0 80 80` or `ED A0 80` | `[0, 1)` |
| `E1 82 41` or truncated `E1 82` | `[0, 2)` |
| `F0 90 80 41` or truncated `F0 90 80` | `[0, 3)` |
| `EF BB BF 61 C0 AF` | `[4, 5)` |

There is no complete UTF-16 snapshot for an invalid-byte file. Do not
fabricate a UTF-16 span, line/column, source length or lexical diagnostic from
a partially decoded prefix. A tooling profile can provide separately labeled
byte-input details, but must not put byte values into UTF-16 fields.
Extra host/I/O diagnostics are not additional source characters.

Input-size limits, unavailable memory and stream/I/O failures have their own
input/resource categories. They are not invalid-UTF-8 findings unless the
observed bytes establish that error. Exact extent/length arithmetic must
not wrap; a constrained interface rejects an unrepresentable input/extent
rather than truncating it. Constant-evaluator limits are not file-decoder fuel.

## Immutable UTF-16 snapshot

Decode every valid scalar below U+10000 to its one UTF-16 unit; decode every
supplementary scalar to its standard high/low surrogate pair. No valid UTF-8
file can introduce an unpaired surrogate. Publish an immutable snapshot
and retain its source identity and origin transformation.

Preserve the scalar sequence exactly. Do not normalize Unicode, fold case,
replace controls, expand tabs, trim whitespace, normalize line endings or
remove interior U+FEFF. CRLF remains two units, although lexical line mapping
counts it as one break. Raw literal contents therefore preserve differences
such as CRLF versus LF and composed versus decomposed spelling.
Later literal interpretation applies only its explicitly defined transformations,
such as raw framing/indentation removal or a requested line-ending directive;
those do not normalize the input snapshot.
The pinned identifier property version comes from the lexical chapter;
the host's Unicode database cannot reinterpret the same snapshot.

The transformation can be described by the following explanatory operations;
their names do not register callable or intrinsic APIs:

```text
bytes = CaptureInputBytes(input);
signatureLength = StartsWithUtf8Signature(bytes) ? 3 : 0;
scalars = DecodeCompleteStrictUtf8(bytes[signatureLength..]);
snapshot = PreserveAsUtf16(scalars);
```

Escapes are not processed by this decoder. The six ASCII characters in
`\uD800` are well-formed source input; later string/character decoding
determines their validity in that literal context. The bytes `ED A0 80`
are instead invalid input before a token or escape exists.

## Coordinates and origin mapping

Token, trivia, syntax and ordinary source diagnostic extents use UTF-16
code-unit offsets/lengths within the snapshot. Line/column mapping follows
the lexical chapter, with zero-based internal line and code-unit column
coordinates. A separately declared human/tooling display can choose another
presentation; it cannot silently change the machine coordinate unit.

Snapshot offset zero is after a consumed UTF-8 signature. EOF is the
snapshot length, not the byte length. If tooling maps to original bytes,
retain the signature offset and exact scalar boundaries. A supplementary
scalar occupies two UTF-16 units but one indivisible UTF-8 byte sequence:
the boundary between its surrogate units has no corresponding byte boundary.
An exact-boundary translation reports that case explicitly; it must not
invent a half-scalar byte offset. A separately named enclosing-range
translation must identify that it expands the requested extent.

For example, original bytes `61 F0 9F 98 80 0D 0A` produce snapshot units
`0061 D83D DE00 000D 000A`. The supplementary scalar occupies UTF-16
`[1, 3)` and bytes `[1, 5)`; UTF-16 offset two is not a byte boundary.
The next line begins at UTF-16 offset five and original byte offset seven.
Prepending a consumed signature adds three to byte coordinates, not to the
snapshot offsets, token lengths or columns.

Content identities and dependency records must identify what they cover:
original bytes, decoded snapshot or a declared transform. They are not
interchangeable merely because two inputs decode to equal text, such as
with and without a leading signature. There is no implicit permission to
reuse a source-origin-dependent result under another path/coordinate map.

## Already-decoded snapshot APIs

A facility that accepts an immutable UTF-16 snapshot accepts those code units,
not another file encoding. It does not re-decode, replace malformed units,
strip an initial U+FEFF or normalize text. It has no original byte coordinate
map unless one is explicitly supplied and validated for that snapshot.

Such a lexer facility retains unpaired surrogate units and diagnoses them,
including inside comments and strings, under the existing lexical recovery
contract. A compilation using that invalid snapshot fails; the retained
recovery text is not valid scalar source or a constant literal value.
A valid pair is consumed together where the lexical rule recognizes a scalar.
This API's diagnosed malformed-unit behavior does not authorize encoded
surrogates in UTF-8 file input.

[Conformance cases](../conformance/source-input.md) cover valid boundary
encodings, malformed-byte extents, signatures, preservation, snapshot
coordinates and the distinct already-decoded entry.
