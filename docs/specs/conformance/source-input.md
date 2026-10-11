# Source-input conformance cases

These cases apply to [source-input/1](../language/source-input.md).
Decoder success alone does not make the decoded text a syntactically valid
program. Byte sequences are hexadecimal; UTF-16 units and Unicode scalars
are explicit rather than host string-rendering conventions.

## Valid scalar encodings

| Case | Bytes | Required decoding |
|---|---|---|
| INPUT-01 | Empty input | Empty snapshot, EOF offset zero |
| INPUT-02 | `00 41 7F` | Units `0000 0041 007F`; later lexical validity is separate |
| INPUT-03 | `C2 80` | U+0080, one UTF-16 unit |
| INPUT-04 | `DF BF` | U+07FF, one UTF-16 unit |
| INPUT-05 | `E0 A0 80` | U+0800, one UTF-16 unit |
| INPUT-06 | `ED 9F BF` | U+D7FF, one UTF-16 unit |
| INPUT-07 | `EE 80 80` | U+E000, one UTF-16 unit |
| INPUT-08 | `EF BF BF` | U+FFFF noncharacter is a valid decoder scalar |
| INPUT-09 | `F0 90 80 80` | U+10000, units `D800 DC00` |
| INPUT-10 | `F4 8F BF BF` | U+10FFFF, units `DBFF DFFF`; valid noncharacter |
| INPUT-11 | A four-byte scalar is split across stream chunks | Wait for its remaining bytes; same snapshot as contiguous input |
| INPUT-12 | CR and LF arrive in different stream chunks | Preserve both units; lexical mapping still has one CRLF break |
| INPUT-13 | `EF BF BD` | Explicit U+FFFD is valid source data; it is not decoder replacement |

## Invalid input and exact extents

All cases below reject before lexing, with no replacement character or
successful partial snapshot. Extents are in original input bytes.

| Case | Bytes | Primary extent/result |
|---|---|---|
| INBAD-01 | `80` | `[0, 1)`, isolated continuation |
| INBAD-02 | `BF` | `[0, 1)`, isolated continuation |
| INBAD-03 | `C0 AF` | `[0, 1)`, forbidden overlong first byte |
| INBAD-04 | `C1 BF` | `[0, 1)`, forbidden first byte |
| INBAD-05 | `F5 80 80 80` | `[0, 1)`, forbidden first byte |
| INBAD-06 | `FF` | `[0, 1)`, forbidden first byte |
| INBAD-07 | `E0 80 80` | `[0, 1)`, overlong three-byte form |
| INBAD-08 | `F0 80 80 80` | `[0, 1)`, overlong four-byte form |
| INBAD-09 | `ED A0 80` | `[0, 1)`, encoded U+D800 surrogate |
| INBAD-10 | `ED AF BF` | `[0, 1)`, encoded U+DBFF surrogate |
| INBAD-11 | `ED BF BF` | `[0, 1)`, encoded U+DFFF surrogate |
| INBAD-12 | `F4 90 80 80` | `[0, 1)`, above U+10FFFF |
| INBAD-13 | `C2` | `[0, 1)`, truncated two-byte form |
| INBAD-14 | `E1 82` | `[0, 2)`, truncated three-byte form |
| INBAD-15 | `F0 90 80` | `[0, 3)`, truncated four-byte form |
| INBAD-16 | `C2 41` | `[0, 1)`; do not consume following ASCII A |
| INBAD-17 | `E1 82 41` | `[0, 2)`; do not consume following ASCII A |
| INBAD-18 | `F0 90 80 41` | `[0, 3)`; do not consume following ASCII A |
| INBAD-19 | `61 E1 82` | `[1, 3)`; no fabricated UTF-16/line span for the prefix |
| INBAD-20 | `ED A1 8C ED BE B4` | `[0, 1)`; CESU-8 pair is not scalar UTF-8 |
| INBAD-21 | `22 61 C0 AF` | Input error `[2, 3)`, not a published unterminated-string recovery |
| INBAD-22 | `EF BB BF 61 C0 AF` | Input error `[4, 5)`; offsets include consumed signature |
| INBAD-23 | A possible prefix is followed by end-of-stream | Report its available malformed extent; no indefinite wait |
| INBAD-24 | Stream I/O or size limit fails without proving an invalid sequence | Separate input/resource error; no invented invalid-UTF-8 finding |

## Signatures and preservation

| Case | Situation | Required result |
|---|---|---|
| INBOM-01 | `EF BB BF` only | Empty snapshot; original-byte EOF is three |
| INBOM-02 | `EF BB BF 41` | Snapshot is A at UTF-16 offset zero; original byte offset three |
| INBOM-03 | `EF BB BF EF BB BF 41` | Strip once; snapshot begins U+FEFF then A |
| INBOM-04 | `41 EF BB BF 42` | Preserve interior U+FEFF |
| INBOM-05 | Truncated `EF BB` | Invalid UTF-8 extent `[0, 2)`, not an ignored signature |
| INBOM-06 | `FF FE 41 00` | Invalid first byte `[0, 1)`; no UTF-16 autodetection |
| INBOM-07 | `FE FF 00 41` | Invalid first byte `[0, 1)`; no big-endian autodetection |
| INBOM-08 | `FF FE 00 00` | Invalid first byte `[0, 1)`; no UTF-32 autodetection |
| INBOM-09 | Composed U+00E9 versus U+0065 U+0301 | Distinct preserved snapshots/spellings; no Unicode normalization |
| INBOM-10 | CRLF, CR, LF, NEL, LS and PS occur | Preserve their units; lexical line map applies its exact break rules |
| INBOM-11 | Tabs, trailing spaces and raw literal line endings occur | No decoder expansion, trimming or normalization; later explicitly defined literal transforms remain separate |
| INBOM-12 | A byte source with/without a signature has equal decoded text | Keep distinct byte origins/content identities; no origin-map equivalence |

## Coordinates and already-decoded input

| Case | Situation | Required result |
|---|---|---|
| INPOS-01 | `61 F0 9F 98 80 0D 0A` | Units `0061 D83D DE00 000D 000A`, snapshot length five |
| INPOS-02 | Supplementary scalar in that fixture is selected | UTF-16 `[1, 3)`; original bytes `[1, 5)` |
| INPOS-03 | Exact translation requests UTF-16 offset two | Explicit non-byte-boundary result, not a guessed offset |
| INPOS-04 | The fixture's next line/EOF is located | UTF-16 offset five, zero-based line one/column zero; byte offset seven |
| INPOS-05 | A signature prefixes that fixture | Same UTF-16 ranges/columns; original byte coordinates add three |
| INPOS-06 | An invalid-byte file's input diagnostic is serialized | Distinct byte-input category/unit; never put its bytes into UTF-16 source fields |
| INPOS-07 | Already-decoded snapshot starts with U+FEFF | Preserve it; no file-signature stripping |
| INPOS-08 | Already-decoded snapshot contains an unpaired high surrogate | Retain unit, lexical diagnostic and failed compilation |
| INPOS-09 | Already-decoded snapshot contains an unpaired low surrogate in a comment | Retain trivia text and diagnose that unit |
| INPOS-10 | Already-decoded snapshot contains an unpaired surrogate in a string | Retain token text and diagnose that unit |
| INPOS-11 | Already-decoded snapshot contains a valid surrogate pair | Scalar recognition advances together under the lexical rule |
| INPOS-12 | Already-decoded input has no supplied validated byte origin | No fabricated byte-coordinate map |
| INPOS-13 | ASCII source spells `\uD800` | Valid file decoding; literal/escape rules decide subsequent validity |
| INPOS-14 | A host Unicode version or decoder fallback setting differs | Same source-input rules and pinned lexical identifier properties |
